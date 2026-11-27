using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HalconVisionStudio.AI.Config;
using HalconVisionStudio.Shared.Diagnostics;

namespace HalconVisionStudio.AI.Copilot
{
    public class LlmCompletionResponse
    {
        public string Text { get; set; } = "";
        public List<CopilotToolCall> ToolCalls { get; set; } = new List<CopilotToolCall>();
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
        public string ErrorMessage { get; set; }
        public bool Success => string.IsNullOrEmpty(ErrorMessage);
    }

    public class OpenAiCompatibleClient
    {
        private readonly HttpClient _httpClient;

        /// <summary>瞬态错误最大重试次数（429 / 5xx / 网络异常）。智谱网关偶发 502，提高到 5 次降低失败率。</summary>
        private const int MaxRetries = 5;

        /// <summary>错误提示最多读取的响应字符数。</summary>
        private const int ErrorBodyLimit = 400;

        /// <summary>附加图片的单张大小上限（base64 后约 12MB，避免撑爆上下文）。</summary>
        private const int MaxImageBase64Length = 12 * 1024 * 1024;

        public OpenAiCompatibleClient(HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        public async Task<LlmCompletionResponse> StreamChatAsync(
            AiSettings settings,
            List<CopilotMessage> messages,
            List<object> toolDefinitions,
            Action<string> onDeltaText = null,
            CancellationToken cancellationToken = default,
            Action<string> onDeltaReasoning = null)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return new LlmCompletionResponse { ErrorMessage = "未配置大模型 API Base URL" };
            }

            var requestUrl = settings.BaseUrl.TrimEnd('/');
            if (!requestUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                requestUrl += "/chat/completions";
            }

            List<Dictionary<string, object>> requestMessages;
            try
            {
                requestMessages = BuildRequestMessages(messages);
            }
            catch (Exception ex)
            {
                return new LlmCompletionResponse { ErrorMessage = $"构建请求消息失败: {ex.Message}" };
            }

            var payload = new Dictionary<string, object>
            {
                ["model"] = string.IsNullOrWhiteSpace(settings.ModelName) ? "deepseek-chat" : settings.ModelName,
                ["messages"] = requestMessages,
                ["stream"] = true,
                ["temperature"] = settings.Temperature,
                ["max_tokens"] = settings.MaxTokens > 0 ? settings.MaxTokens : 4096
            };

            if (toolDefinitions != null && toolDefinitions.Count > 0)
            {
                payload["tools"] = toolDefinitions;
                payload["tool_choice"] = "auto";
            }

            var jsonBody = AiJson.Serialize(payload);

            HttpResponseMessage response = null;
            string lastError = null;

            // 瞬态错误重试：429 限流 / 5xx 服务端错误 / 网络异常，指数退避。
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                }

                try
                {
                    var received = await _httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (received.IsSuccessStatusCode)
                    {
                        response = received;
                        break;
                    }

                    using var failedResponse = received;

                    var status = (int)failedResponse.StatusCode;
                    if (status != 429 && status < 500)
                    {
                        var errorText = await ReadErrorBodyAsync(failedResponse, settings.ApiKey, cancellationToken);
                        ExecutionLogService.AiChat($"模型接口返回错误 (HTTP {status})", ExecLogLevel.Error);
                        return new LlmCompletionResponse
                        {
                            ErrorMessage = $"模型接口返回错误 (HTTP {status}): {errorText}{BuildApiKeyHint(errorText)}"
                        };
                    }

                    lastError = $"HTTP {status}";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex.GetType().Name;
                }

                if (attempt < MaxRetries)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            if (response == null)
            {
                ExecutionLogService.AiChat(
                    $"请求模型服务失败（已重试 {MaxRetries} 次）",
                    ExecLogLevel.Error, $"最后错误: {lastError}");
                return new LlmCompletionResponse
                {
                    ErrorMessage = $"请求模型服务失败（已重试 {MaxRetries} 次）: {lastError}"
                };
            }

            var fullText = new StringBuilder();
            var toolCallsByIndex = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                        {
                            continue;
                        }

                        var choice = choices[0];
                        if (!choice.TryGetProperty("delta", out var delta)) continue;

                        // 文本块流式增量
                        if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                        {
                            var chunk = contentProp.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                fullText.Append(chunk);
                                onDeltaText?.Invoke(chunk);
                            }
                        }

                        // 推理模型思考流（GLM/DeepSeek 的 reasoning_content）：不解析会导致
                        // 模型思考阶段界面长时间空白（content 为空），看起来像"执行到一半卡住"。
                        if (onDeltaReasoning != null &&
                            delta.TryGetProperty("reasoning_content", out var reasoningProp) &&
                            reasoningProp.ValueKind == JsonValueKind.String)
                        {
                            var reasoningChunk = reasoningProp.GetString();
                            if (!string.IsNullOrEmpty(reasoningChunk))
                            {
                                onDeltaReasoning(reasoningChunk);
                            }
                        }

                        // 工具调用增量
                        if (delta.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tcElem in toolCallsProp.EnumerateArray())
                            {
                                int index = 0;
                                if (tcElem.TryGetProperty("index", out var idxProp))
                                {
                                    index = idxProp.GetInt32();
                                }

                                if (!toolCallsByIndex.ContainsKey(index))
                                {
                                    toolCallsByIndex[index] = ("", "", new StringBuilder());
                                }

                                var (currId, currName, currArgs) = toolCallsByIndex[index];

                                if (tcElem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                                {
                                    currId = idProp.GetString();
                                }

                                if (tcElem.TryGetProperty("function", out var funcProp))
                                {
                                    if (funcProp.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                                    {
                                        currName = nameProp.GetString();
                                    }
                                    if (funcProp.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                                    {
                                        currArgs.Append(argsProp.GetString());
                                    }
                                }

                                toolCallsByIndex[index] = (currId, currName, currArgs);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略单个残缺流式 chunk
                    }
                }
            }

            var finalToolCalls = new List<CopilotToolCall>();
            foreach (var kvp in toolCallsByIndex)
            {
                finalToolCalls.Add(new CopilotToolCall
                {
                    Id = string.IsNullOrEmpty(kvp.Value.Id) ? Guid.NewGuid().ToString("N") : kvp.Value.Id,
                    Name = kvp.Value.Name,
                    Arguments = kvp.Value.Args.ToString(),
                    Status = ToolExecutionStatus.Running
                });
            }

            return new LlmCompletionResponse
            {
                Text = fullText.ToString(),
                ToolCalls = finalToolCalls
            };
        }

        /// <summary>
        /// 将内部消息模型转换为 OpenAI 兼容请求格式。
        /// 带图片的用户消息会展开为 content 数组（text + image_url data URI），实现多模态输入。
        /// </summary>
        private static List<Dictionary<string, object>> BuildRequestMessages(List<CopilotMessage> messages)
        {
            var requestMessages = new List<Dictionary<string, object>>();

            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];

                // GLM 等实现不支持单条助手消息内的并行 tool_calls（报 1214 messages 参数非法；
                // DeepSeek/Gemini 宽容）。拆为顺序结构：每个 tool_call 独立成一条助手消息，
                // 其响应紧跟其后 —— 对 OpenAI 规范同样合法，宽容派不受影响。
                if (m.Role == MessageRole.Assistant && m.HasToolCalls && m.ToolCalls.Count > 1)
                {
                    var responses = new Dictionary<string, CopilotMessage>();
                    int j = i + 1;
                    while (j < messages.Count && messages[j].Role == MessageRole.Tool)
                    {
                        responses[messages[j].ToolCallId] = messages[j];
                        j++;
                    }

                    var isFirst = true;
                    foreach (var tc in m.ToolCalls)
                    {
                        var callDict = new Dictionary<string, object> { ["role"] = "assistant" };
                        // content 字段必须存在（GLM 校验），首条带原文，其余为 null。
                        callDict["content"] = isFirst && !string.IsNullOrEmpty(m.Content) ? m.Content : null;

                        callDict["tool_calls"] = new List<object>
                        {
                            new
                            {
                                id = tc.Id,
                                type = "function",
                                function = new { name = tc.Name, arguments = tc.Arguments }
                            }
                        };
                        requestMessages.Add(callDict);

                        if (responses.TryGetValue(tc.Id, out var response))
                        {
                            requestMessages.Add(BuildToolMessage(response));
                        }

                        isFirst = false;
                    }

                    i = j - 1; // 跳过已消费的内联 tool 响应
                    continue;
                }

                var dict = new Dictionary<string, object>
                {
                    ["role"] = m.Role.ToString().ToLowerInvariant()
                };

                if (m.Role == MessageRole.Tool)
                {
                    // 严格格式：tool 角色只发 role/tool_call_id/content。
                    requestMessages.Add(BuildToolMessage(m));
                    continue;
                }

                if (m.Role == MessageRole.Assistant && m.HasToolCalls)
                {
                    // OpenAI 官方格式：带 tool_calls 的助手消息 content 显式为 null
                    // （部分实现如 GLM 要求 content 字段必须存在，省略或空串都可能被拒）。
                    dict["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content;
                    var calls = new List<object>();
                    foreach (var tc in m.ToolCalls)
                    {
                        calls.Add(new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new
                            {
                                name = tc.Name,
                                arguments = tc.Arguments
                            }
                        });
                    }
                    dict["tool_calls"] = calls;
                }
                else if (m.Role == MessageRole.User && m.HasImages)
                {
                    // 多模态：文本 + 图片（data URI base64）
                    var contentParts = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                    {
                        contentParts.Add(new { type = "text", text = m.Content });
                    }

                    foreach (var imagePath in m.Images)
                    {
                        var dataUri = TryLoadImageAsDataUri(imagePath);
                        if (dataUri != null)
                        {
                            contentParts.Add(new
                            {
                                type = "image_url",
                                image_url = new { url = dataUri }
                            });
                        }
                    }

                    if (contentParts.Count == 0)
                    {
                        dict["content"] = m.Content ?? "";
                    }
                    else
                    {
                        dict["content"] = contentParts;
                    }
                }
                else
                {
                    dict["content"] = m.Content ?? "";
                }

                requestMessages.Add(dict);
            }

            return requestMessages;
        }

        /// <summary>严格格式的 tool 响应消息：只含 role/tool_call_id/content（无 name 等规范外字段）。</summary>
        private static Dictionary<string, object> BuildToolMessage(CopilotMessage m) => new()
        {
            ["role"] = "tool",
            ["tool_call_id"] = m.ToolCallId ?? "",
            ["content"] = m.Content ?? ""
        };

        private static string TryLoadImageAsDataUri(string imagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(imagePath);
                var base64 = Convert.ToBase64String(bytes);
                if (base64.Length > MaxImageBase64Length)
                {
                    return null;
                }

                var mime = imagePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           imagePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                    ? "image/jpeg"
                    : "image/png";
                return $"data:{mime};base64,{base64}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 密钥类错误（智谱 1214 / OpenAI invalid_api_key / DeepSeek Authentication 等）附加可操作提示：
        /// 各服务商 API Key 互不通用，切换服务商后需使用对应密钥。
        /// </summary>
        private static string BuildApiKeyHint(string errorBody)
        {
            var text = errorBody ?? "";
            // 只按消息文本匹配密钥类错误——智谱的 1214 也用于"参数非法"，裸匹配错误码会误报。
            if (text.Contains("API Key无效", StringComparison.Ordinal) ||
                text.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Incorrect API key", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Authentication Fails", StringComparison.OrdinalIgnoreCase))
            {
                return "\n（提示：各服务商的 API Key 互不通用——请到 AI 设置检查 ③ API Key 是否属于当前服务商；切换服务商时密钥会自动换载，新服务商首次使用需填入其专属 Key）";
            }
            return "";
        }

        private static async Task<string> ReadErrorBodyAsync(
            HttpResponseMessage response,
            string apiKey,
            CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var key = apiKey?.Trim();
                // 额外读取密钥的尾部，避免错误提示的截断位置正好切在密钥中间。
                var size = ErrorBodyLimit + 1 + Math.Min(key?.Length ?? 0, 4096);
                var buffer = new char[size];
                var count = await reader.ReadBlockAsync(buffer.AsMemory(), timeout.Token);
                var text = new string(buffer, 0, count);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return "（无响应内容）";
                }

                if (!string.IsNullOrEmpty(key))
                {
                    if (key.Length > 4096)
                    {
                        return "（服务返回错误，请检查模型配置）";
                    }

                    text = text.Replace(key, "[已隐藏]", StringComparison.Ordinal);
                }

                return text.Length > ErrorBodyLimit ? text[..ErrorBodyLimit] + "…" : text;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return "（读取响应内容失败）";
            }
        }
    }
}

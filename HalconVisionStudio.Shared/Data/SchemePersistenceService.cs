using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HalconVisionStudio.Shared.Data
{
    /// <summary>
    /// 方案持久化服务，提供视觉工程方案数据的 JSON 序列化/反序列化与文件系统存储。
    /// <para>
    /// 默认存储目录：我的文档/HalconVisionStudio/Schemes/
    /// 文件格式：UTF-8 编码的 JSON，使用小驼峰命名，枚举值以字符串格式持久化。
    /// </para>
    /// </summary>
    public static class SchemePersistenceService
    {
        /// <summary>
        /// JSON 序列化选项：
        /// - 格式化缩进排版（便于人工阅读）
        /// - 属性名采用小驼峰规范（camelCase）
        /// - null 值不写入 JSON
        /// - 枚举采用字符串格式序列化（如 "on" 而非 0）
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        /// <summary>
        /// 方案文件的默认存储目录（位于当前用户的“我的文档/HalconVisionStudio/Schemes/”）。
        /// 目录不存在时在调用 <see cref="SaveAsync"/> 时会自动创建。
        /// </summary>
        public static string DefaultSchemeDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HalconVisionStudio", "Schemes");

        /// <summary>先写入同目录临时文件，完成后一次性替换方案。</summary>
        public static async Task SaveAsync(string filePath, SchemeData scheme)
        {
            var json = JsonSerializer.Serialize(scheme, JsonOptions);
            var target = Path.GetFullPath(filePath);
            var temporary = CreateTemporaryPath(target);
            try
            {
                await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                DeleteTemporaryFile(temporary);
            }
        }

        /// <summary>同步保存，供示例方案生成等同步入口调用。</summary>
        public static void Save(string filePath, SchemeData scheme)
        {
            var target = Path.GetFullPath(filePath);
            var json = JsonSerializer.Serialize(scheme, JsonOptions);
            var temporary = CreateTemporaryPath(target);
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                DeleteTemporaryFile(temporary);
            }
        }

        private static string CreateTemporaryPath(string target)
        {
            var directory = Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $".visionflow-{Guid.NewGuid():N}.tmp");
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 保留原始保存异常。
            }
            catch (UnauthorizedAccessException)
            {
                // 保留原始保存异常。
            }
        }

        public static async Task<SchemeData> LoadAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var scheme = JsonSerializer.Deserialize<SchemeData>(json, JsonOptions)
                ?? throw new InvalidDataException("方案文件没有有效的方案对象。");
            if (scheme.Version > SchemeData.CurrentVersion)
            {
                throw new InvalidDataException($"方案版本 v{scheme.Version} 高于程序支持的版本。");
            }

            return scheme;
        }

        /// <summary>
        /// 根据方案名称获取默认的完整文件路径（位于 <see cref="DefaultSchemeDirectory"/> 下）。
        /// 名称中的非法文件名字符会自动被替换为下划线。
        /// </summary>
        /// <param name="schemeName">方案名称。</param>
        /// <returns>默认文件路径（如 .../Schemes/我的方案.json）。</returns>
        public static string GetDefaultFilePath(string schemeName)
        {
            return Path.Combine(DefaultSchemeDirectory, $"{SanitizeFileName(schemeName)}.json");
        }

        /// <summary>
        /// 列出 <see cref="DefaultSchemeDirectory"/> 目录下所有已保存的方案文件路径。
        /// 若目录不存在则返回空数组。
        /// </summary>
        /// <returns>方案 JSON 文件的完整路径数组。</returns>
        public static string[] ListSavedSchemes()
        {
            if (!Directory.Exists(DefaultSchemeDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(DefaultSchemeDirectory, "*.json");
        }

        /// <summary>
        /// 将字符串中的非法文件名字符替换为下划线。
        /// 若处理后为空，则返回 "untitled"。
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "untitled" : name;
        }
    }
}

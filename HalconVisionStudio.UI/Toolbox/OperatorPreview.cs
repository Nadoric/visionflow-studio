using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using HalconVisionStudio.Shared;

namespace HalconVisionStudio.UI.Controls
{
    internal sealed class OperatorPreview
    {
        private const string CatalogResource = "HalconVisionStudio.HalconOperatorReference.json";
        private static readonly Lazy<Catalog> Reference = new(LoadCatalog);

        public OperatorPreview(IHandleRegisterModel model)
        {
            Name = model.HandleName;
            Description = model.HandleDescription;
            Icon = model.IconGeometry;
            NodeParameters = GetNodeParameters(model.ParameterType);

            var catalog = Reference.Value;
            if (catalog == null)
            {
                Note = "HALCON 参考资料加载失败。";
                Calls = [];
                return;
            }

            if (!catalog.Handles.TryGetValue(model.Key, out var entry))
            {
                Note = "该插件尚未提供 HALCON 算子对应关系。";
                Calls = [];
                return;
            }

            Calls = entry.Calls
                .Select(method => new NativeOperator(method, catalog.Operators[method]))
                .ToArray();

            Note = Calls.Length == 0
                ? "此节点由 VisionFlow 实现，没有对应的 HALCON 原始算子。"
                : entry.Note;
        }

        public string Name { get; }
        public string Description { get; }
        public Geometry Icon { get; }
        public string Note { get; }
        public NativeOperator[] Calls { get; }
        public PreviewParameter[] NodeParameters { get; }

        private static Catalog LoadCatalog()
        {
            try
            {
                using var stream = typeof(OperatorPreview).Assembly.GetManifestResourceStream(CatalogResource);
                return stream == null
                    ? null
                    : JsonSerializer.Deserialize<Catalog>(stream, JsonSerializerOptions.Web);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Debug.WriteLine($"读取 HALCON 算子参考资料失败：{ex.Message}");
                return null;
            }
        }

        private static PreviewParameter[] GetNodeParameters(Type parameterType)
        {
            if (parameterType == null)
            {
                return [];
            }

            return parameterType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetMethod?.IsPublic == true &&
                    property.GetIndexParameters().Length == 0 &&
                    (property.SetMethod?.IsPublic == true ||
                        property.IsDefined(typeof(DisplayNameAttribute))))
                .Select(CreateNodeParameter)
                .ToArray();
        }

        private static PreviewParameter CreateNodeParameter(PropertyInfo property)
        {
            var label = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var range = property.GetCustomAttribute<RangeAttribute>();
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var details = new List<string>();

            if (range != null)
            {
                details.Add($"范围 {range.Minimum} ～ {range.Maximum}");
            }

            if (type.IsEnum)
            {
                var options = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field => field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? field.Name);
                details.Add($"可选值 {string.Join("、", options)}");
            }

            var name = string.IsNullOrWhiteSpace(label) || label == property.Name
                ? property.Name
                : $"{label}  ·  {property.Name}";

            return new PreviewParameter(name, FormatType(type), description, string.Join("　", details));
        }

        private static string FormatType(Type type)
        {
            if (type.IsGenericType)
            {
                var name = type.Name.Split('`')[0];
                var arguments = string.Join(", ", type.GetGenericArguments().Select(FormatType));
                return $"{name}<{arguments}>";
            }

            return type.Name;
        }

        internal sealed class NativeOperator
        {
            public NativeOperator(string method, OperatorDefinition definition)
            {
                Name = definition.Name;
                DotNetName = $"HOperatorSet.{method}";
                ReferenceUri = new Uri($"https://www.mvtec.com/doc/halcon/2605/en/{Name}.html");

                // HDevelop 以冒号分隔四类参数，包括空组。
                string[] roles = ["input_object", "output_object", "input_control", "output_control"];
                var groups = roles.Select(role => string.Join(", ", definition.Parameters
                    .Where(parameter => parameter.Role == role)
                    .Select(parameter => parameter.Name)));
                Signature = $"{Name}({string.Join(" : ", groups)})";

                Parameters = definition.Parameters.Select(parameter =>
                {
                    var direction = parameter.Role switch
                    {
                        "input_object" => "输入对象",
                        "output_object" => "输出对象",
                        "input_control" => "输入控制",
                        "output_control" => "输出控制",
                        _ => parameter.Role
                    };
                    var info = string.IsNullOrEmpty(parameter.Type)
                        ? direction
                        : $"{direction}  ·  {parameter.Type}";
                    var details = new List<string>();

                    if (!string.IsNullOrEmpty(parameter.Default))
                    {
                        details.Add($"HALCON 默认 {parameter.Default}");
                    }

                    if (!string.IsNullOrEmpty(parameter.Constraint))
                    {
                        details.Add($"约束 {parameter.Constraint}");
                    }

                    return new PreviewParameter(
                        parameter.Name, info, parameter.Description, string.Join("　", details));
                }).ToArray();
            }

            public string Name { get; }
            public string DotNetName { get; }
            public string Signature { get; }
            public Uri ReferenceUri { get; }
            public PreviewParameter[] Parameters { get; }
        }

        internal sealed record PreviewParameter(
            string Name, string Info, string Description, string Details);

        private sealed record Catalog(
            Dictionary<string, HandleEntry> Handles, Dictionary<string, OperatorDefinition> Operators);

        private sealed record HandleEntry(string[] Calls, string Note);

        internal sealed record OperatorDefinition(string Name, NativeParameter[] Parameters);

        internal sealed record NativeParameter(
            string Name, string Role, string Type, string Description, string Default, string Constraint);
    }
}

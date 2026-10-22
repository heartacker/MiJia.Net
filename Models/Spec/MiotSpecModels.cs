using System.Text.Json.Serialization;

namespace MiJia.Net.Models.Spec;

/// <summary>
/// MIoT Spec V2 设备实例规格定义
/// </summary>
public sealed class MiotSpecInstance
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 短标识名（例如 urn:miot-spec-v2:service:light:00007802:yeelink-v1:1 解析为 "light"）
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return string.Empty;
            var parts = Type.Split(':');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
    }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("services")]
    public List<MiotSpecService> Services { get; set; } = [];
}

/// <summary>
/// 服务定义 (Service)
/// </summary>
public sealed class MiotSpecService
{
    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 短标识名（例如 urn:miot-spec-v2:service:light:00007802:yeelink-v1:1 解析为 "light"）
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return string.Empty;
            var parts = Type.Split(':');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
    }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public List<MiotSpecProperty>? Properties { get; set; }

    [JsonPropertyName("actions")]
    public List<MiotSpecAction>? Actions { get; set; }
}

/// <summary>
/// 属性定义 (Property)
/// </summary>
public sealed class MiotSpecProperty
{
    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 短标识名（例如 urn:miot-spec-v2:service:light:00007802:yeelink-v1:1 解析为 "light"）
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return string.Empty;
            var parts = Type.Split(':');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
    }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 数据格式：bool, uint8, int32, float, string 等
    /// </summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// 访问权限：read, write, notify
    /// </summary>
    [JsonPropertyName("access")]
    public List<string> Access { get; set; } = [];

    /// <summary>
    /// 单位：percentage, celsius, lux, seconds 等
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// 数值范围与步长 [min, max, step]
    /// </summary>
    [JsonPropertyName("value-range")]
    public List<double>? ValueRange { get; set; }

    /// <summary>
    /// 枚举值列表
    /// </summary>
    [JsonPropertyName("value-list")]
    public List<MiotSpecValueListItem>? ValueList { get; set; }

    [JsonIgnore]
    public bool CanRead => Access.Contains("read");

    [JsonIgnore]
    public bool CanWrite => Access.Contains("write");

    [JsonIgnore]
    public bool CanNotify => Access.Contains("notify");
}

/// <summary>
/// 枚举项
/// </summary>
public sealed class MiotSpecValueListItem
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 动作定义 (Action)
/// </summary>
public sealed class MiotSpecAction
{
    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 短标识名（例如 urn:miot-spec-v2:service:light:00007802:yeelink-v1:1 解析为 "light"）
    /// </summary>
    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return string.Empty;
            var parts = Type.Split(':');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }
    }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 输入参数关联的 Property IID 列表
    /// </summary>
    [JsonPropertyName("in")]
    public List<int> In { get; set; } = [];

    /// <summary>
    /// 输出参数关联的 Property IID 列表
    /// </summary>
    [JsonPropertyName("out")]
    public List<int> Out { get; set; } = [];
}

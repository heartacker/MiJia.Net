using System.Text.Json.Serialization;

namespace MiJia.Net.Models.Devices;

/// <summary>
/// 请求读取设备属性的目标项
/// </summary>
public sealed class MiotPropGetTarget
{
    [JsonPropertyName("did")]
    public required string Did { get; set; }

    [JsonPropertyName("siid")]
    public required int Siid { get; set; }

    [JsonPropertyName("piid")]
    public required int Piid { get; set; }
}

/// <summary>
/// 设备属性读取结果
/// </summary>
public sealed class MiotPropGetResult
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    [JsonPropertyName("siid")]
    public int Siid { get; set; }

    [JsonPropertyName("piid")]
    public int Piid { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("updateTime")]
    public long? UpdateTime { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Code == 0;
}

/// <summary>
/// 请求设置设备属性的目标项
/// </summary>
public sealed class MiotPropSetTarget
{
    [JsonPropertyName("did")]
    public required string Did { get; set; }

    [JsonPropertyName("siid")]
    public required int Siid { get; set; }

    [JsonPropertyName("piid")]
    public required int Piid { get; set; }

    [JsonPropertyName("value")]
    public required object Value { get; set; }
}

/// <summary>
/// 设置设备属性的结果
/// </summary>
public sealed class MiotPropSetResult
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    [JsonPropertyName("siid")]
    public int Siid { get; set; }

    [JsonPropertyName("piid")]
    public int Piid { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// 设置属性是否成功：在 MIoT 规范中，0 为 OK，1 为成功/正在执行
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 0 || Code == 1;
}

/// <summary>
/// 执行设备 Action 动作请求
/// </summary>
public sealed class MiotActionTarget
{
    [JsonPropertyName("did")]
    public required string Did { get; set; }

    [JsonPropertyName("siid")]
    public required int Siid { get; set; }

    [JsonPropertyName("aiid")]
    public required int Aiid { get; set; }

    [JsonPropertyName("in")]
    public List<object> In { get; set; } = [];
}

/// <summary>
/// 执行设备 Action 动作返回结果
/// </summary>
public sealed class MiotActionResult
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    [JsonPropertyName("siid")]
    public int Siid { get; set; }

    [JsonPropertyName("aiid")]
    public int Aiid { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("out")]
    public List<object>? Out { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Code == 0;
}

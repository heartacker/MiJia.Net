using System.Text.Json.Serialization;

namespace MiJia.Net.Models.Devices;

/// <summary>
/// 家庭信息
/// </summary>
public sealed class HomeInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("uid")]
    public long Uid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("bssid")]
    public string? Bssid { get; set; }

    [JsonPropertyName("dids")]
    public List<string> Dids { get; set; } = [];

    [JsonPropertyName("roomlist")]
    public List<RoomInfo> Rooms { get; set; } = [];
}

/// <summary>
/// 房间信息
/// </summary>
public sealed class RoomInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dids")]
    public List<string> Dids { get; set; } = [];

    [JsonPropertyName("bssid")]
    public string? Bssid { get; set; }
}

/// <summary>
/// 米家设备基础信息
/// </summary>
public sealed class DeviceInfo
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("mac")]
    public string? Mac { get; set; }

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("spec_type")]
    public string? SpecType { get; set; }

    [JsonPropertyName("show_mode")]
    public int ShowMode { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    [JsonPropertyName("ssid")]
    public string? Ssid { get; set; }

    [JsonPropertyName("bssid")]
    public string? Bssid { get; set; }

    [JsonPropertyName("rssi")]
    public int? Rssi { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("pid")]
    public string? Pid { get; set; }

    /// <summary>
    /// 所属家庭 ID
    /// </summary>
    public string? HomeId { get; set; }

    /// <summary>
    /// 所属房间 ID
    /// </summary>
    public string? RoomId { get; set; }

    /// <summary>
    /// 所属房间名称
    /// </summary>
    public string? RoomName { get; set; }
}

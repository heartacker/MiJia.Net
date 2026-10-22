using System.Text.Json.Serialization;

namespace MiJia.Net.Models.Scenes;

/// <summary>
/// 米家手动场景定义信息
/// </summary>
public sealed class SceneInfo
{
    [JsonPropertyName("scene_id")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("home_id")]
    public string HomeId { get; set; } = string.Empty;

    [JsonPropertyName("home_name")]
    public string HomeName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int? Type { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}

/// <summary>
/// 米家场景列表云端网关返回结构
/// </summary>
public sealed class SimpleSceneListResponse
{
    [JsonPropertyName("manual_scene_info_list")]
    public List<SceneInfo>? ManualSceneInfoList { get; set; }
}

/// <summary>
/// 执行场景的请求载荷
/// </summary>
public sealed class RunSceneRequest
{
    [JsonPropertyName("scene_id")]
    public string SceneId { get; set; } = string.Empty;

    [JsonPropertyName("scene_type")]
    public int SceneType { get; set; } = 2;

    [JsonPropertyName("phone_id")]
    public string PhoneId { get; set; } = "null";

    [JsonPropertyName("home_id")]
    public string HomeId { get; set; } = string.Empty;

    [JsonPropertyName("owner_uid")]
    public long OwnerUid { get; set; }
}

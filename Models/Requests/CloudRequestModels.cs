using System.Text.Json.Serialization;
using MiJia.Net.Models.Devices;

namespace MiJia.Net.Models.Requests;

public sealed class GetHomesRequest
{
    [JsonPropertyName("fg")]
    public bool Fg { get; set; } = false;

    [JsonPropertyName("fetch_share")]
    public bool FetchShare { get; set; } = true;
}

public sealed class GetDevicesRequest
{
    [JsonPropertyName("getVirtualModel")]
    public bool GetVirtualModel { get; set; } = true;

    [JsonPropertyName("getHuamiDevices")]
    public int GetHuamiDevices { get; set; } = 1;

    [JsonPropertyName("get_split_device")]
    public bool GetSplitDevice { get; set; } = true;

    [JsonPropertyName("support_all_spec")]
    public bool SupportAllSpec { get; set; } = true;
}

public sealed class PropBatchGetRequest
{
    [JsonPropertyName("params")]
    public List<MiotPropGetTarget> Params { get; set; } = [];
}

public sealed class PropBatchSetRequest
{
    [JsonPropertyName("params")]
    public List<MiotPropSetTarget> Params { get; set; } = [];
}

public sealed class ActionRequest
{
    [JsonPropertyName("params")]
    public MiotActionTarget Params { get; set; } = null!;
}

using System.Text.Json.Serialization;
using MiJia.Net.Models.Auth;
using MiJia.Net.Models.Devices;
using MiJia.Net.Models.Requests;
using MiJia.Net.Models.Spec;

namespace MiJia.Net.Serialization;

/// <summary>
/// 编译期静态 AOT 零反射 JSON 序列化上下文
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MiAuthTokens))]
[JsonSerializable(typeof(List<MiAuthTokens>))]
[JsonSerializable(typeof(DeviceInfo))]
[JsonSerializable(typeof(List<DeviceInfo>))]
[JsonSerializable(typeof(HomeInfo))]
[JsonSerializable(typeof(List<HomeInfo>))]
[JsonSerializable(typeof(RoomInfo))]
[JsonSerializable(typeof(MiotSpecInstance))]
[JsonSerializable(typeof(MiotPropGetTarget))]
[JsonSerializable(typeof(List<MiotPropGetTarget>))]
[JsonSerializable(typeof(MiotPropGetResult))]
[JsonSerializable(typeof(List<MiotPropGetResult>))]
[JsonSerializable(typeof(MiotPropSetTarget))]
[JsonSerializable(typeof(List<MiotPropSetTarget>))]
[JsonSerializable(typeof(MiotPropSetResult))]
[JsonSerializable(typeof(List<MiotPropSetResult>))]
[JsonSerializable(typeof(MiotActionTarget))]
[JsonSerializable(typeof(MiotActionResult))]
[JsonSerializable(typeof(GetHomesRequest))]
[JsonSerializable(typeof(GetDevicesRequest))]
[JsonSerializable(typeof(PropBatchGetRequest))]
[JsonSerializable(typeof(PropBatchSetRequest))]
[JsonSerializable(typeof(ActionRequest))]
public partial class MiJiaJsonContext : JsonSerializerContext
{
}

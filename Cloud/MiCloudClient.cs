using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiJia.Net.Models.Auth;
using MiJia.Net.Models.Devices;
using MiJia.Net.Models.Requests;
using MiJia.Net.Security;
using MiJia.Net.Serialization;

namespace MiJia.Net.Cloud;

public interface IMiCloudClient
{
    /// <summary>
    /// 当前认证凭据
    /// </summary>
    MiAuthTokens Tokens { get; }

    /// <summary>
    /// 获取家庭和房间列表
    /// </summary>
    Task<List<HomeInfo>> GetHomesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取名下所有设备列表（可包含共享设备）
    /// </summary>
    Task<List<DeviceInfo>> GetDevicesAsync(bool getVirtualModel = true, bool getHuamiDevices = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量读取设备属性 (MIoT Spec)
    /// </summary>
    Task<List<MiotPropGetResult>> GetPropertiesAsync(IEnumerable<MiotPropGetTarget> targets, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量设置设备属性 (MIoT Spec)
    /// </summary>
    Task<List<MiotPropSetResult>> SetPropertiesAsync(IEnumerable<MiotPropSetTarget> targets, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行设备 Action 动作 (MIoT Spec)
    /// </summary>
    Task<MiotActionResult> RunActionAsync(MiotActionTarget target, CancellationToken cancellationToken = default);
}

public sealed class MiCloudClient : IMiCloudClient
{
    private readonly HttpClient _http;
    private readonly MiAuthTokens _tokens;
    private readonly string _serverBaseUrl;

    public MiAuthTokens Tokens => _tokens;

    public MiCloudClient(MiAuthTokens tokens, HttpClient? httpClient = null, string serverRegion = "cn")
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

        _serverBaseUrl = serverRegion.ToLowerInvariant() switch
        {
            "cn" => "https://api.io.mi.com/app",
            "us" => "https://us.api.io.mi.com/app",
            "de" => "https://de.api.io.mi.com/app",
            "sg" => "https://sg.api.io.mi.com/app",
            "ru" => "https://ru.api.io.mi.com/app",
            "in" => "https://i2.api.io.mi.com/app",
            _ => "https://api.io.mi.com/app"
        };

        if (httpClient != null)
        {
            _http = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = false, // 手工严格在 Header 中控制 Cookie
                AutomaticDecompression = DecompressionMethods.All
            };
            _http = new HttpClient(handler);
        }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Android-7.1.1-1.0.0-ONEPLUS A3010-136-ABCDEFGHIJKLM APP/xiaomi.smarthome APPV/62830");
    }

    /// <inheritdoc />
    public async Task<List<HomeInfo>> GetHomesAsync(CancellationToken cancellationToken = default)
    {
        var reqObj = new GetHomesRequest { Fg = false, FetchShare = true };
        string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.GetHomesRequest);

        var responseNode = await PostAsync("/homeroom/gethome", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode == null) return [];

        var homelist = resultNode["homelist"]?.AsArray();
        if (homelist == null) return [];

        var list = new List<HomeInfo>();
        foreach (var item in homelist)
        {
            if (item == null) continue;
            var home = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.HomeInfo);
            if (home != null)
            {
                list.Add(home);
            }
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<List<DeviceInfo>> GetDevicesAsync(
        bool getVirtualModel = true,
        bool getHuamiDevices = true,
        CancellationToken cancellationToken = default)
    {
        var reqObj = new GetDevicesRequest
        {
            GetVirtualModel = getVirtualModel,
            GetHuamiDevices = getHuamiDevices ? 1 : 0,
            GetSplitDevice = true,
            SupportAllSpec = true
        };
        string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.GetDevicesRequest);

        var responseNode = await PostAsync("/home/device_list", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode == null) return [];

        var deviceListArray = resultNode["list"]?.AsArray();
        if (deviceListArray == null) return [];

        var list = new List<DeviceInfo>();
        foreach (var item in deviceListArray)
        {
            if (item == null) continue;
            var dev = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.DeviceInfo);
            if (dev != null)
            {
                list.Add(dev);
            }
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<List<MiotPropGetResult>> GetPropertiesAsync(
        IEnumerable<MiotPropGetTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var targetList = targets.ToList();
        if (targetList.Count == 0) return [];

        var reqObj = new PropBatchGetRequest { Params = targetList };
        string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.PropBatchGetRequest);

        var responseNode = await PostAsync("/miotspec/prop/get", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"]?.AsArray();
        if (resultNode == null) return [];

        var list = new List<MiotPropGetResult>();
        foreach (var item in resultNode)
        {
            if (item == null) continue;
            var propRes = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.MiotPropGetResult);
            if (propRes != null)
            {
                list.Add(propRes);
            }
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<List<MiotPropSetResult>> SetPropertiesAsync(
        IEnumerable<MiotPropSetTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var targetList = targets.ToList();
        if (targetList.Count == 0) return [];

        var reqObj = new PropBatchSetRequest { Params = targetList };
        string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.PropBatchSetRequest);

        var responseNode = await PostAsync("/miotspec/prop/set", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"]?.AsArray();
        if (resultNode == null) return [];

        var list = new List<MiotPropSetResult>();
        foreach (var item in resultNode)
        {
            if (item == null) continue;
            var propRes = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.MiotPropSetResult);
            if (propRes != null)
            {
                list.Add(propRes);
            }
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<MiotActionResult> RunActionAsync(
        MiotActionTarget target,
        CancellationToken cancellationToken = default)
    {
        var reqObj = new ActionRequest { Params = target };
        string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.ActionRequest);

        var responseNode = await PostAsync("/miotspec/action", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode != null)
        {
            var res = JsonSerializer.Deserialize(resultNode.ToJsonString(), MiJiaJsonContext.Default.MiotActionResult);
            if (res != null) return res;
        }

        return new MiotActionResult
        {
            Did = target.Did,
            Siid = target.Siid,
            Aiid = target.Aiid,
            Code = responseNode?["code"]?.GetValue<int>() ?? -1
        };
    }

    /// <summary>
    /// 底层签名与 HTTP POST 执行（通过 MIOT-ENCRYPT-ALGORITHM: ENCRYPT-RC4 双向加解密通信）
    /// </summary>
    private async Task<JsonNode?> PostAsync(string relativePath, string dataJson, CancellationToken cancellationToken)
    {
        string fullUri = _serverBaseUrl + relativePath;
        string nonce = MiSigner.GenerateNonce();
        string signedNonce = MiSigner.GenerateSignedNonce(_tokens.Ssecurity, nonce);

        // 构造要加密与签名的表单键值对
        var rawParams = new Dictionary<string, string>
        {
            { "data", dataJson }
        };

        // 1. 生成 rc4_hash__
        string rc4Hash = MiSigner.GenerateEncSignature(relativePath, "POST", signedNonce, rawParams);
        rawParams["rc4_hash__"] = rc4Hash;

        // 2. 将 parameters 的每个值用 RC4-drop1024 进行加密并转 Base64
        var encryptedParams = new Dictionary<string, string>();
        foreach (var kvp in rawParams)
        {
            encryptedParams[kvp.Key] = MiRc4.Encrypt(signedNonce, kvp.Value);
        }

        // 3. 计算外层 signature
        string signature = MiSigner.GenerateEncSignature(relativePath, "POST", signedNonce, encryptedParams);

        // 4. 补充协议外层参数
        encryptedParams["signature"] = signature;
        encryptedParams["_nonce"] = nonce;
        encryptedParams["ssecurity"] = _tokens.Ssecurity;

        using var request = new HttpRequestMessage(HttpMethod.Post, fullUri);

        // 设置协议专用 Headers
        request.Headers.Add("Accept-Encoding", "identity");
        request.Headers.Add("x-xiaomi-protocal-flag-cli", "PROTOCAL-HTTP2");
        request.Headers.Add("MIOT-ENCRYPT-ALGORITHM", "ENCRYPT-RC4");
        request.Headers.Add("Cookie", $"userId={_tokens.UserId}; serviceToken={_tokens.ServiceToken}; yetAnotherServiceToken={_tokens.ServiceToken}; locale=zh_CN; timezone=GMT+08:00; is_daylight=0; dst_offset=0; channel=MI_APP_STORE;");

        request.Content = new FormUrlEncodedContent(encryptedParams);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string respEncrypted = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // 使用 signedNonce 解密返回的 RC4 密文
        string respDecrypted = MiRc4.Decrypt(signedNonce, respEncrypted.Trim());

        var root = JsonNode.Parse(respDecrypted);
        if (root == null)
        {
            throw new InvalidOperationException($"Invalid JSON response from {relativePath}");
        }

        int code = root["code"]?.GetValue<int>() ?? -1;
        if (code != 0)
        {
            string message = root["message"]?.GetValue<string>() ?? "Unknown error";
            throw new InvalidOperationException($"MiCloud API error [{code}]: {message} at {relativePath}");
        }

        return root;
    }
}

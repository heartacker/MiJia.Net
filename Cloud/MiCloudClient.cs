using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiJia.Net.Models.Auth;
using MiJia.Net.Models.Devices;
using MiJia.Net.Security;

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
                UseCookies = false, // 我们手工在 Header 中严格控制 Cookie
                AutomaticDecompression = DecompressionMethods.All
            };
            _http = new HttpClient(handler);
        }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Android-7.1.1-1.0.0-ONEPLUS A3010-136-1.0.0 APP/xiaomi.smarthome APPV/62830");
    }

    /// <inheritdoc />
    public async Task<List<HomeInfo>> GetHomesAsync(CancellationToken cancellationToken = default)
    {
        var dataObj = new { fg = false, fetch_share = true };
        string dataJson = JsonSerializer.Serialize(dataObj);

        var responseNode = await PostAsync("/homeroom/gethome", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode == null) return [];

        var homelist = resultNode["homelist"]?.AsArray();
        if (homelist == null) return [];

        var list = new List<HomeInfo>();
        foreach (var item in homelist)
        {
            if (item == null) continue;
            var home = JsonSerializer.Deserialize<HomeInfo>(item.ToJsonString());
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
        var dataObj = new
        {
            getVirtualModel,
            getHuamiDevices = getHuamiDevices ? 1 : 0,
            get_split_device = true,
            support_all_spec = true
        };
        string dataJson = JsonSerializer.Serialize(dataObj);

        var responseNode = await PostAsync("/home/device_list", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode == null) return [];

        var deviceListArray = resultNode["list"]?.AsArray();
        if (deviceListArray == null) return [];

        var list = new List<DeviceInfo>();
        foreach (var item in deviceListArray)
        {
            if (item == null) continue;
            var dev = JsonSerializer.Deserialize<DeviceInfo>(item.ToJsonString());
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

        var dataObj = new { @params = targetList };
        string dataJson = JsonSerializer.Serialize(dataObj);

        var responseNode = await PostAsync("/miotspec/prop/get", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"]?.AsArray();
        if (resultNode == null) return [];

        var list = new List<MiotPropGetResult>();
        foreach (var item in resultNode)
        {
            if (item == null) continue;
            var propRes = JsonSerializer.Deserialize<MiotPropGetResult>(item.ToJsonString());
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

        var dataObj = new { @params = targetList };
        string dataJson = JsonSerializer.Serialize(dataObj);

        var responseNode = await PostAsync("/miotspec/prop/set", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"]?.AsArray();
        if (resultNode == null) return [];

        var list = new List<MiotPropSetResult>();
        foreach (var item in resultNode)
        {
            if (item == null) continue;
            var propRes = JsonSerializer.Deserialize<MiotPropSetResult>(item.ToJsonString());
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
        var dataObj = new { @params = target };
        string dataJson = JsonSerializer.Serialize(dataObj);

        var responseNode = await PostAsync("/miotspec/action", dataJson, cancellationToken).ConfigureAwait(false);
        var resultNode = responseNode?["result"];
        if (resultNode != null)
        {
            var res = JsonSerializer.Deserialize<MiotActionResult>(resultNode.ToJsonString());
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
    /// 底层签名与 HTTP POST 执行
    /// </summary>
    private async Task<JsonNode?> PostAsync(string relativePath, string dataJson, CancellationToken cancellationToken)
    {
        string fullUri = _serverBaseUrl + relativePath;
        string nonce = MiSigner.GenerateNonce();
        string signedNonce = MiSigner.GenerateSignedNonce(_tokens.Ssecurity, nonce);
        string signature = MiSigner.GenerateSignature(relativePath, signedNonce, nonce, dataJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, fullUri);

        // 设置协议专用 Headers
        request.Headers.Add("x-xiaomi-protocal-flag-cli", "PROTOCAL-HTTP2");
        request.Headers.Add("Cookie", $"userId={_tokens.UserId}; serviceToken={_tokens.ServiceToken};");

        // 表单内容包含 data 与签名参数
        var formData = new Dictionary<string, string>
        {
            { "data", dataJson },
            { "_nonce", nonce },
            { "signature", signature }
        };

        request.Content = new FormUrlEncodedContent(formData);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(json);
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

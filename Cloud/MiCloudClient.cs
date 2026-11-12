using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiJia.Net.Models.Auth;
using MiJia.Net.Models.Devices;
using MiJia.Net.Models.Requests;
using MiJia.Net.Models.Scenes;
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

    /// <summary>
    /// 获取用户的手动场景列表；homeId 为空时返回全部家庭的场景
    /// </summary>
    Task<List<SceneInfo>> GetScenesAsync(string? homeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行米家手动场景
    /// </summary>
    Task<bool> RunSceneAsync(string sceneId, string? homeId = null, CancellationToken cancellationToken = default);
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
        var list = new List<HomeInfo>();
        var seenHomeIds = new HashSet<string>();

        // 1. 优先调用现代聚合接口 /v2/homeroom/gethome_merged，米家云端会直接聚合“自有家庭”与“被分享的家庭”
        try
        {
            const string mergedReqJson = "{\"fg\":true,\"fetch_share\":true,\"fetch_share_dev\":true,\"fetch_cariot\":true,\"limit\":300,\"app_ver\":7,\"plat_form\":0}";
            var responseNode = await PostAsync("/v2/homeroom/gethome_merged", mergedReqJson, cancellationToken).ConfigureAwait(false);
            var resultNode = responseNode?["result"];
            var homelist = resultNode?["homelist"]?.AsArray();
            if (homelist != null && homelist.Count > 0)
            {
                foreach (var item in homelist)
                {
                    if (item == null) continue;
                    var home = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.HomeInfo);
                    if (home != null && !string.IsNullOrEmpty(home.Id) && seenHomeIds.Add(home.Id))
                    {
                        list.Add(home);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MiCloudClient] gethome_merged fallback: {ex.Message}");
        }

        // 2. 若 gethome_merged 结果为空，降级调用经典接口 /homeroom/gethome，同时合并 homelist 与 share_home_list
        if (list.Count == 0)
        {
            var reqObj = new GetHomesRequest { Fg = false, FetchShare = true };
            string dataJson = JsonSerializer.Serialize(reqObj, MiJiaJsonContext.Default.GetHomesRequest);
            var responseNode = await PostAsync("/homeroom/gethome", dataJson, cancellationToken).ConfigureAwait(false);
            var resultNode = responseNode?["result"];
            if (resultNode != null)
            {
                var homelist = resultNode["homelist"]?.AsArray();
                if (homelist != null)
                {
                    foreach (var item in homelist)
                    {
                        if (item == null) continue;
                        var home = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.HomeInfo);
                        if (home != null && !string.IsNullOrEmpty(home.Id) && seenHomeIds.Add(home.Id))
                        {
                            list.Add(home);
                        }
                    }
                }

                var shareHomeList = resultNode["share_home_list"]?.AsArray();
                if (shareHomeList != null)
                {
                    foreach (var item in shareHomeList)
                    {
                        if (item == null) continue;
                        var home = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.HomeInfo);
                        if (home != null && !string.IsNullOrEmpty(home.Id) && seenHomeIds.Add(home.Id))
                        {
                            if (home.ShareFlag == 0) home.ShareFlag = 1;
                            list.Add(home);
                        }
                    }
                }
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
        var list = new List<DeviceInfo>();
        var seenDids = new HashSet<string>();

        // 1. 优先调用全域设备聚合接口 /v2/home/device_list_page，包含当前账号的全部自有设备与跨家庭共享设备
        try
        {
            const string pageReqJson = "{\"ssid\":\"<unknown ssid>\",\"bssid\":\"02:00:00:00:00:00\",\"getVirtualModel\":true,\"getHuamiDevices\":1,\"get_split_device\":true,\"support_smart_home\":true,\"get_cariot_device\":true,\"get_third_device\":true,\"get_phone_device\":true,\"get_miwear_device\":true}";
            var responseNode = await PostAsync("/v2/home/device_list_page", pageReqJson, cancellationToken).ConfigureAwait(false);
            var resultNode = responseNode?["result"];
            var deviceListArray = resultNode?["list"]?.AsArray();
            if (deviceListArray != null && deviceListArray.Count > 0)
            {
                foreach (var item in deviceListArray)
                {
                    if (item == null) continue;
                    var dev = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.DeviceInfo);
                    if (dev != null && !string.IsNullOrEmpty(dev.Did) && seenDids.Add(dev.Did))
                    {
                        list.Add(dev);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MiCloudClient] device_list_page fallback: {ex.Message}");
        }

        // 2. 若 device_list_page 结果为空，降级调用经典 /home/device_list
        if (list.Count == 0)
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
            var deviceListArray = resultNode?["list"]?.AsArray();
            if (deviceListArray != null)
            {
                foreach (var item in deviceListArray)
                {
                    if (item == null) continue;
                    var dev = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.DeviceInfo);
                    if (dev != null && !string.IsNullOrEmpty(dev.Did) && seenDids.Add(dev.Did))
                    {
                        list.Add(dev);
                    }
                }
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

    /// <inheritdoc />
    public async Task<List<SceneInfo>> GetScenesAsync(string? homeId = null, CancellationToken cancellationToken = default)
    {
        var homes = await GetHomesAsync(cancellationToken).ConfigureAwait(false);
        var targetHomes = string.IsNullOrEmpty(homeId) 
            ? homes 
            : homes.Where(h => h.Id == homeId).ToList();

        if (targetHomes.Count == 0 && !string.IsNullOrEmpty(homeId))
        {
            // 若用户指定了 homeId 但本地未查到，构造一个临时 entry 尝试直接请求
            long defaultUid = long.TryParse(_tokens.UserId, out var uid) ? uid : 0;
            targetHomes = [new HomeInfo { Id = homeId, Name = "Home", Uid = defaultUid }];
        }

        var allScenes = new List<SceneInfo>();
        foreach (var home in targetHomes)
        {
            long ownerUid = home.Uid != 0 ? home.Uid : (long.TryParse(_tokens.UserId, out var uid) ? uid : 0);
            var reqData = new JsonObject
            {
                ["app_version"] = 12,
                ["get_type"] = 2,
                ["home_id"] = home.Id,
                ["owner_uid"] = ownerUid
            };

            try
            {
                var respNode = await PostAsync(
                    "/appgateway/miot/appsceneservice/AppSceneService/GetSimpleSceneList",
                    reqData.ToJsonString(),
                    cancellationToken).ConfigureAwait(false);

                var resultNode = respNode?["result"] ?? respNode;
                var manualList = resultNode?["manual_scene_info_list"]?.AsArray();
                if (manualList != null)
                {
                    foreach (var item in manualList)
                    {
                        if (item == null) continue;
                        var scene = JsonSerializer.Deserialize(item.ToJsonString(), MiJiaJsonContext.Default.SceneInfo);
                        if (scene != null)
                        {
                            if (string.IsNullOrEmpty(scene.HomeId)) scene.HomeId = home.Id;
                            if (string.IsNullOrEmpty(scene.HomeName)) scene.HomeName = home.Name;
                            allScenes.Add(scene);
                        }
                    }
                }
            }
            catch
            {
                // 忽略单个家庭获取失败，继续收集其他家庭场景
            }
        }

        return allScenes;
    }

    /// <inheritdoc />
    public async Task<bool> RunSceneAsync(string sceneId, string? homeId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) throw new ArgumentException("Scene ID cannot be empty", nameof(sceneId));

        var homes = await GetHomesAsync(cancellationToken).ConfigureAwait(false);
        var targetHome = !string.IsNullOrEmpty(homeId) 
            ? homes.FirstOrDefault(h => h.Id == homeId) 
            : homes.FirstOrDefault();

        string targetHomeId = targetHome?.Id ?? (homeId ?? string.Empty);
        long ownerUid = (targetHome?.Uid ?? 0) != 0 
            ? targetHome!.Uid 
            : (long.TryParse(_tokens.UserId, out var uid) ? uid : 0);

        var runReq = new RunSceneRequest
        {
            SceneId = sceneId,
            SceneType = 2,
            PhoneId = "null",
            HomeId = targetHomeId,
            OwnerUid = ownerUid
        };

        string reqJson = JsonSerializer.Serialize(runReq, MiJiaJsonContext.Default.RunSceneRequest);
        var respNode = await PostAsync(
            "/appgateway/miot/appsceneservice/AppSceneService/NewRunScene",
            reqJson,
            cancellationToken).ConfigureAwait(false);

        int code = respNode?["code"]?.GetValue<int>() ?? -1;
        return code == 0;
    }

    /// <summary>
    /// 底层签名与 HTTP POST 执行（通过 MIOT-ENCRYPT-ALGORITHM: ENCRYPT-RC4 双向加解密通信）
    /// </summary>
    public async Task<JsonNode?> PostAsync(string relativePath, string dataJson, CancellationToken cancellationToken = default)
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

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MiJia.Net.Models.Auth;
using MiJia.Net.Serialization;

namespace MiJia.Net.Auth;

public interface IMiAuthService
{
    /// <summary>
    /// 请求生成米家扫码登录二维码
    /// </summary>
    Task<LoginQrCodeResult> RequestLoginQrCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 轮询检查扫码登录进度
    /// </summary>
    /// <param name="qrResult">之前由 RequestLoginQrCodeAsync 获取的对象</param>
    Task<QrPollResult> PollQrLoginStatusAsync(LoginQrCodeResult qrResult, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载本地存储的认证凭据
    /// </summary>
    Task<MiAuthTokens?> LoadSavedTokensAsync(string? customPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存认证凭据到本地
    /// </summary>
    Task SaveTokensAsync(MiAuthTokens tokens, string? customPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除本地存储的认证凭据（退出登录）
    /// </summary>
    void ClearSavedTokens(string? customPath = null);

    /// <summary>
    /// 获取本地已保存的所有历史登录账号
    /// </summary>
    Task<List<MiAuthTokens>> GetSavedAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除指定用户ID的账号记录
    /// </summary>
    Task RemoveAccountAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class MiAuthService : IMiAuthService
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookieContainer;
    private const string DefaultSid = "xiaomiio";

    public MiAuthService(HttpClient? httpClient = null, CookieContainer? cookieContainer = null)
    {
        _cookieContainer = cookieContainer ?? new CookieContainer();

        if (httpClient != null)
        {
            _http = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = _cookieContainer,
                AllowAutoRedirect = false
            };
            _http = new HttpClient(handler);
        }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <inheritdoc />
    public async Task<LoginQrCodeResult> RequestLoginQrCodeAsync(CancellationToken cancellationToken = default)
    {
        string url = $"https://account.xiaomi.com/longPolling/loginUrl?_qrsize=240&qs=%3Fsid%3D{DefaultSid}%26_locale%3Dzh_CN&bizDeviceType=&callback=https%3A%2F%2Fsts.api.io.mi.com%2Fsts&sid={DefaultSid}&_locale=zh_CN";

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        json = CleanJsonPrefix(json);

        var node = JsonNode.Parse(json);
        if (node == null)
        {
            throw new InvalidOperationException("Failed to parse QR login response: " + json);
        }

        string qrUrl = GetStringSafe(node, "qr");
        string lp = GetStringSafe(node, "lp");
        string? sign = GetOptionalString(node, "_sign");

        if (string.IsNullOrEmpty(lp))
        {
            throw new InvalidOperationException("Missing polling URL in login response: " + json);
        }

        // 尝试直接下载小米官方生成的标准二维码图片字节流，避免二次转码失效
        byte[]? qrBytes = null;
        if (!string.IsNullOrEmpty(qrUrl))
        {
            try
            {
                using var imgResp = await _http.GetAsync(qrUrl, cancellationToken).ConfigureAwait(false);
                if (imgResp.IsSuccessStatusCode)
                {
                    qrBytes = await imgResp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // 下载图片失败时降级回客户端本地生成
            }
        }

        return new LoginQrCodeResult
        {
            QrUrl = qrUrl,
            QrContent = qrUrl,
            QrBytes = qrBytes,
            CheckUrl = lp,
            Sign = sign
        };
    }

    /// <inheritdoc />
    public async Task<QrPollResult> PollQrLoginStatusAsync(LoginQrCodeResult qrResult, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(qrResult.CheckUrl, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            json = CleanJsonPrefix(json);

            var node = JsonNode.Parse(json);
            if (node == null)
            {
                return new QrPollResult { State = QrLoginState.Failed, Message = "Invalid response format." };
            }

            int code = node["code"]?.GetValue<int>() ?? -1;

            switch (code)
            {
                case 0: // 扫码且确认登录成功，处理回调
                    string? location = GetOptionalString(node, "location");
                    if (string.IsNullOrEmpty(location))
                    {
                        return new QrPollResult { State = QrLoginState.Failed, Message = "Missing redirect location." };
                    }

                    // 传递长轮询返回的 node 节点信息（部分情况下 nonce/ssecurity 直接在长轮询返回中）
                    var tokens = await ExchangeTokensAsync(location, cancellationToken, node).ConfigureAwait(false);
                    return new QrPollResult
                    {
                        State = QrLoginState.Confirmed,
                        Tokens = tokens,
                        Message = "Login success."
                    };

                case 70014: // 二维码真正失效或被取消
                case 87002:
                    return new QrPollResult { State = QrLoginState.Expired, Message = "QR code expired." };

                case 87001: // 已在手机端扫码，等待用户点击确认
                    return new QrPollResult { State = QrLoginState.ScannedWaitingConfirm, Message = "Scanned, waiting confirmation." };

                case 70016: // Comet 单次 30s 挂起超时，仍处于等待扫码状态
                default:
                    return new QrPollResult { State = QrLoginState.WaitingScan, Message = "Waiting for scan." };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new QrPollResult { State = QrLoginState.Failed, Message = ex.Message };
        }
    }

    /// <summary>
    /// 请求 location 跳转地址换取 serviceToken 和 ssecurity
    /// </summary>
    private async Task<MiAuthTokens> ExchangeTokensAsync(string location, CancellationToken cancellationToken, JsonNode? pollNode = null)
    {
        // 确保 URI 协议完整
        if (!location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            location = "https:" + (location.StartsWith("//") ? string.Empty : "//") + location;
        }

        string ssecurity = string.Empty;
        string userId = string.Empty;
        string serviceToken = string.Empty;
        string? cUserId = null;
        string? passToken = null;

        // 0. 优先从小轮询返回节点中尝试提取
        if (pollNode != null)
        {
            ssecurity = GetOptionalString(pollNode, "ssecurity") ?? string.Empty;
            userId = GetOptionalString(pollNode, "userId") ?? string.Empty;
            serviceToken = GetOptionalString(pollNode, "serviceToken") ?? string.Empty;
            passToken = GetOptionalString(pollNode, "passToken");
            cUserId = GetOptionalString(pollNode, "cUserId");
        }

        // 使用支持自动重定向的子处理流程获取 STS 凭证，同时保持 CookieContainer 追踪
        using var stsHandler = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        using var stsHttp = new HttpClient(stsHandler);
        stsHttp.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        using var res = await stsHttp.GetAsync(location, cancellationToken).ConfigureAwait(false);
        string body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // 1. 从当前 CookieContainer 提取关联 Cookie
        var uri = new Uri(location);
        var cookies = _cookieContainer.GetCookies(uri);
        foreach (Cookie cookie in cookies)
        {
            if (string.IsNullOrEmpty(serviceToken) && cookie.Name.Equals("serviceToken", StringComparison.OrdinalIgnoreCase)) serviceToken = cookie.Value;
            else if (string.IsNullOrEmpty(userId) && cookie.Name.Equals("userId", StringComparison.OrdinalIgnoreCase)) userId = cookie.Value;
            else if (cUserId == null && cookie.Name.Equals("cUserId", StringComparison.OrdinalIgnoreCase)) cUserId = cookie.Value;
            else if (passToken == null && cookie.Name.Equals("passToken", StringComparison.OrdinalIgnoreCase)) passToken = cookie.Value;
        }

        // 也检查 xiaomi.com 域下的 Cookie
        try
        {
            var miCookies = _cookieContainer.GetCookies(new Uri("https://account.xiaomi.com"));
            foreach (Cookie c in miCookies)
            {
                if (string.IsNullOrEmpty(userId) && c.Name.Equals("userId", StringComparison.OrdinalIgnoreCase)) userId = c.Value;
                if (string.IsNullOrEmpty(serviceToken) && c.Name.Equals("serviceToken", StringComparison.OrdinalIgnoreCase)) serviceToken = c.Value;
                if (cUserId == null && c.Name.Equals("cUserId", StringComparison.OrdinalIgnoreCase)) cUserId = c.Value;
                if (passToken == null && c.Name.Equals("passToken", StringComparison.OrdinalIgnoreCase)) passToken = c.Value;
            }
        }
        catch
        {
            // 忽略非致命异常
        }

        // 2. 从 Set-Cookie 头提取
        if (res.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var cookie in cookieHeaders)
            {
                var match = Regex.Match(cookie, @"([^=;\s]+)=([^;]+)");
                if (match.Success)
                {
                    string name = match.Groups[1].Value.Trim();
                    string val = match.Groups[2].Value.Trim();

                    if (string.IsNullOrEmpty(serviceToken) && name.Equals("serviceToken", StringComparison.OrdinalIgnoreCase)) serviceToken = val;
                    else if (string.IsNullOrEmpty(userId) && name.Equals("userId", StringComparison.OrdinalIgnoreCase)) userId = val;
                    else if (cUserId == null && name.Equals("cUserId", StringComparison.OrdinalIgnoreCase)) cUserId = val;
                    else if (passToken == null && name.Equals("passToken", StringComparison.OrdinalIgnoreCase)) passToken = val;
                }
            }
        }

        // 3. 从响应正文 JSON 提取
        try
        {
            var node = JsonNode.Parse(CleanJsonPrefix(body));
            if (node != null)
            {
                if (string.IsNullOrEmpty(ssecurity)) ssecurity = GetOptionalString(node, "ssecurity") ?? string.Empty;
                if (string.IsNullOrEmpty(userId)) userId = GetOptionalString(node, "userId") ?? string.Empty;
                if (string.IsNullOrEmpty(serviceToken)) serviceToken = GetOptionalString(node, "serviceToken") ?? string.Empty;
                if (passToken == null) passToken = GetOptionalString(node, "passToken");
                if (cUserId == null) cUserId = GetOptionalString(node, "cUserId");
            }
        }
        catch
        {
            // 某些 STS 响应直接为重定向或纯文本
        }

        // 4. 从 location URL 查询参数中提取 ssecurity 与 userId
        if (string.IsNullOrEmpty(ssecurity) && location.Contains("ssecurity="))
        {
            var match = Regex.Match(location, @"ssecurity=([^&]+)");
            if (match.Success)
            {
                ssecurity = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(userId) && location.Contains("userId="))
        {
            var match = Regex.Match(location, @"userId=([^&]+)");
            if (match.Success)
            {
                userId = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(serviceToken) && location.Contains("serviceToken="))
        {
            var match = Regex.Match(location, @"serviceToken=([^&]+)");
            if (match.Success)
            {
                serviceToken = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(serviceToken) || string.IsNullOrEmpty(ssecurity))
        {
            throw new InvalidOperationException($"换取米家凭证失败：缺少关键参数(serviceToken={!string.IsNullOrEmpty(serviceToken)}, ssecurity={!string.IsNullOrEmpty(ssecurity)}, userId={userId})");
        }

        return new MiAuthTokens
        {
            UserId = userId,
            ServiceToken = serviceToken,
            Ssecurity = ssecurity,
            CUserId = cUserId,
            PassToken = passToken,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 获取凭证默认存储路径（优先 ~/.config/mijia-api/auth.json，兼容原有 Python 工具）
    /// </summary>
    public static string GetDefaultAuthPath()
    {
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDir = Path.Combine(userHome, ".config", "mijia-api");
        return Path.Combine(configDir, "auth.json");
    }

    /// <inheritdoc />
    public async Task<MiAuthTokens?> LoadSavedTokensAsync(string? customPath = null, CancellationToken cancellationToken = default)
    {
        string path = customPath ?? GetDefaultAuthPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var tokens = JsonSerializer.Deserialize(json, MiJiaJsonContext.Default.MiAuthTokens);
            return (tokens != null && tokens.IsValid) ? tokens : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetAccountsLedgerPath()
    {
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDir = Path.Combine(userHome, ".config", "mijia-api");
        return Path.Combine(configDir, "auth_accounts.json");
    }

    /// <inheritdoc />
    public async Task SaveTokensAsync(MiAuthTokens tokens, string? customPath = null, CancellationToken cancellationToken = default)
    {
        string path = customPath ?? GetDefaultAuthPath();
        string dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(tokens, MiJiaJsonContext.Default.MiAuthTokens);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

        // 同步追加或更新到多账号存储列表
        try
        {
            var accounts = await GetSavedAccountsAsync(cancellationToken).ConfigureAwait(false);
            accounts.RemoveAll(a => a.UserId == tokens.UserId);
            accounts.Insert(0, tokens);

            string ledgerPath = GetAccountsLedgerPath();
            string ledgerJson = JsonSerializer.Serialize(accounts, MiJiaJsonContext.Default.ListMiAuthTokens);
            await File.WriteAllTextAsync(ledgerPath, ledgerJson, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    /// <inheritdoc />
    public async Task<List<MiAuthTokens>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
    {
        string ledgerPath = GetAccountsLedgerPath();
        if (!File.Exists(ledgerPath))
        {
            // 若 auth_accounts.json 不存在但 auth.json 存在，做一次自动迁移
            var current = await LoadSavedTokensAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (current != null && current.IsValid)
            {
                return [current];
            }
            return [];
        }

        try
        {
            string json = await File.ReadAllTextAsync(ledgerPath, cancellationToken).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(json, MiJiaJsonContext.Default.ListMiAuthTokens);
            return list?.Where(x => x.IsValid).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task RemoveAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await GetSavedAccountsAsync(cancellationToken).ConfigureAwait(false);
            accounts.RemoveAll(a => a.UserId == userId);

            string ledgerPath = GetAccountsLedgerPath();
            string ledgerJson = JsonSerializer.Serialize(accounts, MiJiaJsonContext.Default.ListMiAuthTokens);
            await File.WriteAllTextAsync(ledgerPath, ledgerJson, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    /// <inheritdoc />
    public void ClearSavedTokens(string? customPath = null)
    {
        string path = customPath ?? GetDefaultAuthPath();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string CleanJsonPrefix(string raw)
    {
        if (raw.StartsWith("&&&START&&&"))
        {
            return raw.Substring(11).Trim();
        }
        return raw.Trim();
    }

    private static string GetStringSafe(JsonNode node, string propertyName)
    {
        return GetOptionalString(node, propertyName) ?? string.Empty;
    }

    private static string? GetOptionalString(JsonNode node, string propertyName)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue(propertyName, out var prop) || prop == null)
        {
            return null;
        }

        if (prop is JsonValue val)
        {
            // 如果底层是字符串、数字或布尔，统一安全转为字符串
            if (val.TryGetValue<string>(out var str))
            {
                return str;
            }
            if (val.TryGetValue<long>(out var intVal))
            {
                return intVal.ToString();
            }
            if (val.TryGetValue<double>(out var dblVal))
            {
                return dblVal.ToString();
            }
            if (val.TryGetValue<bool>(out var boolVal))
            {
                return boolVal ? "true" : "false";
            }
        }

        return prop.ToString();
    }
}

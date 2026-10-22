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
}

public sealed class MiAuthService : IMiAuthService
{
    private readonly HttpClient _http;
    private const string DefaultSid = "xiaomiio";

    public MiAuthService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
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
        // 小米返回的可能包含 &&&START&&& 前缀，做清理
        json = CleanJsonPrefix(json);

        var node = JsonNode.Parse(json);
        if (node == null)
        {
            throw new InvalidOperationException("Failed to parse QR login response: " + json);
        }

        string qrUrl = node["qr"]?.GetValue<string>() ?? string.Empty;
        string lp = node["lp"]?.GetValue<string>() ?? string.Empty;
        string? sign = node["_sign"]?.GetValue<string>();

        if (string.IsNullOrEmpty(lp))
        {
            throw new InvalidOperationException("Missing polling URL in login response.");
        }

        return new LoginQrCodeResult
        {
            QrUrl = qrUrl,
            QrContent = qrUrl,
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
                case 0: // 扫码登录成功，处理回调
                    string? location = node["location"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(location))
                    {
                        return new QrPollResult { State = QrLoginState.Failed, Message = "Missing redirect location." };
                    }

                    var tokens = await ExchangeTokensAsync(location, cancellationToken).ConfigureAwait(false);
                    return new QrPollResult
                    {
                        State = QrLoginState.Confirmed,
                        Tokens = tokens,
                        Message = "Login success."
                    };

                case 70016: // 二维码已失效
                    return new QrPollResult { State = QrLoginState.Expired, Message = "QR code expired." };

                case 87001: // 已扫码，等待手机端点击确认
                    return new QrPollResult { State = QrLoginState.ScannedWaitingConfirm, Message = "Scanned, waiting confirmation." };

                default: // 还在等待扫码
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
    private async Task<MiAuthTokens> ExchangeTokensAsync(string location, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, location);
        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);

        string ssecurity = string.Empty;
        string userId = string.Empty;
        string serviceToken = string.Empty;
        string? cUserId = null;
        string? passToken = null;

        // 从 Set-Cookie 头提取 serviceToken 与 userId
        if (res.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var cookie in cookieHeaders)
            {
                var match = Regex.Match(cookie, @"([^=;\s]+)=([^;]+)");
                if (match.Success)
                {
                    string name = match.Groups[1].Value.Trim();
                    string val = match.Groups[2].Value.Trim();

                    if (name.Equals("serviceToken", StringComparison.OrdinalIgnoreCase)) serviceToken = val;
                    else if (name.Equals("userId", StringComparison.OrdinalIgnoreCase)) userId = val;
                    else if (name.Equals("cUserId", StringComparison.OrdinalIgnoreCase)) cUserId = val;
                    else if (name.Equals("passToken", StringComparison.OrdinalIgnoreCase)) passToken = val;
                }
            }
        }

        // 也从响应正文获取（部分接口会直接在 JSON 里返回 ssecurity 和 nonce）
        string body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var node = JsonNode.Parse(CleanJsonPrefix(body));
            if (node != null)
            {
                if (string.IsNullOrEmpty(ssecurity)) ssecurity = node["ssecurity"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrEmpty(userId)) userId = node["userId"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrEmpty(serviceToken)) serviceToken = node["serviceToken"]?.GetValue<string>() ?? string.Empty;
            }
        }
        catch
        {
            // 非 JSON 格式时从 Query 或 Set-Cookie 已取到
        }

        // 如果 location 本身带 ssecurity 参数
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
    }

    private static string CleanJsonPrefix(string raw)
    {
        if (raw.StartsWith("&&&START&&&"))
        {
            return raw.Substring(11).Trim();
        }
        return raw.Trim();
    }
}

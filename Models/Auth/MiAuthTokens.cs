using System.Text.Json.Serialization;

namespace MiJia.Net.Models.Auth;

/// <summary>
/// 扫码登录各阶段状态
/// </summary>
public enum QrLoginState
{
    /// <summary>
    /// 等待用户手机扫描二维码
    /// </summary>
    WaitingScan,

    /// <summary>
    /// 用户已扫描，等待手机端点击确认登录
    /// </summary>
    ScannedWaitingConfirm,

    /// <summary>
    /// 用户在手机端已确认授权，登录成功
    /// </summary>
    Confirmed,

    /// <summary>
    /// 二维码已超时失效，需重新获取
    /// </summary>
    Expired,

    /// <summary>
    /// 扫码异常或失败
    /// </summary>
    Failed
}

/// <summary>
/// 请求二维码后返回的数据
/// </summary>
public sealed class LoginQrCodeResult
{
    /// <summary>
    /// 二维码图片直链 URL
    /// </summary>
    public required string QrUrl { get; set; }

    /// <summary>
    /// 二维码对应的内容数据（如用于自定义在界面/终端渲染二维码）
    /// </summary>
    public required string QrContent { get; set; }

    /// <summary>
    /// 扫码状态长轮询请求地址
    /// </summary>
    public required string CheckUrl { get; set; }

    /// <summary>
    /// Sign 标识
    /// </summary>
    public string? Sign { get; set; }
}

/// <summary>
/// 轮询扫码结果
/// </summary>
public sealed class QrPollResult
{
    public QrLoginState State { get; set; }
    public string? Message { get; set; }
    public MiAuthTokens? Tokens { get; set; }
}

/// <summary>
/// 米家云端长期登录与签名凭证
/// </summary>
public sealed class MiAuthTokens
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("serviceToken")]
    public string ServiceToken { get; set; } = string.Empty;

    [JsonPropertyName("ssecurity")]
    public string Ssecurity { get; set; } = string.Empty;

    [JsonPropertyName("cUserId")]
    public string? CUserId { get; set; }

    [JsonPropertyName("passToken")]
    public string? PassToken { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; } = "cn";

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 判断凭据是否有效
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(UserId) &&
        !string.IsNullOrWhiteSpace(ServiceToken) &&
        !string.IsNullOrWhiteSpace(Ssecurity);
}

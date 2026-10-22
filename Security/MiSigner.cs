using System.Security.Cryptography;
using System.Text;

namespace MiJia.Net.Security;

/// <summary>
/// 小米米家云端 API 签名与 Nonce 生成器
/// </summary>
public static class MiSigner
{
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    /// <summary>
    /// 生成符合米家协议规范的 Nonce
    /// 规则：12 字节随机数 Base64 字符串 + ":" + (当前毫秒时间戳 / 60000)
    /// </summary>
    public static string GenerateNonce(long? serverTimeDiffMs = null)
    {
        byte[] randomBytes = new byte[12];
        Rng.GetBytes(randomBytes);
        string randBase64 = Convert.ToBase64String(randomBytes);

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (serverTimeDiffMs.HasValue)
        {
            nowMs += serverTimeDiffMs.Value;
        }

        long timeMinute = nowMs / 60000;
        return $"{randBase64}:{timeMinute}";
    }

    /// <summary>
    /// 基于 ssecurity 和 nonce 计算 signed_nonce
    /// signed_nonce = Base64(SHA256(Base64Decode(ssecurity) + Base64Decode(nonce)))
    /// </summary>
    public static string GenerateSignedNonce(string ssecurity, string nonce)
    {
        byte[] ssecurityBytes = Convert.FromBase64String(ssecurity);
        byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);

        byte[] buffer = new byte[ssecurityBytes.Length + nonceBytes.Length];
        Buffer.BlockCopy(ssecurityBytes, 0, buffer, 0, ssecurityBytes.Length);
        Buffer.BlockCopy(nonceBytes, 0, buffer, ssecurityBytes.Length, nonceBytes.Length);

        byte[] hash = SHA256.HashData(buffer);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 计算米家 API 请求的 signature
    /// signature = Base64(HMAC-SHA256(Base64Decode(signed_nonce), $"{path}&{signed_nonce}&{nonce}&data={data}"))
    /// </summary>
    /// <param name="path">请求相对路径，如 "/app/home/device_list"</param>
    /// <param name="signedNonce">派生 signed_nonce</param>
    /// <param name="nonce">请求随机 nonce</param>
    /// <param name="data">表单或请求体中的 data 字段字符串</param>
    public static string GenerateSignature(string path, string signedNonce, string nonce, string? data)
    {
        // 签名串格式：URI & signedNonce & nonce [& data=...]
        var sb = new StringBuilder();
        sb.Append(path).Append('&').Append(signedNonce).Append('&').Append(nonce);

        if (!string.IsNullOrEmpty(data))
        {
            sb.Append("&data=").Append(data);
        }

        byte[] keyBytes = Convert.FromBase64String(signedNonce);
        byte[] messageBytes = Encoding.UTF8.GetBytes(sb.ToString());

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}

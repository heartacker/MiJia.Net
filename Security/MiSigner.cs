using System.Buffers.Binary;
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
    /// 结构：8字节随机数 + 大端序可变长字节(毫秒时间戳 / 60000)，随后 Base64
    /// </summary>
    public static string GenerateNonce(long? serverTimeDiffMs = null)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (serverTimeDiffMs.HasValue)
        {
            nowMs += serverTimeDiffMs.Value;
        }

        long timeMinute = nowMs / 60000;

        // 计算 timeMinute 所需最少字节数 (Big-Endian)
        int timeBytesLen = 0;
        long temp = timeMinute;
        while (temp > 0)
        {
            timeBytesLen++;
            temp >>= 8;
        }
        if (timeBytesLen == 0) timeBytesLen = 1;

        byte[] buffer = new byte[8 + timeBytesLen];
        Rng.GetBytes(buffer, 0, 8);

        for (int i = 0; i < timeBytesLen; i++)
        {
            buffer[8 + timeBytesLen - 1 - i] = (byte)(timeMinute & 0xFF);
            timeMinute >>= 8;
        }

        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    /// 基于 ssecurity 和 nonce 计算 signed_nonce
    /// signed_nonce = Base64(SHA256(Base64Decode(ssecurity) + Base64Decode(nonce)))
    /// </summary>
    public static string GenerateSignedNonce(string ssecurity, string nonce)
    {
        byte[] ssecurityBytes = Convert.FromBase64String(ssecurity);
        byte[] nonceBytes = Convert.FromBase64String(nonce);

        byte[] buffer = new byte[ssecurityBytes.Length + nonceBytes.Length];
        Buffer.BlockCopy(ssecurityBytes, 0, buffer, 0, ssecurityBytes.Length);
        Buffer.BlockCopy(nonceBytes, 0, buffer, ssecurityBytes.Length, nonceBytes.Length);

        byte[] hash = SHA256.HashData(buffer);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 计算米家 RC4 加密接口的 SHA1 签名
    /// 签名格式：METHOD & URI & param1=val1 & ... & signedNonce
    /// </summary>
    public static string GenerateEncSignature(string path, string method, string signedNonce, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var sb = new StringBuilder();
        sb.Append(method.ToUpperInvariant()).Append('&');
        sb.Append(path);

        foreach (var kvp in parameters)
        {
            sb.Append('&').Append(kvp.Key).Append('=').Append(kvp.Value);
        }

        sb.Append('&').Append(signedNonce);

        byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = SHA1.HashData(data);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 计算普通米家 API 请求的 signature (HMAC-SHA256)
    /// signature = Base64(HMAC-SHA256(Base64Decode(signed_nonce), $"{path}&{signed_nonce}&{nonce}&data={data}"))
    /// </summary>
    public static string GenerateSignature(string path, string signedNonce, string nonce, string? data)
    {
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

using System.Security.Cryptography;
using System.Text;

namespace MiJia.Net.Security;

/// <summary>
/// 高性能零依赖 RC4-drop1024 加解密器（米家 MIOT-ENCRYPT-ALGORITHM: ENCRYPT-RC4 协议标准实现）
/// </summary>
public sealed class MiRc4
{
    private readonly byte[] _s = new byte[256];
    private byte _i;
    private byte _j;

    public MiRc4(ReadOnlySpan<byte> key)
    {
        for (int idx = 0; idx < 256; idx++)
        {
            _s[idx] = (byte)idx;
        }

        byte j = 0;
        for (int idx = 0; idx < 256; idx++)
        {
            j = (byte)(j + _s[idx] + key[idx % key.Length]);
            (_s[idx], _s[j]) = (_s[j], _s[idx]);
        }

        _i = 0;
        _j = 0;

        // 小米协议固定规则：抛弃前 1024 字节伪随机流 (Drop 1024)
        Span<byte> dropBuffer = stackalloc byte[1024];
        CryptInPlace(dropBuffer);
    }

    /// <summary>
    /// 原地加密 / 解密（RC4 对称性）
    /// </summary>
    public void CryptInPlace(Span<byte> data)
    {
        for (int k = 0; k < data.Length; k++)
        {
            _i = (byte)(_i + 1);
            _j = (byte)(_j + _s[_i]);
            (_s[_i], _s[_j]) = (_s[_j], _s[_i]);
            byte t = (byte)(_s[_i] + _s[_j]);
            data[k] ^= _s[t];
        }
    }

    /// <summary>
    /// 使用 base64Key (如 signed_nonce) 加密 UTF-8 字符串并返回 Base64 密文
    /// </summary>
    public static string Encrypt(string base64Key, string plainText)
    {
        byte[] keyBytes = Convert.FromBase64String(base64Key);
        byte[] dataBytes = Encoding.UTF8.GetBytes(plainText);

        var rc4 = new MiRc4(keyBytes);
        rc4.CryptInPlace(dataBytes);

        return Convert.ToBase64String(dataBytes);
    }

    /// <summary>
    /// 使用 base64Key (如 signed_nonce) 解密 Base64 密文并返回原始 UTF-8 字符串
    /// </summary>
    public static string Decrypt(string base64Key, string cipherBase64)
    {
        byte[] keyBytes = Convert.FromBase64String(base64Key);
        byte[] cipherBytes = Convert.FromBase64String(cipherBase64);

        var rc4 = new MiRc4(keyBytes);
        rc4.CryptInPlace(cipherBytes);

        return Encoding.UTF8.GetString(cipherBytes);
    }
}

using System.Text.RegularExpressions;

namespace MiJia.Net.Devices;

/// <summary>
/// 多位开关/分路虚拟子设备识别与解析工具类（格式：&lt;父did&gt;.sN，如 123456789.s2）
/// </summary>
public static class SubDeviceHelper
{
    private static readonly Regex SubDeviceSuffix = new(@"\.s(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// 判断指定的 DID 是否为虚拟分路子设备
    /// </summary>
    public static bool IsSubDevice(string? did)
    {
        if (string.IsNullOrEmpty(did)) return false;
        return SubDeviceSuffix.IsMatch(did);
    }

    /// <summary>
    /// 获取虚拟分路子设备所属的真实父设备 DID；如果不是子设备则返回 null
    /// </summary>
    public static string? GetParentDid(string? did)
    {
        if (string.IsNullOrEmpty(did)) return null;
        var match = SubDeviceSuffix.Match(did);
        return match.Success ? did[..match.Index] : null;
    }

    /// <summary>
    /// 获取虚拟分路子设备的路数序号（例如 123456.s2 返回 2）；如果不是子设备则返回 null
    /// </summary>
    public static int? GetGangNumber(string? did)
    {
        if (string.IsNullOrEmpty(did)) return null;
        var match = SubDeviceSuffix.Match(did);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int gang))
        {
            return gang;
        }
        return null;
    }

    /// <summary>
    /// 构造虚拟分路子设备的 DID
    /// </summary>
    public static string CreateSubDid(string parentDid, int gang)
    {
        return $"{parentDid}.s{gang}";
    }
}

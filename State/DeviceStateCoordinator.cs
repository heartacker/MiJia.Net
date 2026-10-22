using System.Collections.Concurrent;
using MiJia.Net.Models.Devices;

namespace MiJia.Net.State;

/// <summary>
/// 设备属性更新租约（用于解决云端异步轮询导致的状态回弹与竞争）
/// </summary>
public sealed class PropertyLease
{
    public string Did { get; init; } = string.Empty;
    public int Siid { get; init; }
    public int Piid { get; init; }
    public object? TargetValue { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3.5);

    public bool IsActive => DateTimeOffset.UtcNow - Timestamp < Duration;
}

public interface IDeviceStateCoordinator
{
    /// <summary>
    /// 当本地用户触发属性改变（如点击 Toggle 开关、拖动滑块）时登记租约
    /// </summary>
    void RecordLocalChange(string did, int siid, int piid, object? targetValue, TimeSpan? duration = null);

    /// <summary>
    /// 云端轮询回最新属性时，根据租约决定是否采纳云端值
    /// 如果返回 false，说明当前属性处于本地租约保护期，应保留本地预测值，忽略云端旧值
    /// </summary>
    bool ShouldAcceptRemoteValue(string did, int siid, int piid, object? remoteValue, out object? effectiveValue);

    /// <summary>
    /// 强制清除某属性的租约（例如操作确认成功或用户取消）
    /// </summary>
    void ClearLease(string did, int siid, int piid);
}

public sealed class DeviceStateCoordinator : IDeviceStateCoordinator
{
    // Key: $"{did}:{siid}:{piid}"
    private readonly ConcurrentDictionary<string, PropertyLease> _leases = new();

    private static string BuildKey(string did, int siid, int piid) => $"{did}:{siid}:{piid}";

    public void RecordLocalChange(string did, int siid, int piid, object? targetValue, TimeSpan? duration = null)
    {
        string key = BuildKey(did, siid, piid);
        var lease = new PropertyLease
        {
            Did = did,
            Siid = siid,
            Piid = piid,
            TargetValue = targetValue,
            Timestamp = DateTimeOffset.UtcNow,
            Duration = duration ?? TimeSpan.FromSeconds(3.5)
        };
        _leases[key] = lease;
    }

    public bool ShouldAcceptRemoteValue(string did, int siid, int piid, object? remoteValue, out object? effectiveValue)
    {
        string key = BuildKey(did, siid, piid);
        if (_leases.TryGetValue(key, out var lease))
        {
            if (lease.IsActive)
            {
                // 如果云端值已经与用户设定的预期值一致，说明云端已经同步完成，可以提前释放租约
                if (Equals(remoteValue, lease.TargetValue))
                {
                    _leases.TryRemove(key, out _);
                    effectiveValue = remoteValue;
                    return true;
                }

                // 否则当前处于保护期，拒绝旧的云端状态，保持用户设置的值
                effectiveValue = lease.TargetValue;
                return false;
            }
            else
            {
                // 租约已超时，移除
                _leases.TryRemove(key, out _);
            }
        }

        effectiveValue = remoteValue;
        return true;
    }

    public void ClearLease(string did, int siid, int piid)
    {
        string key = BuildKey(did, siid, piid);
        _leases.TryRemove(key, out _);
    }
}

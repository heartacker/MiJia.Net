using System.Threading.Channels;
using MiJia.Net.Cloud;
using MiJia.Net.Models.Devices;

namespace MiJia.Net.State;

public interface IDeviceJobChannel : IAsyncDisposable
{
    /// <summary>
    /// 将一个属性写操作推入队列（支持防抖合并）
    /// </summary>
    ValueTask EnqueueSetPropertyAsync(string did, int siid, int piid, object value);
}

public sealed class DeviceJobChannel : IDeviceJobChannel
{
    private readonly IMiCloudClient _client;
    private readonly IDeviceStateCoordinator? _coordinator;
    private readonly Channel<MiotPropSetTarget> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processLoopTask;

    public DeviceJobChannel(IMiCloudClient client, IDeviceStateCoordinator? coordinator = null, int capacity = 200)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _coordinator = coordinator;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // 队列满时丢弃最老的未处理请求
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<MiotPropSetTarget>(options);

        _processLoopTask = Task.Run(ProcessQueueAsync);
    }

    public async ValueTask EnqueueSetPropertyAsync(string did, int siid, int piid, object value)
    {
        // 1. 同步登记本地防回弹租约
        _coordinator?.RecordLocalChange(did, siid, piid, value);

        // 2. 写入通道
        var target = new MiotPropSetTarget
        {
            Did = did,
            Siid = siid,
            Piid = piid,
            Value = value
        };

        await _channel.Writer.WriteAsync(target).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync()
    {
        var buffer = new List<MiotPropSetTarget>();

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // 等待至少一个任务到达
                if (!await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    break;
                }

                // 取出当前可读的所有项进行防抖合并
                buffer.Clear();
                if (_channel.Reader.TryRead(out var firstItem))
                {
                    buffer.Add(firstItem);
                }

                while (_channel.Reader.TryRead(out var item))
                {
                    buffer.Add(item);
                }

                if (buffer.Count == 0) continue;

                // 防抖合并规则：对于同一个 did+siid+piid，只保留最后提交的一条
                var distinctJobs = buffer
                    .GroupBy(x => $"{x.Did}:{x.Siid}:{x.Piid}")
                    .Select(g => g.Last())
                    .ToList();

                // 批量下发给米家云端
                await _client.SetPropertiesAsync(distinctJobs, _cts.Token).ConfigureAwait(false);

                // 微小缓冲，避免突发高频冲击米家云端服务器
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 网络异常时不让处理循环崩溃，短延时后继续
                await Task.Delay(500, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        try
        {
            await _processLoopTask.ConfigureAwait(false);
        }
        catch
        {
            // ignore cancellation
        }
        _cts.Dispose();
    }
}

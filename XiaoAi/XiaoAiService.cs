using MiJia.Net.Cloud;
using MiJia.Net.Models.Devices;
using MiJia.Net.Models.Spec;
using MiJia.Net.Spec;

namespace MiJia.Net.XiaoAi;

public interface IXiaoAiService
{
    /// <summary>
    /// 向指定小爱音箱设备发送文字指令（如“打开客厅风扇”）
    /// </summary>
    /// <param name="did">小爱音箱的 DID</param>
    /// <param name="commandText">要执行的自然语言指令</param>
    /// <param name="silent">是否静默执行（不播报语音回复）</param>
    /// <param name="model">设备型号（可选，用于动态适配 Spec 的 Action 定义）</param>
    Task<bool> ExecuteTextDirectiveAsync(string did, string commandText, bool silent = false, string? model = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 让指定小爱音箱朗读指定文本 (TTS)
    /// </summary>
    /// <param name="did">小爱音箱的 DID</param>
    /// <param name="text">朗读的内容</param>
    /// <param name="model">设备型号（可选，用于动态适配 Spec 的 Action 定义）</param>
    Task<bool> PlayTtsAsync(string did, string text, string? model = null, CancellationToken cancellationToken = default);
}

public sealed class XiaoAiService : IXiaoAiService
{
    private readonly IMiCloudClient _client;
    private readonly IMiotSpecClient? _specClient;

    public XiaoAiService(IMiCloudClient client, IMiotSpecClient? specClient = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _specClient = specClient;
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteTextDirectiveAsync(
        string did,
        string commandText,
        bool silent = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did)) throw new ArgumentException("DID cannot be empty", nameof(did));
        if (string.IsNullOrWhiteSpace(commandText)) throw new ArgumentException("Command text cannot be empty", nameof(commandText));

        int siid = 5;
        int aiid = 1;
        List<object> inParams = [commandText, silent ? 1 : 0];

        // 尝试从规格定义库中查找该音箱型号的 Execute Text Directive 对应 siid 与 aiid
        if (_specClient != null && !string.IsNullOrEmpty(model))
        {
            try
            {
                var spec = await _specClient.GetSpecByModelAsync(model, cancellationToken).ConfigureAwait(false);
                if (spec != null)
                {
                    var (foundSiid, foundAiid, inCount) = FindDirectiveAction(spec);
                    if (foundSiid > 0 && foundAiid > 0)
                    {
                        siid = foundSiid;
                        aiid = foundAiid;
                        inParams = inCount == 1 ? [commandText] : [commandText, silent ? 1 : 0];
                    }
                }
            }
            catch
            {
                // 回退到默认推导
            }
        }

        // 若型号中明确含有 oh2p（如小吃音箱 Pro、小小音箱 Pro），常见 siid=7, aiid=4
        if (model != null && model.Contains("oh2p", StringComparison.OrdinalIgnoreCase))
        {
            siid = 7;
            aiid = 4;
        }
        else if (model != null && model.Contains("lx06", StringComparison.OrdinalIgnoreCase))
        {
            siid = 5;
            aiid = 5;
        }

        var target = new MiotActionTarget
        {
            Did = did,
            Siid = siid,
            Aiid = aiid,
            In = inParams
        };

        var result = await _client.RunActionAsync(target, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess) return true;

        // 如果首选 Action 执行失败，且尚未尝试备用 Action，进行容错降级重试：
        // 尝试候选 Action：(5, 5), (7, 4), (5, 1)
        var fallbackActions = new[] { (5, 5), (7, 4), (5, 1) };
        foreach (var (altSiid, altAiid) in fallbackActions)
        {
            if (altSiid == siid && altAiid == aiid) continue;

            var retryTarget = new MiotActionTarget
            {
                Did = did,
                Siid = altSiid,
                Aiid = altAiid,
                In = [commandText, silent ? 1 : 0]
            };
            var retryRes = await _client.RunActionAsync(retryTarget, cancellationToken).ConfigureAwait(false);
            if (retryRes.IsSuccess) return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> PlayTtsAsync(
        string did, 
        string text, 
        string? model = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did)) throw new ArgumentException("DID cannot be empty", nameof(did));
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text cannot be empty", nameof(text));

        int siid = 5;
        int aiid = 1;

        if (model != null && model.Contains("oh2p", StringComparison.OrdinalIgnoreCase))
        {
            siid = 7;
            aiid = 3;
        }

        var target = new MiotActionTarget
        {
            Did = did,
            Siid = siid,
            Aiid = aiid,
            In = [text]
        };

        var result = await _client.RunActionAsync(target, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    private static (int siid, int aiid, int inCount) FindDirectiveAction(MiotSpecInstance spec)
    {
        foreach (var s in spec.Services)
        {
            if (s.Actions == null) continue;
            foreach (var a in s.Actions)
            {
                if (a.Type.Contains(":action:execute-text-directive:") ||
                    a.Description.Contains("execute-text-directive", StringComparison.OrdinalIgnoreCase) ||
                    a.Description.Contains("文字指令", StringComparison.OrdinalIgnoreCase))
                {
                    return (s.Iid, a.Iid, a.In?.Count ?? 2);
                }
            }
        }
        return (0, 0, 0);
    }
}

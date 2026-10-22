using MiJia.Net.Cloud;
using MiJia.Net.Models.Devices;

namespace MiJia.Net.XiaoAi;

public interface IXiaoAiService
{
    /// <summary>
    /// 向指定小爱音箱设备发送文字指令（如“打开客厅风扇”）
    /// </summary>
    /// <param name="did">小爱音箱的 DID</param>
    /// <param name="commandText">要执行的自然语言指令</param>
    /// <param name="silent">是否静默执行（不播报语音回复）</param>
    Task<bool> ExecuteTextDirectiveAsync(string did, string commandText, bool silent = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 让指定小爱音箱朗读指定文本 (TTS)
    /// </summary>
    /// <param name="did">小爱音箱的 DID</param>
    /// <param name="text">朗读的内容</param>
    Task<bool> PlayTtsAsync(string did, string text, CancellationToken cancellationToken = default);
}

public sealed class XiaoAiService : IXiaoAiService
{
    private readonly IMiCloudClient _client;

    public XiaoAiService(IMiCloudClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteTextDirectiveAsync(
        string did,
        string commandText,
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did)) throw new ArgumentException("DID cannot be empty", nameof(did));
        if (string.IsNullOrWhiteSpace(commandText)) throw new ArgumentException("Command text cannot be empty", nameof(commandText));

        // 小爱文字指令标准 Action:
        // Service: Intelligent Speaker (siid: 5)
        // Action: Execute Text Directive (aiid: 1)
        // in: [text, silent_mode: 0 或 1]
        var target = new MiotActionTarget
        {
            Did = did,
            Siid = 5,
            Aiid = 1,
            In = [commandText, silent ? 1 : 0]
        };

        var result = await _client.RunActionAsync(target, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<bool> PlayTtsAsync(string did, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did)) throw new ArgumentException("DID cannot be empty", nameof(did));
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text cannot be empty", nameof(text));

        // 小爱 TTS 播放标准 Action:
        // Service: Intelligent Speaker (siid: 5)
        // Action: Play Text (aiid: 2 或 3，视型号而定，常用 1 或通过属性设置)
        var target = new MiotActionTarget
        {
            Did = did,
            Siid = 5,
            Aiid = 2,
            In = [text]
        };

        var result = await _client.RunActionAsync(target, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess;
    }
}

using System.Globalization;

namespace MiJia.Net.Spec;

/// <summary>
/// 环境指标文本格式化工具（对齐 Python 端的 format_metrics_text）
/// </summary>
public static class MetricsFormatter
{
    public static string FormatTemp(double value)
    {
        return $"{Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture)}°C";
    }

    public static string FormatHumidity(double value)
    {
        return $"{Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>
    /// 将温度和湿度格式化为摘要文本（例如 "24.5°C  55%"）
    /// </summary>
    public static string? FormatMetricsText(double? temp, double? hum)
    {
        if (!temp.HasValue && !hum.HasValue)
            return null;

        if (temp.HasValue && hum.HasValue)
            return $"{FormatTemp(temp.Value)}  {FormatHumidity(hum.Value)}";

        if (temp.HasValue)
            return FormatTemp(temp.Value);

        return FormatHumidity(hum!.Value);
    }
}

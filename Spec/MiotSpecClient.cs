using System.Collections.Concurrent;
using System.Text.Json;
using MiJia.Net.Models.Spec;
using MiJia.Net.Serialization;

namespace MiJia.Net.Spec;

public interface IMiotSpecClient
{
    /// <summary>
    /// 获取设备规格定义（优先从内存/磁盘缓存中读取，未命中则请求网络）
    /// </summary>
    /// <param name="urn">如 urn:miot-spec-v2:device:switch:0000A003:cuco-cp1:1</param>
    Task<MiotSpecInstance?> GetSpecInstanceAsync(string urn, CancellationToken cancellationToken = default);
}

public sealed class MiotSpecClient : IMiotSpecClient
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, MiotSpecInstance> _memoryCache = new();

    public MiotSpecClient(HttpClient? httpClient = null, string? cacheDir = null)
    {
        _http = httpClient ?? new HttpClient();
        
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _cacheDir = cacheDir ?? Path.Combine(userHome, ".config", "mijia-api", "spec_cache");
        
        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }
    }

    public async Task<MiotSpecInstance?> GetSpecInstanceAsync(string urn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urn)) return null;

        // 1. 内存缓存
        if (_memoryCache.TryGetValue(urn, out var cached))
        {
            return cached;
        }

        // 2. 磁盘缓存
        string safeFileName = urn.Replace(":", "_").Replace("/", "_") + ".json";
        string diskPath = Path.Combine(_cacheDir, safeFileName);

        if (File.Exists(diskPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(diskPath, cancellationToken).ConfigureAwait(false);
                var spec = JsonSerializer.Deserialize(json, MiJiaJsonContext.Default.MiotSpecInstance);
                if (spec != null)
                {
                    _memoryCache[urn] = spec;
                    return spec;
                }
            }
            catch
            {
                // 缓存文件损坏则回退网络重新获取
            }
        }

        // 3. 网络获取
        string url = $"https://miot-spec.org/miot-spec-v2/instance?type={Uri.EscapeDataString(urn)}";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string rawJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var instance = JsonSerializer.Deserialize(rawJson, MiJiaJsonContext.Default.MiotSpecInstance);
        if (instance != null)
        {
            _memoryCache[urn] = instance;
            // 异步写入磁盘缓存
            try
            {
                await File.WriteAllTextAsync(diskPath, rawJson, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 缓存写入失败不阻断业务
            }
        }

        return instance;
    }
}

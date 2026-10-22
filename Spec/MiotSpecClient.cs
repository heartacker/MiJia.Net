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

    /// <summary>
    /// 根据设备 model 获取规格定义（优先通过实例库解析 model -> urn）
    /// </summary>
    Task<MiotSpecInstance?> GetSpecByModelAsync(string model, CancellationToken cancellationToken = default);
}

public sealed class MiotSpecClient : IMiotSpecClient
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly string _instancesIndexPath;
    private readonly ConcurrentDictionary<string, MiotSpecInstance> _memoryCache = new();
    private readonly ConcurrentDictionary<string, string> _modelToUrnMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexLoaded = false;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public MiotSpecClient(HttpClient? httpClient = null, string? cacheDir = null)
    {
        _http = httpClient ?? new HttpClient();
        
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _cacheDir = cacheDir ?? Path.Combine(userHome, ".config", "mijia-api", "spec_cache");
        _instancesIndexPath = Path.Combine(userHome, ".config", "mijia-api", "instances_all.json");
        
        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
        }
    }

    private async Task EnsureIndexLoadedAsync(CancellationToken cancellationToken)
    {
        if (_indexLoaded) return;
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_indexLoaded) return;

            string? json = null;
            if (File.Exists(_instancesIndexPath))
            {
                try
                {
                    json = await File.ReadAllTextAsync(_instancesIndexPath, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (string.IsNullOrEmpty(json))
            {
                try
                {
                    using var resp = await _http.GetAsync("https://miot-spec.org/miot-spec-v2/instances?status=all", cancellationToken).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        _ = File.WriteAllTextAsync(_instancesIndexPath, json, cancellationToken);
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("instances", out var instArr) && instArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in instArr.EnumerateArray())
                    {
                        if (elem.TryGetProperty("model", out var mProp) && elem.TryGetProperty("type", out var tProp))
                        {
                            string? model = mProp.GetString();
                            string? type = tProp.GetString();
                            if (!string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(type))
                            {
                                // 优先记录已发布或版本较高的
                                _modelToUrnMap[model] = type;
                            }
                        }
                    }
                }
            }

            _indexLoaded = true;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<MiotSpecInstance?> GetSpecByModelAsync(string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        await EnsureIndexLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (_modelToUrnMap.TryGetValue(model, out var urn))
        {
            return await GetSpecInstanceAsync(urn, cancellationToken).ConfigureAwait(false);
        }

        return null;
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

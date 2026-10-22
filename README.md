# MiJia.Net

[![NuGet Version](https://img.shields.io/nuget/v/MiJia.Net.svg)](https://www.nuget.org/packages/MiJia.Net)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-blue.svg)](https://dotnet.microsoft.com/)

现代、高性能且跨平台的**小米米家 (Xiaomi MiHome) 云端 API C# SDK**。

支持扫码登录授权、长轮询认证、云端接口 HMAC 签名、MIoT Spec 元数据自动解析与磁盘缓存、状态防回弹协调器（Anti-rebound Lease）以及高频操作串行防抖通道（Debounce JobChannel），完全零反射并天然支持 **Native AOT**。

---

## 特性一览

- 🔐 **开箱即用的扫码登录**：生成标准二维码图片或内容，支持长轮询状态监听、Token 自动持久化。
- ⚡ **米家协议与加密签名**：基于 `ssecurity`、时间戳和 Nonce 的 HMAC-SHA256 签名，完全兼容米家官方 `/app/...` 接口。
- 📦 **MIoT Spec V2 实例解析**：从官方 CDN 高速解析属性、动作、服务、数值范围（`value-range`）与枚举列表（`value-list`），内置二级内存与磁盘缓存。
- 🛡️ **设备状态防回弹协调器 (`DeviceStateCoordinator`)**：解决设备控制后因云端异步轮询延迟导致开关反复回弹的痛点体验。
- 🚀 **并发防抖通道 (`DeviceJobChannel`)**：基于 `System.Threading.Channels` 串行合并高频指令（例如快速拖动滑块调亮度）。
- 🗣️ **小爱音箱联动 (`XiaoAiService`)**：向小爱音箱下发文本指令与文字转语音 (TTS)。

---

## 安装

```bash
dotnet add package MiJia.Net
```

---

## 快速使用

### 1. 扫码登录与凭据存储

```csharp
using MiJia.Net.Auth;

var authService = new MiAuthService();

// 尝试加载本地已有凭证 (默认路径 ~/.config/mijia-api/auth.json)
var tokens = await authService.LoadSavedTokensAsync();

if (tokens == null || !tokens.IsValid)
{
    // 请求登录二维码
    var qrResult = await authService.RequestLoginQrCodeAsync();
    Console.WriteLine($"请使用米家 APP 扫描二维码: {qrResult.QrUrl}");

    // 轮询检查扫码状态
    while (true)
    {
        await Task.Delay(2000);
        var pollResult = await authService.PollQrLoginStatusAsync(qrResult);
        if (pollResult.State == QrLoginState.Confirmed && pollResult.Tokens != null)
        {
            tokens = pollResult.Tokens;
            await authService.SaveTokensAsync(tokens);
            Console.WriteLine("登录成功！");
            break;
        }
    }
}
```

### 2. 拉取家庭与设备列表

```csharp
using MiJia.Net.Cloud;

var client = new MiCloudClient(tokens);

// 获取家庭与房间列表
var homes = await client.GetHomesAsync();

// 获取名下所有设备
var devices = await client.GetDevicesAsync();
foreach (var device in devices)
{
    Console.WriteLine($"[{ (device.IsOnline ? "在线" : "离线") }] {device.Name} ({device.Model}) - DID: {device.Did}");
}
```

### 3. 读取与控制设备属性 (MIoT Spec)

```csharp
using MiJia.Net.Models.Devices;

// 读取开关状态 (假设 siid: 2, piid: 1)
var propResults = await client.GetPropertiesAsync(new[]
{
    new MiotPropGetTarget { Did = "device_did_here", Siid = 2, Piid = 1 }
});

// 打开开关
await client.SetPropertiesAsync(new[]
{
    new MiotPropSetTarget { Did = "device_did_here", Siid = 2, Piid = 1, Value = true }
});
```

### 4. 获取 MIoT Spec 实例元数据

```csharp
using MiJia.Net.Spec;

var specClient = new MiotSpecClient();
var spec = await specClient.GetSpecInstanceAsync("urn:miot-spec-v2:device:switch:0000A003:cuco-cp1:1");

foreach (var service in spec.Services)
{
    Console.WriteLine($"服务: {service.Description} (siid: {service.Iid})");
}
```

---

## 许可证

本项目遵循 [MIT 许可证](LICENSE)。

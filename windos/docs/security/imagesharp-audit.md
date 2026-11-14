# ImageSharp 2.1.13 NuGet 审计豁免分析

> 状态：已豁免（TipCL.csproj `NuGetAuditSuppress` × 5）· 分析日期：2026-11-08 · 关联决策：DECISIONS D-20

## 背景

`dotnet build` 对 `SixLabors.ImageSharp 2.1.13` 报 NU1902/NU1903 共 5 条告警。
ImageSharp 2.x 已停更，修复版 3.2.0 / 4.1.2 起 **仅 target net6.0+**（已下载 3.2.0 nupkg 核实：
`lib/` 下只有 `net6.0`），net48 无法消费。TipCL 的 net48 / Win7 / 8.1 / x86 兼容是硬约束，
因此不能通过升级消除告警。

## 为什么不可达（逐条）

本仓库**唯一**的 ImageSharp 入口是 `Services/WebpFallback.cs`：

```csharp
private static readonly Configuration WebpOnly = new Configuration(new WebpConfigurationModule());
...
using Image image = Image.Load(WebpOnly, data);   // 只注册了 WebP 解码器
image.Save(ms, new PngEncoder());                  // 只用了 PNG 编码器
```

`WebpOnly` 不含 TIFF/JPEG/GIF 等任何其它格式模块；`ToPng` 外层还有 `IsWebp` 魔数守卫，
非 WebP 字节根本不会进入 ImageSharp。全仓库 grep 确认：`HistogramEqualization`、
`IccProfile`、`TiffEncoder`、其它 `Image.Load` 调用 **均为 0 处**。

| 告警 | 漏洞路径 | 触发前提 | 本程序是否可达 |
|---|---|---|---|
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4)（高） | TIFF 浮点解码 + `HistogramEqualization()` 直方图越界 | 解码 TIFF 且调用 HistogramEqualization | ✗ 无 TIFF 模块、零调用 |
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q)（高） | TIFF CCITT T4 编码器缓冲区溢出 | 用 `TiffEncoder` 编码 1-bit 图 | ✗ 零 TiffEncoder 使用 |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w)（高） | TIFF CCITT T6 编码器缓冲区溢出 | 同上 | ✗ 同上 |
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44)（中） | ICC CLUT 解析巨额分配 | 2.x 需主动访问 `IccProfile.Entries` | ✗ 零 Entries 访问；WebP-only 解码不做 ICC 转换 |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g)（中） | BigTIFF IFD 计数死循环 | 解码 BigTIFF | ✗ 无 TIFF 模块 |

5 条漏洞**全部**位于 TIFF / ICC / 直方图处理路径，无一在 WebP 解码或 PNG 编码路径上。

## 为什么不换原生 libwebp

评估过的替代方案与放弃原因：

- **官方 dwebp.exe（静态链接，无运行时依赖）**：1.3.2+ 的官方 Windows 发布包**只有 x64**，
  无 x86 / arm64；采用即丢掉 32 位 Windows 的 WebP 兜底（README 明确承诺 Win7/8.1 可用）。
- **Imazen.WebP.NativeRuntime（libwebp 1.6.x，含 x86/arm64）**：DLL 为 /MD 构建，
  导入 `VCRUNTIME140.dll` 与 UCRT（api-ms-win-crt-*）；干净 Win7/8.1 未必装有
  KB2999226 / VC++ 运行库，会破坏"双击即用、不装任何运行时"的承诺。
- **自编译 libwebp**：本机无 C 工具链；引入工具链超出本任务范围。
- ** vendored ImageSharp WebP 源码子集**：依赖图深（Image<T>/Buffer2D/内存管理），
  提取成本与正确性风险高，收益与豁免相同。

纯托管 ImageSharp 2.1.13 是唯一全架构、零原生依赖、Win7 可用的选项；
在入口被锁死为 WebP-only 的前提下，其审计告警为**误报**。

## 回归守卫

`tests/TipCL.Tests/WebpFallbackTests.cs`：

- `DecoderRegistersOnlyWebp`：反射断言 `WebpOnly` 只注册 WEBP 一种格式——
  未来若有人改用默认 `Configuration`（注册全套格式，TIFF 路径变可达），测试立即失败。
- `NonWebpBytesPassThroughUnchanged` / `CorruptWebpFallsBackToOriginal`：契约守卫。
- `RealWebpDecodesToPng`：用 libwebp 1.6.0 官方 `test.webp` 样本验证解码链路真实可用。

## 退出策略（满足任一即应移除豁免）

1. TipCL 放弃 net48（不再支持 Win7/8.1）→ 升级 ImageSharp ≥ 3.2.0，删除全部豁免。
2. 上游发布修复版 2.1.x（截至分析日无）→ 升级并删除豁免。
3. 项目引入 C 工具链 → 自编译 libwebp ≥ 1.3.2 静态 DLL（x86/x64/arm64），
   走 WebView2Loader 式内嵌释放，替换 ImageSharp 并删除豁免。

**红线：任何新增 ImageSharp 调用必须复用 `WebpFallback.WebpOnly`，禁止使用默认 Configuration。**

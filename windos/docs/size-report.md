# TipCL 体积报告（WP5.1）

> 数据来源：`tools/dump-embedded-resources.ps1`（反射枚举 exe 内嵌资源）。
> 生成于 WP2 完成后（ui-rebuild 分支）。

## 1. 总量

| 阶段 | TipCL.exe 字节数 | 红线 ≤5,707,264 | 备注 |
|---|---|---|---|
| 基线（main @ 7c7014a） | 5,698,048 | ✓ | v1.0.15 实测同档 |
| WP1 后（+dev 工具） | 5,707,776 | ✗ +512 | 曾超限，见 DECISIONS D-7/D-8 |
| WP2 后（当前） | **5,704,192** | ✓（余量 3,072） | Costura IncludeDebugSymbols=false |

exe 总构成（WP2 后）：内嵌资源 2,742,620 B + 自身 IL/BAML/元数据 ≈ 2,961,572 B。

## 2. exe 内嵌资源清单（WP2 后实测，压缩后字节数）

| 字节 | 资源 | 归属 | compact 版处置（WP5.2） |
|---|---|---|---|
| 628,416 | costura.sixlabors.imagesharp.dll.compressed | ImageSharp 2.1.13（WebP 兜底） | **移除**（仓库内解码器替代） |
| 527,029 | costura.system.text.encoding.codepages.dll.compressed | ImageSharp 传递依赖 | **移除**（随 ImageSharp） |
| 226,464 | costura.system.text.json.dll.compressed | System.Text.Json 8.0.5 | 保留（业务用） |
| 166,272 | TipCL.Core.locales.en.json | 词表 | 保留（premium 原样） |
| 160,683 | TipCL.Core.locales.zh_CN.json | 词表 | 保留 |
| 159,344 | TipCL.WebView2Loader.x64.dll | WebView2 原生 loader | 保留 |
| 147,520 | TipCL.WebView2Loader.arm64.dll | 同上 | 保留 |
| 141,114 | costura.microsoft.web.webview2.core.dll.compressed | WebView2 托管 | 保留 |
| 120,944 | TipCL.WebView2Loader.x86.dll | loader x86 | 保留 |
| 60,947 | costura.system.memory.dll.compressed | ImageSharp 传递依赖 | **移除**（随 ImageSharp）* |
| 48,744 | TipCL.g.resources | 图标字体 + XAML 资源 | 保留（红线：图标字体复用） |
| 39,748 | TipCL.Core.ai.model_rules.json | AI 模型规则 | 保留 |
| 39,429 | costura.microsoft.web.webview2.wpf.dll.compressed | WebView2 WPF | 保留 |
| 35,106 | costura.system.text.encodings.web.dll.compressed | STJ 传递依赖 | 保留 |
| 33,140 | costura.system.numerics.vectors.dll.compressed | ImageSharp 传递依赖 | **移除**（随 ImageSharp）* |
| 32,936 | TipCL.Core.plugin-host.host-plugin.mjs | 插件宿主 | 保留 |
| 20,820 | TipCL.Core.plugin-host.host-web.mjs | 插件宿主 | 保留 |
| 20,199 | costura.microsoft.web.webview2.winforms.dll.compressed | WebView2 WinForms（零代码引用） | 排除尝试未遂（D-8），随 WP5.2 复查 |
| 15,196 | costura.microsoft.bcl.asyncinterfaces.dll.compressed | STJ 传递依赖 | 保留 |
| 13,655 | costura.system.valuetuple.dll.compressed | 兼容 | 保留 |
| 13,638 | costura.system.threading.tasks.extensions.dll.compressed | 兼容 | 保留 |
| 13,150 | TipCL.Core.plugin-host.tipcl-plugin.d.ts | 插件 SDK 类型 | 保留 |
| 12,758 | TipCL.Core.plugin-host.host.mjs | 插件宿主 | 保留 |
| 11,557 | costura.system.buffers.dll.compressed | ImageSharp 传递依赖 | **移除**（随 ImageSharp）* |
| 9,619 | costura.system.runtime.compilerservices.unsafe.dll.compressed | 兼容 | 保留 |
| 其余 10 项 | plugin-host 脚本/成本拉元数据 | — | 保留 |

\* System.Memory / Numerics.Vectors / Buffers 是否可随 ImageSharp 一起移除，
取决于 System.Text.Json 8.0.5 在 net48 下是否引用它们（STJ net48 依赖 Bcl.AsyncInterfaces、
System.Memory、Buffers、Numerics.Vectors、Encoding.Extensions）——**保守预期：Memory/Buffers/Numerics
仍被 STJ 需要，只能移除 ImageSharp 本体 + CodePages**。WP5.2 完成后以实测为准。

## 3. WP5.2 预算（compact 版）

- 确定可省：ImageSharp 628,416 + CodePages 527,029 = **1,155,445 B（压缩后）**
- 新增：仓库内 WebP 解码器 IL ≈ +40~70 KB（净省 ≈ 1.09 MB）
- 预期结果：5,704,192 − 1,155,445 + ~55,000 ≈ **4.60 MB**（贴 4.5 目标线，偏上）
- 达标 ≤4.5 MB 的补充手段（按序）：
  1. locales/model_rules JSON 构建期 gzip（读取侧走 GZipStream）≈ −300 KB（Core 资源读取代码需小改，CoreResources 读取属"布局相关"边界，需谨慎：**只在 CoreResources 的资源加载函数**改，不动业务逻辑）；
  2. plugin-host 脚本 gzip ≈ −90 KB；
  3. WebView2 WinForms 排除（换 ResolvePackageAssets 思路）≈ −20 KB。
- 冲刺 ≤3.8 MB：上述全部 + arm64 loader 按需裁剪需报备（砍架构 = 功能裁剪，**默认不做**）。

premium 版：不做任何裁剪（含 ImageSharp 全量），无大小约束。

## 4. 结论

- 每阶段红线检查通过（当前 5,704,192 ≤ 5,707,264）；
- WP5.2 主收益来自移除 ImageSharp 及其传递依赖，预算见 §3，完成后回填实测值。

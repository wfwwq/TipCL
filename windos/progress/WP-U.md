# WP-U 启动页贴 launch2 效果图实修 —— 完成（2026-11-07，双 flavor 编译+截图验证）

接续被中断代理的半成品（工作树已有 3 行沉浸态 GlassStrong/GlassBorder 覆盖，保留并随本包提交）。

## 逐项差异与处理
| 项 | 效果图 | 原状 | 处理 |
|---|---|---|---|
| 1 主按钮内嵌版本 | 「▶ 启动游戏 │ fabric-loader-0.20.1-26.3」 | 只有「▶ 启动游戏」 | SyncHero 把 `_ver.Str()`（版本全名，无加载器即游戏版本）上屏到按钮：细竖线分隔（1px Border，OnAccentStrong，Opacity .45）+ 13px 版本文本（Opacity .85）；无版本时竖线和文本一起收起。LaunchAsync/启动链路不变（仍按 `_ver.Str()` 启动）。 |
| 2 chip 首位 | 「Fabric 0.20.1」（加载器名+版本） | WP-K 加的裸版本 chip | 解析 `fabric-loader-<loader>-<mc>` → chip「Fabric <loader 版本>」；非 fabric（原版等）回落显示版本全名（原版无加载器名）。 |
| 3 主按钮 ▶ | play 三角 | 已有 `\ue768`（map.py 标 "r"，字体内置） | 已一致，不动。 |
| 4 联机 wifi 图标 | wifi 弧线 | 已有 `\ue701`（map.py 标 "r"，字体内置） | 已一致，不动。 |
| 5 其余（绿点就绪行/34 ExtraBold 标题+箭头/Java·内存·模组 chips/48×48 齿轮钮/底部新闻卡红标签） | — | WP-B 已落 | 截图逐项对照一致，不动。 |

注：launch2.png 上实例为 fabric 1.20.1，本机测试实例只有原版 26.3，故截图 chip/按钮显示「26.3」；fabric 前缀实例同逻辑显示「Fabric 0.20.1」chip +「fabric-loader-0.20.1-26.3」按钮文本。

## 改动
- 仅 `src/TipCL/Pages/LaunchPage.cs`：+字段 `_goSep`/`_goVer`；按钮 Content 加分隔线与版本文本；SyncHero 填充 + chip 改加载器名。约 +20 行，未新增资源键/I18n 键。MainWindow.*（WP-T2）未碰。

## 体积 / 构建 / 测试
| 项 | 结果 |
|---|---|
| compact 全量 Rebuild | 0 错误，exe **5,706,752 B** ≤ 5,707,264 红线，余量 **512 B** |
| premium（`-p:Premium=true`） | 0 错误 |
| `dotnet test -c Release` | **127/128**（唯一失败 = 既有基线 PhoneLinkTests.FullFlow） |

坑记录：premium 构建后紧接 csproj 增量构建会把 exe 顶到 5,707,264（残留 +512），`-t:Rebuild` 全量即回 5,706,752；验收以全量构建为准。

## 截图
- `_wph_run/wpu/launch-dark.png`、`launch-light.png`（compact exe `--shot launch`）：按钮「▶ 启动游戏 │ 26.3」、chip「26.3 / Java 自动 / 4 GB」、▶ 与 wifi 图标正常、绿点就绪行/标题箭头/齿轮钮/新闻卡与 launch2.png 一致。测试实例无截图背景，故为渐变底（背景逻辑 WP-G/WP-B 既有，未动）。

## 遗留
1. 体积余量仅 512 B，下一包加代码需先按 WP-A 回收（Card.Shadow / Seg.Item DropShadow，回收须动 Themes，届时解锁）。
2. chip 的加载器解析只认 fabric-loader-* 前缀；forge/neoforge 版本名格式不统一，回落显示全名（未额外分支，省体积）。

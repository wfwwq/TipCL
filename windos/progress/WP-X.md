# WP-X 安装页贴 install2 实修收尾（2026-11-07，编译+测试通过，真窗验证未做）

接续两任中断代理的半成品（git 工作树 diff +59 行），完成「安装页贴 install2」。

## 半成品审读结论
两任改动方向正确、结构完整，逐项对照 install2.html CSS 核对：
- premium 输入框下 12px 灰字路径提示 `_pathHint`（`SyncPath`：已有实例用其 `Path`，新实例用实例根目录推算 `Path.Combine(root, n)`，拿不到隐藏）——正确，`InstanceInfo.Path` 字段已确认存在（Models/InstanceInfo.cs:9）。
- 加载器卡右上勾标 margin 由 `(-2,-2)` 改 `(0,9,10,0)`——与 CSS `.ld.on:after{right:10px;top:9px}` 精确一致。
- `_name.Padding=(10,3,10,3)`——覆盖 Input 样式默认 `10,7`，配 38 高度居中，合理。
- `SepRow`：vbox 行上方 1px `B.RowLine` 分隔线，包裹的 Border 即 `_apiRow/_liteRow` 本体，折叠语义正确（折叠的是带分隔线的整行）；首行 `_verRow` 不加分隔线，与 `.vr:first-child{border-top:0}` 一致。
- 全部改动仅 `#if PREMIUM` 或无侧效，compact 路径除 Padding 外零变化。

## 本代理补完点（1 处）
- `src/TipCL/Pages/InstallPage.cs:100`：头部类型标签 `Ui.Tag(typeLabel, "B.Ok", "B.LineSoft")` → `"B.Ok", "B.AccentSoft"`。install2 `.tg{background:#e8f5ee;color:#2f8f63}` 是绿底绿字，原灰底 `B.LineSoft` 不符；`B.AccentSoft=#E7F5EE`、`B.Ok=#2E9B6B` 均为主题既有资源。

未改 LoaderIcons.cs（Tile 36×36 圆角 9 与 `.li` 一致，无需动）。

## 验证数据（2026-11-07，Windows）
| 项 | compact | premium |
|---|---|---|
| 构建（-m:1 全防锁参数） | 0 错误 | 0 错误 |
| TipCL.exe | **5,707,264 B** ≤ 5,757,264 红线（D-19 放宽后），余量 **50,000 B** | **6,002,688 B**（无上限，仅记录） |
| 测试 | **127/128**（唯一失败 PhoneLinkTests.FullFlow，基线既有，未碰） | 同一次跑出（测试工程不受 Premium 影响） |

## 遗留
- **真窗截图验证未做（监工安排）**——本代理约束禁止启动应用/开真窗口/抢前台，仅完成编译与代码审读验证。建议监工用 `--shot` 流程贴 install2.png 对照（重点：头部绿标签、勾标位置、premium 路径提示行、vbox 分隔线）。

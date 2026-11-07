# WP-Z 插件页卡片细部贴 plugins2 实修 —— 完成（2026-11-07）

## 对照 plugins2.png / plugins2.html / style2.css 的逐项差异与处理
改动文件只有 `src/TipCL/Pages/Plugins/PluginsPage.Installed.cs`（WP-D/WP-F 后插件卡已大体贴图，本次修剩余硬差异）：

| 项 | 效果图/CSS | 原状 | 处理 |
|---|---|---|---|
| logo 底色 | 每插件不同色（#7c5cff/#f59e0b/#64748b） | 全部主题色 B.AccentStrong | 新增 `LogoBrush`：按插件 id 哈希从 6 色固定调色板取色（明暗主题通用，配白字） |
| 卡片内边距 | `.pc{padding:16px 18px}` | Ui.Card 统一 16 | Padding 改 (18,16,18,16) |
| 底行版本字号 | `.r` 12px #999 | Ui.Small（11px 淡灰） | 改 Ui.Muted（12px B.InkMuted） |
| 卡片高度 | 内容自适应 | UniformGrid 单元格拉满整格高度（截到视口底） | 卡片 VerticalAlignment=Top |
| ⋯ 菜单顺序 | 语境合理 | 更新/设置/重新加载 | 调为 更新 → 设置 → 重新加载 → 数据目录/日志/权限/开发文件夹/主页 → 卸载（功能未删） |
| 已达标项 | 36×36 圆角 9 logo、名称 15 ExtraBold、开关右对齐、描述 12.5 两行高 36、3 列网格间距 14（Margin 7）、页边 22、Seg.Rail 灰轨道、从文件安装 + ⋯ | 均已符合，未动 | — |

市场卡片（PluginsPage.Market.cs）效果图未给出市场态，结构未动。Switch 40×22 vs CSS 36×20 属全局主题样式（Controls.xaml 非本包地盘），未动。

## 验证
- 构建全防锁通过；compact exe = **5,707,264 B**（红线 D-19 放宽后 ≤5,757,264，余量 50KB；多代理并行共享 bin，以构建时点为准）；premium 构建通过 = 6,002,688 B。
- `dotnet test`：**127/128**（唯一失败 PhoneLinkTests.FullFlow，与本包无关）。
- 截图（`_wph_run/wpz/seed-light.png` / `seed-dark.png`）：用 `TIPCL_HOME=C:\tipcl-wpz-home` 预置 3 个假插件清单（ai-saves / screenshot-wall / hello-plugin）让真实后端出卡，与 plugins2.png 逐项对照：布局/字号/间距/灰度一致；卡片内容自适应高度后与效果图同构。深浅两主题均正常。

## 遗留
- 首次用全新 TIPCL_HOME 截图会弹诊断同意框，挡 light 首拍（第二次起不再弹）；DevShot 不在本包范围未改。
- 插件未启用时截图显示"已禁用"标签，效果图开关全开无此标签——为真实状态渲染，非差异。
- commit hash 见 git log（WP-Z: 插件页卡片细部贴图实修）。

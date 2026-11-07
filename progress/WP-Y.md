# WP-Y 设置页分组卡片贴 settings2 实修（2026-11-07）

基线：WP-U 之后开工，WP-V 未提交 SettingsPage 改动；基于 WP-D 的设置页左导航版做增量。构建期间其它代理并行改动了 PluginsPage.Installed.cs / InstallPage.cs / LibraryPage.cs / Controls.xaml，未触碰。

## 逐项差异与处理（对照 docs/mockups-hark/v2/settings2.png + .html + style2.css）
| # | 项 | 核对结果 | 处理 |
|---|---|---|---|
| 1 | 组标题 | `T.GroupTitle` 已是 12.5 SemiBold / B.InkMuted(#888) / Margin 4,0,0,6，与 CSS `.gt` 完全一致 | 无需改 |
| 2 | 分组卡片 | Card 圆角 12（CR.Card）、内边距 12 已符；组间距走 ScrollBody 的 10px（任务口径"约 10"，CSS 实为 14，未动共享 ScrollBody 以免波及其它页） | 无需改 |
| 3 | 行内布局 | CSS `.rw`：行高 48、行间 1px #f2f2f2 分隔线通到卡片两端。原实现无分隔线、行高偏矮 | 新增 `Rows(...)` 辅助：行 MinHeight 40（+Field 上下边距≈48）、行间插 1px 分隔线（背景用 B.SegRail≈5% 墨色，明暗皆可；Margin -12 通到卡片两端）。8 张卡（外观/壁纸/主题包/下载/游戏/存储/AI/启动器）全部换成 Rows |
| 4 | 左导航 | Seg.Item 13.5 Medium→选中白底 Bold、圆角 CR.Ctl=8、条目 padding 12,8 / 间距 2 / 导航 Margin 12,16,0,12，与 `.nav/.n` 一致 | 无需改 |
| 5 | 特殊卡一致性 | 记忆卡/手机同步卡用 `Ui.Section`（标题在卡内），与普通组的"标题在卡外"不一致 | `_views[0]` 里补 `Gt("记忆")`/`Gt("手机同步")`；两张卡改 Rows，记忆卡标题行改为 Field（标题+按钮+说明）；手机同步卡删 Ui.Section |

## 精简（体积超限自救）
- 删 `_aiPerm`（ComboBox 建了、加载了但界面从不显示；ai_permission_mode 实际由 AiPermissionPanel 管，SettingsPage 保存时也不写它）。删 AiPermOpts 数组、字段、LoadAsync SelectKey、WireAutoSave 数组项。

## 构建与验证
- compact：**5,707,264 B（恰等于上限，余量 0）**；premium：6,002,688 B。均编译通过（含其它代理并行改动）。
- 测试：127/128（已知 PhoneLinkTests.FullFlow 失败，与基线一致）。
- 截图 `_wph_run/wpy/settings-dark.png`、`settings-light.png`：行分隔线出现且通到卡片两端、行高≈48、组标题灰字在卡外、记忆/手机同步卡与普通组一致，与 settings2.png 对齐良好。

## 遗留
- 组间距 10 vs CSS 14：如要精确需动 PageBase.ScrollBody（共享，未动）。
- 卡内边距 12 vs mockup 行内边距 18：差 6px，属极细差，为省体积未改。
- compact 体积余量为 0，后续任何代理加代码都会超限，需有人再精简。

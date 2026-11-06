# TipCL 设计规范（design-spec）v1.0

> 本文件是 ui-rebuild 分支全部视觉决策的**唯一真相**。WP2-WP5 的代码改动必须能逐条对应到本文。
> 依据 GOAL §5-WP1 产出；裁决条目见文末 §10。所有数值可直接翻译成代码常量。

## 1. 间距栅格

4px 基准。**合法值集合：`{4, 6, 8, 10, 12, 16, 20, 24}`**。
（6 为半档，仅供图标-文字组合等紧凑场景；栅格外数值一律收敛。）

| 用途 | 值 | 落点 |
|---|---|---|
| 页面外边距 | `18,14,18,16`（左,上,右,下） | `PageBase.Body/ScrollBody` |
| 页面纵向间距（卡片间） | `10` | `PageBase.Body/ScrollBody` 的 SPanel spacing |
| 卡片内边距 | `12` | `Ui.Card` 默认 padding、`Card` 样式 Padding |
| 卡片二级内边距（Flat/嵌套 Pane） | `10` | `CardFlat`、`Ui.Pane` 默认 |
| RowCard 内边距 | `10` | `Ui.RowCard` |
| Field 上下外距 | `0,4,0,4` | `Ui.Field` |
| Field 标签与控件列间距 | `12` | `Ui.Field` label margin-right |
| 控件组间距（H/V 默认小间距） | `6` / `8` | 各页面局部 |
| 标题与内容间距 | `8` | `Ui.Section`、样张 |
| 按钮内边距（常规） | `14,6` | `Btn.Base` Padding |
| 按钮内边距（Chip/小） | `10,4` | `Btn.Chip` |
| 输入框内边距 | `10,6` | `Input`/`Pw` Padding，MinHeight 30 |

## 2. 字号阶梯

**合法集合：`{11, 12, 13, 15, 17, 20}`。全界面禁止出现 >20 的字号**（横幅 27px 大标题在 WP4 移除）。

| 令牌 | 字号 | 字重 | 用途 |
|---|---|---|---|
| `T.Small` | 11 | Regular | 小注、脚注、时间戳、徽章内文 |
| `T.Muted` / 12 | 12 | Regular | 辅助说明、hint、次级信息 |
| 正文（默认） | 13 | Regular | 正文、按钮、输入框、列表行 |
| `T.H3` | 15 | SemiBold | 行内小标题（原 14 归入 15） |
| `T.H2` | 17 | SemiBold | 卡片标题、Section 标题（保持 17） |
| `T.H1` | 20 | SemiBold | 页面标题（GOAL WP2.5：24→20） |

> 说明：GOAL §5-WP1 阶梯 {11,12,13,15,17,20} 的对应关系以 GOAL WP2 明文为准
> （T.H1 24→20；H2 保持 17；H3 14→15）。初版 spec 曾把 H1 定 17/H2 定 15，已按 GOAL 原文纠正（见 D-9）。

## 3. 圆角三档

| 令牌 | 值 | 用途 |
|---|---|---|
| `CR.Ctl` | 6 | 一切输入控件：按钮、输入框、下拉、复选框、菜单项 |
| `CR.Small` | 8 | 小卡、RowCard、Tag（普通标签）、导航选中底 |
| `CR.Card` | 10 | 卡片、Pane、弹层容器（下拉 Popup、ContextMenu、Tooltip 用 8） |
| `CR.Pill` | 999 | **仅限** Badge、BridgePill 等胶囊徽章/标签 |

全库收敛规则：`12→10`（Card）、`14→10`（弹层/大圆角容器）、`9→8`（行卡）、
`7→6`、`5→6`（小 Tag 统一 6 或徽章 999）。

## 4. 色板规则

明暗两套必须**令牌一一配对**（GOAL 红线 §3.9）：一个文件有的键另一个必须有。
每族色三档：**base（实色）/ soft（柔底）/ border（描边）**；文字四档 Ink 四阶；强调七档。

| 族 | base | soft | border |
|---|---|---|---|
| 强调（绿） | `B.Accent` | `B.AccentSoft` / `B.AccentSoft2` | `B.AccentBorder`（新增，成对） |
| 危险 | `B.Danger` | `B.DangerSoft` | `B.DangerBorder`（新增，成对） |
| 警告 | `B.Warn` | `B.WarnSoft` | `B.WarnBorder`（新增，成对） |
| 信息 | `B.Info` | `B.InfoSoft` | `B.InfoBorder`（新增，成对） |
| 中性 | `B.Paper` / `B.Paper2` | `B.Hover` / `B.LineSoft` | `B.Line` |

- 主色沿用现有绿系（dark `#3FB07C`、light `#2E9B6B`），不改色相；
- **渐变仅保留 `B.LogoFill`（Logo 一处）**；`B.BannerFill` 在 WP4 移除使用后从两套色板成对删除；
- 对比度（WCAG）：正文 `B.Ink` ≥ 4.5:1，辅助 `B.InkMuted/InkSoft` ≥ 3:1，小注 `B.InkFaint` ≥ 3:1。

### 4.1 本轮实测对比度（对 B.Paper / B.Paper2）

| 令牌 | Dark（#1E1E1E 底） | Light（#FFFFFF 底） | 判定 |
|---|---|---|---|
| `B.Ink` #EDEDED / #262626 | 14.0:1 | 15.5:1 | ✓ 正文 |
| `B.InkSoft` #C0C0C0 / #565656 | 9.2:1 | 7.5:1 | ✓ |
| `B.InkMuted` #909090 / #888888 | 5.2:1 | 3.6:1 | ✓ 辅助 ≥3 |
| `B.InkFaint` #707070 / #B0B0B0 | 3.3:1 | **2.2:1 ✗** | Light 调至 `#8A8A8A`（3.4:1） |

## 5. 组件解剖

| 组件 | padding | 内部间距 | 字号 | 圆角 | 备注 |
|---|---|---|---|---|---|
| `Card`（样式） | 12 | — | — | 10 | B.Paper 底 + B.Line 1px 描边 |
| `CardFlat` | 10 | — | — | 10 | B.Paper2 底 |
| `Ui.Pane` | 10 | — | — | 8 | 无描边柔底容器 |
| `Ui.Field` | 行距 `0,4` | 标签列距 12；标签-hint 纵距 2 | 13 / hint 12 | — | labelWidth 默认 140 |
| `Ui.Section` | — | 标题-副题纵距 2 | 标题 15 / 副题 12 | — | 副题可省略 |
| `Ui.RowCard` | 10 | — | 行内 13 | 8 | B.Paper + 1px B.Line，可点击 |
| `Btn.*` | `14,6`（Chip `10,4`，Icon 7） | 图文距 6 | 13（Chip 12） | 6 | Primary/Soft/Normal/Ghost/Danger/OnBanner |
| `Input`/`Pw` | `10,6` | — | 13 | 6 | MinHeight 30 |
| `Combo` | `10,4` | 弹层内 `0,5`，项 `10,6` | 13 | 6（弹层 8） | |
| `Chk`/`Radio` | 盒 18×18 | 图文距 8 | 13 | 5（盒） | |
| `Switch` | 40×22 | 滑块 16 | — | 11 | |
| `Tag` | `6,1,6,2` | — | 11 | 8 | 普通标签 |
| `Badge` | `6,0,6,1` | — | 11 SemiBold | 999 | 胶囊仅此 |
| 导航项 | 行高 32，外距 `8,1` | 图文距 10 | 13 | 8 | 分组头 11 号 |
| 标题栏 | 40px 高 | — | 13.5 / 12.5 | — | 侧边栏默认 196（可拖 140-320） |
| Empty 空态 | 上下外距 32 | 图文距 6/4 | 图标 28 / 标题 13 / hint 12 | — | 原 46 外距、34 图标收敛 |
| Skeleton | — | — | — | 6 | 高度默认 14 |

## 6. 设计样张页（WP1 交付物之二）

新增 `src/TipCL/Pages/DesignSheetPage.cs`：开发专用页，仅通过 `--design-sheet` CLI 参数可达
（不注册进默认导航，不影响用户）。一页内渲染：间距标尺、字号阶梯、圆角三档、
色板全套（明暗当前主题）、按钮全家、输入控件、Tag/Badge、Card/RowCard/Field/Section/Empty。
截图：`docs/ui-audit/sheet/sheet-light.png`、`sheet-dark.png`。

## 7. 窗口默认尺寸（WP4 实测后回填）

- PCL 参考物：`D:\项目\tipcl\windos\_pcl_ref\Plain Craft Launcher 2.exe`（已解压）。
- 实测方法与结果：**见 §9 回填**（WP4 执行时以动态 GetWindowRect/GetClientRect 实测为准）。
- `MainWindow.xaml` 的 `Width/Height/MinWidth/MinHeight` 以实测值改写，来源记录于此节。

## 8. 令牌→代码映射（WP2 落点速查）

| spec 条目 | 代码位置 |
|---|---|
| 间距栅格 | `Pages/PageBase.cs` Body/ScrollBody、`Services/Ui.cs` Card/Field/RowCard/Section/Empty |
| 字号阶梯 | `Themes/Controls.xaml` T.H1/T.H2/T.H3、`Ui.cs` 默认 size |
| 圆角三档 | `Controls.xaml` CR.Card/CR.Ctl（新增 CR.Small）、`Ui.cs` 各默认 radius |
| 色板三档 | `Themes/Palette.Dark.xaml` + `Palette.Light.xaml`（成对） |
| 渐变仅 Logo | `Controls.xaml` LogoFill 保留；BannerFill 使用点在 WP4 拆除 |

## 9. PCL 尺寸实测记录（WP4 回填）

（待 WP4 执行时回填：方法、原始读数、采用的 Width/Height/MinWidth/MinHeight。）

## 10. 裁决条目（执行中的 spec 追加）

1. **GOAL 说导航 14 项，实际 `NavDefs` 注册 20 项**（launch/instance/version/mod/mods/modpack/datapack/resourcepack/shader/world/account/java/ai/multiplayer/servers/playtime/feedback/settings/tasks/plugins）。已按实际 20 项做分组收纳，目标不变：顶级 6-7 个。
2. **Light `B.InkFaint` #B0B0B0 对比度 2.2:1 不达标**，调至 #8A8A8A（3.4:1），Dark 不动。属色板成对修改的合规范围内（暗色该档本来达标）。
3. **基线状态**：main @ 7c7014a 构建 0 错误；测试 123/124 过，`PhoneLinkTests.FullFlow` 为**基线既有失败**（与 UI 无关，不属本任务修复范围，不碰 Core/）；exe 5,698,048 B ≤ 5,707,264 B 红线。
4. **横幅兼容策略**：`banner` 卡片类型保留（老用户存档可继续渲染），仅 27px 标题收敛到 ≤20；默认布局（`DashLayout.Default()` + `UiLayout.DefaultDoc()` 同步）在 WP4 换为紧凑启动头 + 3 卡。
5. **字号阶梯映射**：T.H3 14→15、T.H2 17→15、H1 24→17，新增 20 仅用于紧凑启动头。GOAL 允许（阶梯集合 {11,12,13,15,17,20} 本身就是 spec 的一部分）。

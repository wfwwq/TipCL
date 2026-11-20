# WP-L0 令牌 · 施工报告（R34 布局 v3）

- 包：WP-L0（令牌、矢量图标、截图开关）
- 分支 / 工作区：`r34/l0` · `D:\项目\TipCL\_r34\l0`（起点 `8cad7d0`）
- 提交：`b609ef1`（代码 + 单测 + design-spec），本报告单独一次提交。
- 接替说明：本包第一次派出的子代理在 17:35 掉线时尚未动工（工作区干净、分支停在 `8cad7d0`），本报告全部工作由第二次派出的接替者完成。
- 硬性约束执行情况：**没有启动 TipCL.exe**（任何参数都没有），没有做界面自动化；只跑了 `dotnet build` / `dotnet test` / git（add、commit、diff、log、status、show）和文件读写；所有 dotnet 命令都带 `-nodeReuse:false`，没有跑 `dotnet build-server shutdown`。

## 1. 做了什么

### 1.1 色板（两个 Palette 文件成对）
新增 20 个画刷 + 4 个阴影，两边键集合完全一致（单测守住）：

- §6.4 点名的 9 个：`B.EditOutline`、`B.EditOutlineHover`、`B.EditOutlineOnDark`、`B.EditDropFill`、`B.EditColShade`、`B.EditBarBg`、`B.EditDim`、`B.WarnSoft`（**已有键，沿用**，浅色值正好等于效果图琥珀浅底 `#FDF3E2`）、`B.WarnInk`。
- 样式需要的补充键：`B.EditOutlineHoverOnDark`、`B.EditDropFillOnDark`、`B.EditColShadeOnDark`、`B.EditBarLine`、`B.EditItemBg`、`B.EditSegRail`、`B.EditSegOn`、`B.EditGlass`、`B.EditGlassLine`、`B.EditGlassChip`、`B.EditGlassChipLine`、`B.OnEditGlass`、`B.OnEditGlassMuted`。
- 阴影：`Fx.EditBar`、`Fx.EditLabel`、`Fx.Drawer`、`Fx.SegOn`（`DropShadowEffect`，深色主题不透明度更高）。
- 取值表见 `windos/docs/design-spec.md` §12.1 / §12.2。

### 1.2 矢量图标（Controls.xaml，16×16 视框，只描边）
`Geo.Eye` / `Geo.EyeOff` / `Geo.Lock` / `Geo.Undo` / `Geo.Redo` / `Geo.Grip`，形状照 layout3.html 的 SVG 换算成只含绝对坐标 M/L/C/A/Z 的 WPF 路径（`S` 平滑曲线展开成 `C`）。显示用 Path 样式 `Layout.Glyph`（14px、有效线宽 1.4、Stroke 跟随按钮前景色）、`Layout.Glyph.Small`（12px）、`Layout.Grip` / `.OnDark`（六点，12px）。

### 1.3 样式（Controls.xaml）
- §6.4 点名的 5 个：`Layout.EditBar`（Border，高 46、圆角 12、细描边 + 轻阴影）、`Layout.EditLabel`（Border，高 24、圆角 8）、`Layout.IconAct`（ButtonBase，26×24、圆角 8、悬停变色）+ `Layout.IconAct.Danger`（悬停 `#C42B1C`）、`Layout.Drawer`（Border，宽 316、左侧阴影）、`Layout.SegItem`（ToggleButton/RadioButton，选中白底 + 粗体）。
- §6.3 验收点名的 `Layout.EditOutline`（Rectangle：1.5 虚线、外扩 4；`Tag` = `hover` / `selected` / `drag` 切状态）。
- 深色玻璃版本：`Layout.EditOutline.OnDark`、`Layout.EditLabel.OnDark`、`Layout.EditLabelNote.OnDark`、`Layout.IconAct.OnDark`、`Layout.IconAct.Danger.OnDark`、`Layout.EditBar.OnDark`、`Layout.EditHint.OnDark`、`Layout.EditBarSep.OnDark`、`Layout.DropBox.OnDark`、`Layout.ColShade.OnDark`、`Layout.Grip.OnDark`。
- 辅助：`Layout.EditLabelNote`、`Layout.EditBarTitle`、`Layout.EditHint`、`Layout.EditBarSep`、`Layout.Count`、`Layout.SegRail`、`Layout.ResizeGrip` / `.Bottom`（Thumb）、`Layout.EditTip`、`Layout.DropBox`、`Layout.ColShade`、`Layout.DrawerTitle` / `DrawerSection` / `DrawerItem` / `DrawerIcon` / `DrawerEmpty` / `DrawerNote`、`Layout.WarnNote`、`Layout.LockNote`。
- 圆角只用 8 和 12（单测扫描全部 `Layout.*` 样式）；所有资源引用都能在 Controls.xaml 或两个 Palette 里解析到（单测）。

### 1.4 DevShot.cs（只写代码，**没有运行**）
- `--edit`：截图前找到目标页的 `BlockHost`（可视树 + 逻辑树）并 `EnterEdit()`。匹配规则：宿主 id 完全一致优先，其次「导航 id:」前缀（`library` → `library:main`）。
- `--select <blockId>`：隐含 `--edit`。先走 `DevShot.SelectHook`；没接线时用反射调 `LayoutEditor` 现有的 `_session.Select` + `UpdateSelectionVisual`（r34/base 里已有的成员），失败只打印不抛异常。
- `--pop layout-drawer`：隐含 `--edit`。先走 `DevShot.LayoutDrawerHook`；没接线时按 `OpenAddDrawer` / `ShowAddDrawer` / `OpenDrawer` 找无参方法；再找不到就退回 R34 之前底部托盘的「+ 自建区块」展开态并打印提示。启动页原有的 `--pop config|instances|news` 不受影响。
- `--expand advanced`：先走 `DevShot.ExpandHook`；没接线时按标题文字找（`I18n.L("分栏、网页与按钮组件、布局文件")`，其次 `I18n.L("高级")`）：Expander 置 `IsExpanded`，ToggleButton 置 `IsChecked`，普通按钮发 `Click`，然后 `BringIntoView`。
- `--shot settings:<子页>`：`general / account / java / network / servers / playtime / about / layout` 映射到设置页左导航视图号（导航前设好现有的 `TIPCL_DEV_SETTINGS_VIEW`）。`java` / `servers` / `playtime` / `feedback` 不是顶栏页，直接 `--shot java` 也会落到设置子视图（以前这几个 id 导航不到，会拍成启动页）。
- `--design-sheet`：样张页末尾追加「布局 v3 编辑态」一节（编辑条、块四种状态 + 手柄 + 「N / 列数」、落点框 + 列底纹、抽屉、六个图标、深色玻璃版、琥珀提示）。样张页本体在 `Pages/DesignSheetPage.cs`（不归本包），所以在 DevShot 里注入；`--no-v3-sheet` 关掉。
- 顺带修了一个小问题：单独写 `--design-sheet`（后面紧跟 `--out` 或没有值）时，旧实现会把下一个参数当页 id，拍成启动页；现在就是样张页。
- 参数解析抽成纯函数 `DevShotArgs.Parse`（不碰任何 WPF 类型），有单测。

### 1.5 design-spec.md
只在文末追加 §12「布局 v3 编辑态」：全部新键、明暗取值、用途、阴影参数、图标、样式数值、截图开关；没有改前面的章节。

### 1.6 单测（新建 `windos/tests/TipCL.Tests/LayoutV3ResourceTests.cs`，53 个用例）
- `LayoutV3ResourceTests`：两个 Palette 键集合与元素类型一致；§6.4 画刷齐全；补充键齐全；只用在深色背景上的令牌明暗相同；浅色值对得上效果图；6 个 Geometry 存在、只用 M/L/C/A/Z 且坐标在 0–16；26 个样式存在且 TargetType 正确；关键数值（46 / 12 / 24 / 8 / 26×24 / 316 / 1.5 / −4）；选中框是 2px 主题色实线；删除钮悬停是 `B.CaptionHot` 且两套色板都等于 `#C42B1C`；`Layout.*` 只用圆角 8 / 12；`Layout.*` / `Geo.*` 的资源引用全部可解析且成对。
- `DevShotArgsTests`：`--edit` / `--select` / `--pop layout-drawer` 的隐含关系；启动页浮层不算抽屉；`settings:layout --expand advanced`；8 个设置子页别名的视图号与宿主 id；顶栏页不被改道；`library` 前缀匹配；`--design-sheet` 单独出现；缺值时为 null。

## 2. 文件清单（相对仓库根）
1. `windos/src/TipCL/Themes/Palette.Light.xaml`
2. `windos/src/TipCL/Themes/Palette.Dark.xaml`
3. `windos/src/TipCL/Themes/Controls.xaml`
4. `windos/src/TipCL/DevShot.cs`
5. `windos/docs/design-spec.md`（只追加 §12）
6. `windos/tests/TipCL.Tests/LayoutV3ResourceTests.cs`（新建）
7. `windos/progress/WP-L0.md`（本报告）

编码 / 换行：全部保持原样（UTF-8 无 BOM、LF；DevShot.cs 和测试文件用 Tab 缩进，XAML 两空格）。

## 3. 构建、测试、体积

| 项 | 基线（续跑须知，WP-L3 实测，`8cad7d0`） | 收尾（本包） |
|---|---|---|
| compact 构建 | 0 错误；8 个警告（6×NU1900 NuGet 缓存目录权限 + 2×xUnit1031 `BridgeMutationAndLaunchGuardTests.cs`） | **0 错误；警告集合与基线相同**（6×NU1900 + 2×xUnit1031，没有新的 CS / XAML / xUnit 警告；MSBuild 汇总行写 14 是同一批警告的重复计数） |
| premium 构建（`-p:Premium=true`） | — | **0 错误；警告集合同上** |
| 全量测试（`dotnet test TipCL.sln -c Release --no-build -nodeReuse:false`） | 通过 593 / 失败 0 / 跳过 0 | **通过 646 / 失败 0 / 跳过 0**（+53 = 本包新增用例） |
| compact 体积 | 5,599,232 B | **5,634,048 B**（+34,816 B；上限 5,757,264 B，余 123,216 B） |
| premium 体积 | — | 5,654,016 B |

体积增量主要来自 Controls.xaml 的 BAML（`obj/.../Themes/Controls.baml` 现为 34,266 B）和 DevShot 的样张代码。

编译环境照续跑须知：SDK `D:\项目\TipCL\_tools\dotnet`（8.0.428），`DOTNET_CLI_HOME` / `TEMP` 指到工作区 `scratch\cli-home` / `.testtmp`，日志在 `windos\scratch\`（已被 .gitignore 忽略）。

## 4. 词表（不改词表文件，合并时由主 agent 合入）

离线检查：从本包改过的 `.cs`（`DevShot.cs`、`LayoutV3ResourceTests.cs`）提取 `I18n.L("…")` 字面量，和 `zh_CN.json` / `en.json` 比对。

| 中文 key | 建议英文 | 状态 | 说明 |
|---|---|---|---|
| 分栏、网页与按钮组件、布局文件 | Split panes, web & button widgets, layout file | 新增 | GOAL §5.4 规定的「高级」卡片标题，WP-L4 也会用到同一个 key；DevShot 用它找折叠区 |
| 高级 | Advanced | 新增 | 「高级」组标题，同上 |

废弃 key：无（DevShot.cs 原来没有 `I18n.L` 字面量）。样张页里的演示文字和 DesignSheetPage 一样不走词表（dev-only）。

## 5. 合并时需要主 agent 处理的事
1. **主题色跟随**：`B.EditDropFill` 是默认绿的固定值。用户自定义主题色时 `SettingsPage.Themes.cs` 的 `ApplyAccent` 只重算 `B.Accent*`；建议在其中补 `d["B.EditDropFill"] = new SolidColorBrush(color5);`（与 `B.AccentSoft` 同色），并把 `"B.EditDropFill"` 加进 `SettingsPage.cs` 的 `AccentKeys`。不补的后果：换了主题色后，拖动落点框的浅底仍是绿色（描边是 `B.Accent`，会跟着变）。
2. **DevShot 钩子接线**（三选一接到正式入口，接好后删掉对应的反射兜底）：
   - `DevShot.SelectHook = (h, id) => { h.Editor.<WP-L12 的选中入口>(id); return true; };`
   - `DevShot.LayoutDrawerHook = h => { h.Editor.<WP-L12 打开抽屉的入口>(); return true; };`——抽屉必须是窗口内元素，不能是 `Popup`，否则 `RenderTargetBitmap` 拍不到。
   - `DevShot.ExpandHook`：WP-L4 的「高级」若用 Expander 或标题文字正是「分栏、网页与按钮组件、布局文件」，可以不接。
   接线位置建议放在 `DevShot.Run` 开头或 App 启动的 dev 分支里，避免正式启动路径多一行代码。
3. **样张页**：可以把 `DevShot.LayoutV3Sheet()` 挂进 `Pages/DesignSheetPage.cs` 的 `Sheet()`（加一行 `Card("布局 v3 编辑态", DevShot.LayoutV3Sheet())`），再删掉 DevShot 里的 `InjectLayoutV3Sheet` 注入。
4. **WP-L12 用键提醒**：块状态推荐用 `Layout.EditOutline` 的 `Tag`（`""` / `hover` / `selected` / `drag`）切换；深色启动页统一换成 `.OnDark` 变体；块按钮的图标用 `new Path { Style = Layout.Glyph, Data = Geo.EyeOff }`，删除用 `Ico.Trash` 字形配 `Layout.IconAct.Danger`。
5. **可能冲突的地方**：`DevShot.cs`、`Controls.xaml`、两个 Palette 只归本包，其他包不该碰；若别的包也往 Controls.xaml 末尾追加了样式，合并时是同一位置的追加冲突，两段都保留即可。`design-spec.md` 只在文末追加。
6. 词表两条 key 按第 4 节合入，并重新打包 `.json.gz`。

## 6. 决定（合并时编号 D-82 起）
1. 圆角收敛：效果图 `.sg2 button` 的 6 取 8；浏览器里外扩 4 的 outline 圆角是 16，这里取 12。理由：WP-L0 清单要求「圆角只用 8 和 12」，与 design-spec §3 三档一致。
2. 灰色文字一律用 `B.InkMuted`，不照抄效果图的 `#8c8c8c`（白底 3.4:1，不满足 design-spec §4 的 4.5:1）。深色抽屉项底取 `#313131`（`#333333` 上 InkMuted 只有 4.49:1）。
3. 删除钮悬停红沿用窗口关闭钮的 `B.CaptionHot` / `B.OnCaptionHot`，不新开键。
4. 阴影做成色板里成对的 `DropShadowEffect` 资源（`Fx.*`），明暗主题各自的不透明度；按钮 / 分段 / 手柄的阴影放在模板底层 Border 上，内容不受 Effect 影响。
5. 启动页「去饱和」用 `B.EditDim` 遮罩近似（WPF 没有去饱和滤镜，且不加着色器资源）。
6. `Geo.Undo` / `Geo.Redo` 比效果图整体上移 1（视觉居中）；`Geo.Grip` 用 0.1 长线段 + 圆头线帽画点，保持「只描边」。
7. DevShot：`java` / `servers` / `playtime` / `feedback` 作为设置子视图别名；单独的 `--design-sheet` 就是样张页；选中 / 抽屉 / 展开用「钩子 + 反射兜底」，不在编译期引用别的包新增成员。
8. 样张演示文字不走词表（与 DesignSheetPage 一致，dev-only）。

## 7. 待真机（需要用户在场时启动 TipCL.exe；本轮**一次都没有运行**）
在 `D:\项目\TipCL\_r34\base\windos`（合并后）执行，exe 路径 `src\TipCL\bin\Release\net48\TipCL.exe`，建议照「本手册的截图怎么来的」那套隔离 `TIPCL_HOME` / `TEMP`：

1. 设计样张明暗各一张（§6.3 WP-L0 验收）：
   - `TipCL.exe --design-sheet --theme light --size 1000x3600 --out docs\ui-audit\sheet\layout-v3-light.png`
   - `TipCL.exe --design-sheet --theme dark --size 1000x3600 --out docs\ui-audit\sheet\layout-v3-dark.png`
   看点：新一节在样张最后；虚线框 / 选中实线 / 标签 / 图标按钮 / 手柄 / 落点框 / 抽屉 / 六个图标 / 深色玻璃都能渲染；stderr 没有「找不到」类提示。
2. 编辑态开关：
   - `TipCL.exe --shot download --edit --theme light --out shot-download-edit.png`
   - `TipCL.exe --shot download --select list --theme light --out shot-download-select.png`（看标签和右 / 下边缘手柄；`list` 换成下载页实际的块 id）
   - `TipCL.exe --shot download --pop layout-drawer --theme light --out shot-download-drawer.png`（WP-L12 合并接线后应是右侧抽屉；没接线时会看到旧托盘展开态并在 stderr 提示）
   - `TipCL.exe --shot settings:layout --expand advanced --size 1000x3100 --theme light --out shot-settings-layout-advanced.png`
   - `TipCL.exe --shot java --edit --out shot-java-edit.png`、`--shot playtime --edit`、`--shot library --edit`（验证设置子视图别名和 `library:main` 前缀匹配）
   - `TipCL.exe --shot launch --edit --theme dark --out shot-launch-edit-dark.png`（深色玻璃配色需 WP-L12 用上 `.OnDark` 样式）
3. 主题切换：在设置里换主题色后进编辑态拖一次，看落点框浅底是否跟随（取决于合并事项 1 有没有补）。
4. 视觉细节：14px 图标线宽是否清晰（LayoutTransform 缩放后的 1.4px）、六点拖拽的点在 100% / 150% 缩放下是否是圆点、抽屉与编辑条阴影下的文字是否可接受（灰度抗锯齿）。

## 8. 过程备注
- 提交前本机工具掉线约 3 分钟（`SNCS outpost invoke error (400)`，设备 OFFLINE）；恢复后确认工作区文件与已编译、已测试的状态逐字节一致（文件大小核对），也没有残留的 `index.lock`，再重新 `git add` / `commit`。掉线期间没有任何写操作落盘。

## 9. 风险
1. `--select` / `--pop layout-drawer` 的兜底依赖 `LayoutEditor` 的私有成员名（`_session`、`UpdateSelectionVisual`、`_trayAddOpen`、`RefreshTray`）。WP-L12 重写编辑器后很可能改掉，届时兜底只会在 stderr 打印失败，截图里看不到选中 / 抽屉效果——需要按合并事项 2 接钩子。
2. `--expand advanced` 靠标题文字找元素；WP-L4 若改了标题措辞或用了自绘控件且不是按钮 / Expander，需要接 `ExpandHook`。
3. `B.EditDropFill` 不跟随自定义主题色（合并事项 1）。
4. `Layout.EditBar` / `EditLabel` / `Drawer` 的阴影 Effect 挂在 Border 本身上，里面的文字会从 ClearType 退成灰度抗锯齿（与现有 ContextMenu / ToolTip 相同）。若真机上觉得发虚，可改成模板里单独的阴影层（需要把这几个样式的目标类型换成 ContentControl，属于接口变更，要和 WP-L12 一起改）。
5. 体积：compact +34,816 B，五个包共用剩余约 150 KB 的预算，本包占了约四分之一；合并后需要复测总量。
6. 深色主题取值（效果图只画了浅色）是按现有色板层级推导的，未经真机目测。
7. 全部截图类验收都没有实测（硬性规则禁止运行 TipCL.exe）。

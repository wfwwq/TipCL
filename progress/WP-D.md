# WP-D 插件页卡片化 + 设置页左导航 —— 完成待验（2026-11-06，未编译，未 commit）

## 改动文件
| 文件 | 内容 |
|---|---|
| `src/TipCL/Pages/Plugins/PluginsPage.cs` | 顶部改为 `Seg.Rail` 灰轨道 + 两个 `Seg.Item`（已安装 / 市场）；右侧“从文件安装”(Btn Normal) + ⋯ 菜单（刷新 / 开发者 / 插件设置）。删 `_tDev`、`_tabBar`、TabItemBtn 页签条与底线。“插件设置”= `ShowSubView` 内嵌 `PluginSettingsCard.Build()`（市场地址、未审核、模型供应商，从设置页搬来）。`_installedHost` 改 `UniformGrid`(3 列，空时 1 列)，外包 `Ui.Scroll`。Tab.Dev 保留（经 ⋯ 进入，此时分段两项都不亮）。 |
| `src/TipCL/Pages/Plugins/PluginsPage.Installed.cs` | 删 `InstalledHeader()`（Section+“去插件市场”）。`InstalledCard` 重写为 v2 卡片：36×36 圆角 9 首字母方块（B.AccentStrong 底 / B.OnAccentStrong 字）、名称 15 ExtraBold、开关；描述 12.5 两行(高 36)；底行 版本(开发插件显示“开发链接”) + 标签（状态[运行中不显示]/开发/本地/.NET/有更新）+ ⋯。更新 / 设置 / 重新加载 收进 ⋯ 菜单（原有的数据目录、日志、权限、开发文件夹、主页、卸载保留）。卡片 `Ui.Card(…,16)`（Card 样式圆角 12），Margin 7（网格间距 14）。业务调用（plugins_set_enabled / reload / install / uninstall、进度条 Track）原样保留。 |
| `src/TipCL/Pages/SettingsPage.cs` | 删 `BuildMoreLinks`/`MoreLink`（顶部“更多页面”横排）及旧的整页 ScrollBody。新左导航（168 宽）：通用 / 账号 / Java / 下载与网络 / 服务器 / 游戏时长 / 反馈与关于，按钮用 `Seg.Item`（选中 B.SegOn 白底+Bold，圆角 CR.Ctl=8），内容是固定宽 120 的 TextBlock 以实现左对齐（模板里 ContentPresenter 写死居中）；导航底部放自动保存状态 `_saveState`。右侧 `ContentControl`：<br>· 通用：标题 T.PageTitle + 分组（T.GroupTitle 在卡片外）外观 / 壁纸 / 主题包 / 启动器 / 游戏 / 存储 / AI 助手 + 记忆卡 + 手机同步卡；壁纸、主题包、AI 卡内的 Ui.Section 标题改成 Ui.Small 说明行。<br>· 下载与网络：border4。<br>· 账号/Java/服务器/游戏时长：本页自持的 `AccountPage/JavaPage/ServersPage/PlaytimePage` 实例（懒建），切换时 EnsureLoadedAsync+OnShown，离开调 OnHidden；SettingsPage 新 override `OnShown/OnHidden/OnEvent` 把生命周期与桥事件转给当前子页。<br>· 反馈与关于：上方 关于卡 +“检查更新”按钮，下方嵌 `FeedbackPage` 实例。<br>· `PluginSettingsCard` 从设置页移除（设置不含插件），移到插件页 ⋯ → 插件设置。 |

未动：Themes/*、MainWindow.*、其他页面。未新增主题键；用到的键：Seg.Rail、Seg.Item、T.PageTitle、T.GroupTitle、Card、B.AccentStrong、B.OnAccentStrong、B.Warn、B.WarnSoft、B.Danger（均已 grep 确认存在，明暗成对）。

## 未完成
- 插件卡片 logo 一律用名称首字母 + 主题色；未按插件区分颜色，也不再显示插件自带图标（PluginIcon.Make 仍被别处使用，未删）。
- 卡片网格固定 3 列，窄窗口不自适应（UniformGrid）。
- 在设置内嵌账号页切换账号后，标题栏账号胶囊不会立即刷新（`SyncAccountChipAsync` 只在离开 MainWindow 的 account 页时触发；MainWindow 不在本包范围）。
- MainWindow 的 `SyncTabs`：通过账号钮进 `account` 页时“设置”页签不亮（WP-A 已记）。

## 风险
1. 子页双实例：设置页内嵌的 Account/Java/Servers/Playtime/Feedback 与 MainWindow `_pages` 里的同名页是不同实例（为避免同一控件两个父级）。事件由 SettingsPage.OnEvent 只转发给当前显示的子页；两实例各自加载数据，状态不共享。已粗查构造函数无静态事件订阅。
2. `FeedbackPage` 在 SettingsPage 构造时就实例化（字段初始化），只在选中“反馈与关于”时才 Load。
3. `Select` 中 switch 表达式各分支为 `(PageBase)(...)` / FeedbackPage / null，自然类型为 PageBase。
4. ToggleButton / UniformGrid 在 SettingsPage.cs、PluginsPage.Installed.cs 用全限定名，避免引入 Primitives 命名空间造成 `Track` 等重名歧义（PluginsPage.cs 原本已 using Primitives）。
5. 嵌入页自带 ScrollBody 与标题，和设置页右侧标题风格可能不完全一致（视觉待 Windows 实测）。
6. 体积：净增约 +100 行 C#（主要是设置导航与生命周期转发），删掉了 InstalledHeader、MoreLinks、旧行卡片操作区。exe 余量约 3 KB，**需实测**；超了可先删 `PTitle`（改用 Ui.Section）或砍插件卡片标签。

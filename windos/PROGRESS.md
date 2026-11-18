# PROGRESS（工作包总表）

每个工作包的详细记录在 `progress/WP-X.md`（子代理各写各的，避免并发冲突）。主 agent 汇总到这里并负责 git 提交。

| 包 | 内容 | 主要文件（独占） | 状态 |
|---|---|---|---|
| WP-A | 窗口外壳：Win10 圆角(SetWindowRgn)、矢量窗口按钮、标题栏（5 页签含插件、AI 图标、头像、C 桥仅断开显示）、全局圆角/卡片样式 token | MainWindow.xaml(.cs)、Controls.xaml 及主题资源字典、Services/Backdrop.cs | 完成待验（未编译）。含 download 接线。资源键清单见 progress/WP-A.md。遗留：启动页背景铺到标题栏下；设置子页时“设置”页签不亮；Win10 Region 圆角锯齿/缩放闪烁需实测 |
| WP-B | 启动页 v2：左对齐、截图背景+渐变、去 HeroGrid/光晕/胶囊、底部新闻卡 | Pages/LaunchPage.cs | 完成待验（未编译）。详见 progress/WP-B.md；LaunchBackground.cs 已并入 LaunchPage.LoadShot。遗留：标题栏延伸到截图上→WP-A；B.HeroGrid/HeroGlow 键可删→WP-F；删抽屉时保留 multiplayer NavDef |
| WP-C | 下载页 v2 + 新安装页（加载器卡片、版本下拉、Fabric API、OptiFine、底部汇总）+ 加载器官方图标运行时下载缓存 | Pages/Download*、新建安装页文件、新建图标缓存服务 | 完成待验（未编译）。新建 DownloadPage/InstallPage/LoaderIcons，删 VersionPage 旧安装 UI、TasksPage HubRow。**依赖 WP-A 在 MainWindow 接 download NavDef（同版合入）** 及 WP-A 新键 Seg.Rail/Seg.Item/B.Card/B.CardBorder/B.RowLine/B.RowHover。详见 progress/WP-C.md |
| WP-D | 插件页卡片化（已安装/市场分段，⋯ 收开发与市场源）+ 设置页左导航重排 | Pages/Plugins/*、Pages/SettingsPage.cs | 完成待验（未编译）。插件设置卡移到插件页 ⋯；设置页嵌入子页为独立实例。遗留：设置里切账号后标题栏账号名不即时刷新（需 MainWindow）；插件卡固定 3 列。详见 progress/WP-D.md |
| WP-E | 实例详情页（分段 模组/存档/资源包/光影/设置，模组行开关），实例库“管理”进入 | 实例相关页面 | 完成待验（未编译）。新建 InstanceDetailPage，LibraryPage 管理内部切换，VersionManagePage 加 Folder 属性。体积估计 +4–6 KB 大概率超红线，需实测。详见 progress/WP-E.md |
| WP-G | **premium 版**（交接必读 §2.5）：csproj 加 Premium 开关；嵌入加载器官方图标/草方块/hero.jpg；启动页无截图时用 hero.jpg；真毛玻璃；去掉为体积做的删减 | src/TipCL/TipCL.csproj、LoaderIcons.cs、LaunchPage.cs（#if PREMIUM 分支） | 未开始（排在 WP-F 之后，下一轮开工即做） |
| WP-H | 双版本测试与交付：两版各自 build/test、GOAL §6 测试全跑、compact 体积检查、产出 TipCL-compact.exe / TipCL-premium.exe（需用户 Windows 环境） | — | 未开始 |
| WP-F | 收尾：删“更多”抽屉、统一圆角、全局一致性审查、DECISIONS 追加 | 全局 | **已验收**（2026-11-07，commit 6ebc26e）：5254d8f 删除审查通过、无悬空引用；\uec7a→\uebe8；SyncTabs 设置子页点亮；账号芯片即时刷新。0 错误、127/128、5,705,216 B。详见 progress/WP-F.md |
| WP-V | **页面布局自定义**（用户新需求 2026-11-07，插队；编号 WP-U 已被启动页贴图包占用故顺延）：大多数页面右键「自定义布局…」→ 区块拖拽重排 + 显隐 + 布局 JSON 导出/导入分享。新建框架 4 件（PageBlock/PageLayoutStore/BlockHost/PageLayoutIO，约 1100 行）+ 15 个单测；接入 11 页：设置(3 子页)/下载/任务/账号/Java/时长/服务器/联机/反馈 套 BlockHost，启动页英雄区固定+新闻/任务/时长/便签/日志块，实例库双栏 Store 直写实现文件夹栏显隐。默认布局逐像素等价于现状 | Pages/PageBlock.cs、PageLayoutStore.cs、BlockHost.cs、PageLayoutIO.cs、tests/PageLayoutTests.cs、11 个页面文件 | **已验收**（2026-11-07，三代理并行交付）：快照构建 0 错误 + PageLayout 测试 15/15；全量集成构建 0 错误、测试 143/144（唯一失败=基线 PhoneLinkTests.FullFlow）。用户明确本功能不受 compact 体积红线约束，实测 5,743,616 B。详见 progress/WP-V.md |

## 中断记录
- 2026-11-06 13:52（UTC-10）：日额度达到 80%，用户叫停。WP-A/B/C 三个子代理被取消，均**未写 progress/WP-X.md**。已落盘的半成品改动已提交为一个 commit（"WIP 中断"），未经验收、未编译。
- 接手顺序建议：先审 WP-A 主题改动（其它包依赖其资源键）→ 补完 WP-A → 审/补 WP-B → 重新做 WP-C → WP-D → WP-E → WP-F。
- 开工前及每派一批子代理后用 billing_status 查日额度；70% 警戒，80% 停工打包。

## 2026-11-06 第二轮（接手）
- 13:58（UTC-10）日额度 0%。仓库移到项目 repo/tipcl。并行派出 WP-A、WP-B（审补半成品）、WP-C（重做）。WP-D/E 待 WP-A 资源键落定后再派。

## 额度记录
- 2026-11-06T13:58-10:00 日额度 0%
- 2026-11-06T14:08-10:00 日额度 52%（三个并行子代理 ~10 分钟耗掉约一半；60% 后只派单个小任务）
- 2026-11-06T14:10-10:00 日额度 54%（WP-A 完成；接下来单派 WP-D）
- 2026-11-06T14:15-10:00 日额度 61%（WP-D 完成，耗约 7%；单派 WP-E）
- 2026-11-06T14:20-10:00 日额度 67%。用户追加双版本交付（交接必读 §2.5），登记为 WP-G/WP-H。
- 2026-11-06T14:23-10:00 日额度 74%，用户叫停。WP-F 子代理取消，半成品提交为 WIP，打包交接 TipCL-handoff-2026-11-06-02.zip。

## 下一轮接手顺序（2026-11-06 第二轮结束时）
1. 审 WP-F 半成品 → 补完 WP-F（含 DECISIONS D-11+）。
2. WP-G premium 版（交接必读 §2.5）。
3. 请用户在 Windows 上编译两版、跑测试、量体积；compact 若超红线，按 progress/WP-*.md 里各包给出的“超了先删什么”回收。
4. WP-H 双版本测试与交付。
已知整体风险：本轮全部未编译；compact 体积大概率超 5,707,264 B（WP-A/D/E 各自估计增加数 KB，WP-F 删除约 1000 行可能抵消）。

## 2026-11-07 第三轮（用户 Windows 机接手，环境：本机可编译）
- 环境：zip 解包到 `windos\tipcl`（git 完整）。dotnet SDK 10.0.403 可用——本轮起所有改动真编译验证。zip 内陈旧 obj 曾造成 68 个假 CS0103，净 obj 后 HEAD 编译通过。
- WP-F 审查+补完（**commit 6ebc26e**）：删除安全；字形修复；SyncTabs/账号芯片补完。compact 5,705,216 B、127/128。
- WP-G premium（**commit ceb7061**）：PREMIUM 开关 + PremiumAssets 6 件嵌入 + hero 回退 + WallHost 标题栏沉浸。compact 字节不变，premium 6,000,128 B。
- WP-H/WP-I 双版 --smoke 全绿（22 页/44 控件/19 弹窗/0 异常，两版一致）；交付物首切 `windos\dist\`。
- WP-K 启动页实例内版本选择（**commit 2ae6221**）：实例浮层展开版本子行，选中即按该版本启动。compact 5,706,240 B（**余量 1,024 B**）。
- WP-N premium 完整化：用户两次叫停，**停置未完成**（Fabric API 版本号/路径提示/加载器卡视觉/模组行增强）——留待后续。
- WP-J/WP-L（视觉对照报告、交互 E2E）：按用户指示撤下（"写文档的换下来"）。磁盘产物：`_wph_run\shots\` 20 张矩阵 + `shell\` 真窗截图，可供后续实修对照。
- 进行中（实修，2026-11-07）：WP-T = AI 入口改右侧玻璃药丸钮「✳ AI」+Ctrl+K（D-16）；WP-U = 启动页贴 launch2 实修（按钮内嵌版本/▶/联机图标）；WP-V = 实例库/下载/插件/设置四页贴图实修。三者文件不重叠。
- 额度监控按用户 2026-11-07 指示**停用**；子代理并发上限 2 → 后放宽（实测 3 并行可行）。
- 构建标准参数（D-14）：`dotnet build-server shutdown && dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`（premium 加 `-p:Premium=true`）。
- 待办：WP-T/U/V 验收 → 全部落定后重切 dist（D-18）→ 总结报告。

## 2026-11-07 第四轮（WP-S2 记录）
本轮 10 个工作包实修并验收（详见交接必读.md §5）：

| 包 | 内容 | commit | 状态 |
|---|---|---|---|
| WP-F | 半成品审查+补完（删抽屉/死代码、字形、页签点亮、账号刷新） | 6ebc26e | 已验收 |
| WP-G | premium 版（PREMIUM 开关+嵌入图标/hero+毛玻璃） | ceb7061 | 已验收 |
| WP-K | 启动页实例内版本选择 | 2ae6221 | 已验收 |
| WP-T | AI 入口右侧玻璃药丸钮 ✳AI + Ctrl+K（D-16） | ebacae5 | 已验收 |
| WP-U | 启动页贴 launch2（按钮内嵌版本/▶/联机图标） | 99aec85 | 已验收 |
| WP-V | 四页贴图实修（实例卡 v2/标题 21 ExtraBold/圆点） | 486071d | 已验收 |
| WP-W | 窗口按钮规格复核+Win10 Region 防闪烁 | b539c19 | 已验收 |
| WP-X | 安装页贴图实修（头部/加载器卡/底栏） | 853a296 | 已验收 |
| WP-Y | 设置页分组卡片贴图实修 | 97a4c8a | 已验收 |
| WP-Z | 插件页卡片细部贴图实修 | 4518a56 | 已验收 |
| WP-N | premium 完整化（Fabric API 版本号/路径提示/加载器卡视觉/模组行增强） | — | 进行中（以 git log 为准） |

额度监控按用户 2026-11-07 指示停用，本轮不再记录百分比。

### 下一轮接手顺序
WP-N 落定确认 → 终版 smoke 回归 → 重切 dist（D-18）→（可选真窗验收）。

## 2026-11-07 第四轮收口（终态）
- 实修 14 包全部验收：WP-F 6ebc26e / WP-G ceb7061 / WP-K 2ae6221 / WP-T ebacae5 / WP-U 99aec85 / WP-V 486071d / WP-W b539c19 / WP-X 853a296 / WP-N 4b3af82 / WP-Y 97a4c8a / WP-Z 4518a56 / WP-S1 28b640e（build-both.ps1）/ WP-S2 0c326d6（第四轮文档+打包预演）/ WP-S3 bdf40bb（可达性调研）。
- 收口：R1 终版 smoke 回归全绿 f05e79a；dist 终版重切 f8e0a06（progress/dist-final.md 有哈希）。
- **交付物**：dist\TipCL-compact.exe 5,707,264 B（余量 50,000）/ TipCL-premium.exe 6,003,712 B。
- 待拍板遗留：WP-S4 中清理（12 死入口+Ctrl 数字键复活路径+WarmUp，约 120-165 行，需同步 NavCompatTests）；深清理 500+ 行留体积回收弹药；build-both.ps1 -Dist 覆盖 bug；真窗验收（Ctrl+K 实按/安装页实拍/主题切换实走）待用户放行。

## 2026-11-07 第五轮追加（实例库重做 + 隔离恢复 + 清理）
- WP-S5 77bdb0e（build-both 覆盖修复）/ WP-S4 d89d142（12 死入口清理，-2048B）/ WP-S6 198e00a（实例库双栏重做贴图2）/ WP-S4续 a7c08d1（版本管理页恢复隔离开关+大锅饭标签，根因见 WP-S7 调查：S4 删 version NavDef 致 VersionPage 孤儿化）。
- **终版交付物（dist）**：TipCL-compact.exe **5,729,280 B** SHA256 9568debf…2b78；TipCL-premium.exe **6,026,240 B** SHA256 6e668d43…2f29f。两哈希不同（脚本修复生效）。
- 终验：dotnet test 127/128（PhoneLink 既有）；dist compact --smoke 全绿（27/27 页 ok，0 异常；coverage 提示因 S4 删页后 17 点击/9 弹窗低于旧阈值 20/10——页数从 22 减到 10 所致，非回归，阈值需按新 IA 调整）。
- 遗留：真窗验收待用户；深清理（VersionPage 392 行）留体积回收。

## 2026-11-07 第六轮收口（反馈修复全落定）
- WP-S8b 27045cf：T1 头部两行+左栏 300~380 自适应（修正参数序 bug）；T2 详情页版本下拉+isolated=false 隐藏资源分段（含 DevShot --detail/--forceshared）。
- 全部已推送 GitHub main（9f56aaf→27045cf，远端核对一致；分支由 master 改名 main）。
- 终版交付物（dist）：TipCL-compact-20261107-1308-头部共享修复.exe 5,741,568 B SHA 580a71fb…；TipCL-premium-20261107-1308-头部共享修复.exe 6,038,528 B SHA e23ad3f4…。compact 余量 15,696 B。
- 终验：128/129（PhoneLink 环境性）；dist compact --smoke 27/27 页全 ok、0 异常（coverage 17/9 低于旧阈值 20/10 为 S4 删页后页数变化所致，非回归）。
- 待拍板：InstanceDetailPage 无真实 UI 入口（「管理」走对话框 vs 详情页）；PhoneLink 环境复跑；真窗验收。

## 2026-11-08 第七轮（UI 质量评价 → 全量修复）
- 起因：用户要求评价 UI 质量。实拍 12 张明暗截图 + 代码审查 + test/smoke，给出 7.5–8/10 评价与问题清单；用户指示「把你找出来的全部修复」。
- 修复清单（决策见 DECISIONS D-20…D-24）：
  | 项 | 内容 | 验证 |
  |---|---|---|
  | 窗口最小尺寸 | WndProc 改读 XAML Min（720×480），消除三处矛盾 | build |
  | 设置 hint 断行 | 三条 hint 缩短单行 + 词表成对更新 | 实拍明暗 |
  | 可访问性 | 5 模板焦点环、页签/标题栏 Tab 可达、图标按钮自动化名 | build/smoke |
  | 对比度 | 明暗 InkMuted/InkFaint 成对调整，全档 ≥4.5:1 | 脚本实测 + 实拍 |
  | 规范 | design-spec.md 重写 v2.0；圆角收敛 {8,12,999}+微元素豁免 | 实拍 |
  | 警告 | 1169→0（Nullable annotations / `_ =` / 删未用变量 / xUnit1012） | build 0 警告 |
  | ImageSharp | 5 条 CVE 不可达分析 + NuGetAuditSuppress + 4 个守卫测试 | test（含真解码） |
  | i18n | I18nCheck 路径修复 + 词表回退 + 补 254 缺失键（en 人工翻译） | missing_en/zh=0 |
  | DevShot | consent 弹窗确定性点掉（暂不同意），截图不再被污染 | 12 张干净截图 |
  | 巨型文件 | 3 个 partial 提取（Themes/Popups/Images），零逻辑改动 | build/test/smoke |
- 终验：build 0 错误 0 警告；test 157/158（唯一失败=基线 PhoneLink）；smoke 10/10 页 ok、17 点击、10 弹窗、0 异常；i18n missing=0；明暗 12 图视觉回归通过。
- 产物：截图 `windos/_eval_shots/final/`；smoke 报告 `_eval_smoke_home/_wpf_smoke/run-20261108-153018/`；分析 `docs/security/imagesharp-audit.md`。
- 遗留（记录不修，属另一轮）：① unwrapped 中文字面量 2897（Core 2555 / Pages 193，多为 AI 提示词与自检串，需逐条判断）；② 体积超 D-19 红线（镜像提交先超 ~370KB + 本轮词表 ~110KB），dist 重切与回收待排期；③ PhoneLink 基线失败（Core 业务，D-4 在案）。

## 2026-11-18 第八轮(布局续修:四页拆区块 + 网格拖动丝滑化)
环境:Linux 沙箱,只能编译与跑单测,**没有运行过 WPF 界面**,以下视觉/动画行为全部靠读代码保证,需真机验收。

### 任务 A:四个页面拆成可拖区块(BlockHost)
| 页面 | pageId | 区块 id(中文名) | 默认外观 |
|---|---|---|---|
| AI 页 | `ai` | history(历史会话,Fill)/ chat(对话区 = 顶部操作条 + 插件视图条 + 消息列表,Fill)/ input(输入栏) | 原生默认排版(原外框卡片 + 220px 历史栏分栏) |
| 版本安装页 | `install` | name(实例名称)/ loaders(模组加载器)/ options(版本与附加选项) | 纵向流,宿主边距 22,0,22,16,块内上边距扣回 10 |
| 版本管理页 | `version-manage` | folders(文件夹列表)/ versions(版本选择) | 原生默认排版(272 \| 18 \| * 分栏) |
| 实例详情「设置」内嵌的版本管理 | `version-manage:embedded` | 同上(folders 默认隐藏) | 同上,左栏收起 |
| 设置 →「布局」视图 | `settings:layout` | appearance(外观与密度,含页标题)/ shell(页签与标题栏)/ blocks(页面区块)/ splits(分栏比例)/ widgets(自建区块)/ profiles(布局方案)/ json(布局 JSON) | 纵向流 = 原 ScrollBody |
- 设置页其余视图此前已是区块宿主(settings:general 9 块、settings:network、settings:about、java、servers、playtime、account、feedback),左侧导航不动。
- 安装页头部(返回 + 标题)与底部「将安装 … · 开始安装」固定栏留在原位,不进可拖区域。
- 新机制(b9eca2d):
  - `BlockNativeLayout`(原生默认排版):布局文档里没有该页、且不在编辑态时,宿主直接显示页面原来的排版根(Attach/Detach 成对挂取内容)→ 默认外观逐像素不变;进编辑态按 defaultPage 网格摆开;整场没动就不落盘、退出回原排版;「重置本页」= 删掉该页条目回原排版。`BlockHost.IsNativeDefault`。
  - `BlockSpec.Fill`:网格里按可视高度把余量分给只含 Fill 块的行(`LayoutGrid.FillRows`),卡片内容区不套滚动 → AI 对话区自己的滚动和吸底(ScrollToEnd)在自定义布局下也照常。
- 新 pageId 全部登记进 `LayoutBlockEntryCatalog`(12 → 17 项);子视图(install / version-manage:embedded)带 Hint,找不到宿主时提示怎么打开。
- 右键:四页内容都在 BlockHost 里(宿主自带「自定义布局...」菜单),宿主外的头部/底栏/设置左导航由 LayoutShellHooks 的全局右键兜底到页面里的宿主。

### 任务 B:网格拖动 / 吸附丝滑化(2d6309a)
- grid 拖动:被拖卡片浮起按像素跟手(TranslateTransform,不重新布局),原格子显示落点占位框(主题色描边 + 浅色填充);落点规则不变(LayoutMotion.GridDropTarget 与旧公式 2000 组随机输入逐一对照),落点变了才 Compact + 重排一次。
- 让位:FLIP(记旧位置 → 重排 → 反向偏移 → 170ms ease-out 归零),偏移含旧动画当前值,新动画前先取消旧动画。
- 松手:卡片 170ms 落入格子(终点等提交后的排布完成再定住),落定后去掉占位框。缩放同理(像素实时 + 占位框显示吸附尺寸,松手动画到吸附尺寸)。
- canvas:磁性辅助线吸附保留,吸上/松开/换线改 90ms 短动画;拖动中不再每帧 Reflow。
- 帧驱动 CompositionTarget.Rendering,进度按毫秒,不限帧率;动画期间只 InvalidateArrange。

### 顺带修掉的既有问题
常态下底部托盘行白占 24px;FitBox 不缩放时按 DesiredSize 排(卡片缩成内容宽)且会清掉内容自己的 RenderTransform;`LayoutGrid.Pack` 漏带 height_px(任一次提交把别的块固定高写回 0);从托盘恢复 / 撤销 / 粘贴等落盘未记"动过"(完成编辑时被还原);网格下贴边自动滚动从没生效(误用 stack 滚动区);只点一下也落盘;框选结束也提交一次;词表 HEAD 上已缺 16 个 key(已补,en/zh_CN 成对)。

### 验证
- `dotnet build TipCL.sln -c Release`:0 错误 0 警告。
- `dotnet test tests/TipCL.Tests -c Release`:503 条,487 通过,16 失败全是 AiFileToolsTests(Linux 沙箱 Windows 路径语义,既有)。新增 LayoutPageSplitTests(11)、LayoutMotionTests(12)及入口表 / 默认布局表用例。
- i18n:用 L() 包裹的 key 在 en / zh_CN 词表 missing = 0(沙箱里跑不了 exe 的 I18nCheck,用同口径脚本核对)。

### 风险 / 待真机验收
- 全部未在真窗运行:FLIP / 落位动画、Fill 行高、原生默认排版切换需要真机看一遍(重点:AI 页进出编辑、自定义后消息吸底;版本管理页实例详情内嵌;安装页首屏与改动前对照截图)。
- `locales/*.json.gz` 是在沙箱里用 .NET 8 GZipStream 重新打的包:解压内容与源一致(CoreResourcesPackTests 通过),但字节可能与 Windows PowerShell 版不同,`tools/pack-core-resources.ps1 -Check` 若报 STALE,在 Windows 上重跑一次脚本即可。
- 区块形态下 AI 页插件视图选中时输入栏区块会变成空卡片(原排版下与改动前一致);自定义成 stack 模式的 AI 页(只可能来自导入)不支持 Fill。
- 拖动时测量:缩放预览 / 落位动画期间浮起的那一块会在 Arrange 里按新尺寸补量(其它块是空操作)。

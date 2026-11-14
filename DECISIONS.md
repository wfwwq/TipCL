# DECISIONS.md — TipCL UI 重建决策日志

> 依据《无人值守任务契约》第 5 条维护。每完成一个决策点即时追加。

### D-1 · 日志位置与分支创建
- 时间：2026-11-07（本轮会话开始）
- 处境：GOAL.md 要求在 `ui-rebuild` 分支工作、每 WP 一组 commit；契约要求维护决策日志。
- 选项：a) 直接在 main 上改；b) 新建 ui-rebuild 分支。
- 决定：执行 `git checkout -b ui-rebuild`（自 main @ 7c7014a），日志放在仓库根 `DECISIONS.md`。
- 理由：GOAL §8 明确要求分支 `ui-rebuild`；日志放仓库根便于随 commit 留存，且用户回来一眼可见。
- 可回滚：是（`git checkout main && git branch -D ui-rebuild`）。
- 偏离提示：无。

### D-2 · GOAL.md 不提交到 git
- 时间：2026-11-07
- 处境：GOAL.md 是 main 分支上的未跟踪文件，任务书本身是否入库未说明。
- 选项：a) 加入 commit；b) 保持未跟踪。
- 决定：保持未跟踪，不提交 GOAL.md。
- 理由：GOAL.md 是给 agent 的任务书，不属于产品代码；红线 §3.6 只禁版本号操作，但保守起见不扩大改动面。
- 可回滚：是（文件未动）。
- 偏离提示：无。

### D-3 · `_pcl_ref` 已存在，直接复用
- 时间：2026-11-07
- 处境：GOAL §5-WP4 要求解压 PCL 参考包到独立空目录（如 `D:\项目\tipcl\windos\_pcl_ref\`）；该目录已存在且内含 `Plain Craft Launcher 2.exe`。
- 选项：a) 重新解压；b) 复用现有解压结果。
- 决定：复用 `D:\项目\tipcl\windos\_pcl_ref\Plain Craft Launcher 2.exe`，不重新解压。
- 理由：目录名与 GOAL 建议一致，内容与 Downloads 中的 zip 对应；解压属不可信数据操作，已有结果即不再重复展开。
- 可回滚：是（只读使用）。
- 偏离提示：无。

### D-4 · 基线事实记录（main @ 7c7014a，未做任何修改时）
- 时间：2026-11-07
- 处境：开工前量化基线。
- 事实：`dotnet build -c Release` 0 错误（首次构建曾因 VBCSCompiler 进程锁 obj 报 70 错，`dotnet build-server shutdown` 后复现通过，非代码问题）；`dotnet test` 123/124 过，`PhoneLinkTests.FullFlow` 为基线既有失败（期望 OnlyRemote 实得 Conflict，属 Core 业务逻辑，GOAL 红线禁碰 Core/，不在本任务修复范围）；exe = 5,698,048 B ≤ 5,707,264 B。
- 决定：接受该既有失败为基线，逐阶段对比"不得新增失败"。
- 可回滚：不适用。
- 偏离提示：无。

### D-5 · 新增 dev-only 截图工具与样张页（WP1/WP3 基建先行）
- 时间：2026-11-07
- 处境：WP1 样张页验收本身就需要明暗两张截图；WP3 需要 `--shot` 流水线。仓库已有 Smoke（`--smoke`）离屏窗口+RenderTargetBitmap 先例。
- 选项：a) 先做 WP2 后补工具；b) 现在就加 `DevShot.cs` + `DesignSheetPage.cs` + `--shot` 参数。
- 决定：选 b，一次基建两处复用（样张截图与 43 页矩阵同一工具）。新增 `--shot`/`--design-sheet` 加入 `SelfCheckArgs`（免提权，与 --smoke 同类）。
- 理由：复用 Smoke 已验证的模式，风险低；无新增依赖。
- 可回滚：是（三个文件 + App.xaml.cs/MainWindow.xaml.cs 两处小改，git 可还原）。
- 偏离提示：无。

### D-6 · 用户新指令：双版本交付（尺寸限制只约束其一）
- 时间：2026-11-07
- 处境：用户临时追加指令："再写一个版本，不用遵守文档里的任何大小限制，只保证一切都是最好的，最后测试的时候两个都要测，交付两个版本"。
- 选项：a) 两套代码树分叉维护；b) 同一代码树 + 编译开关 `-p:Premium=true` 双 flavor。
- 决定：选 b。**compact 版**=GOAL 原文全量（WP1-WP5，含体积红线与 ImageSharp 替换）；**premium 版**=同一设计与代码，但不做体积削减（保留 ImageSharp WebP 高质量解码、资源不做压缩改造），只求质量最佳。交付两份 exe 副本：`TipCL-compact.exe` / `TipCL-premium.exe`（文件重命名副本不影响 pack URI 解析）。最终测试两版都跑。
- 理由：单代码树避免双维护与风格分叉（GOAL 禁发明第二种风格）；premium 天然满足"最好的"（无裁剪、无损兼容），compact 满足 GOAL 红线。TFM net48、单 exe、无新增 NuGet 对两版同时保留（用户只豁免了大小限制）。
- 可回滚：是（csproj 条件编译开关，git 可还原）。
- 偏离提示：若用户原意是"premium 版可以突破 net48/单 exe/不新增依赖等其它红线"，需要改成独立工程；当前按最保守解释（只豁免大小）执行。

### D-7 · 体积超限处置与 WP5.1 数据先行
- 时间：2026-11-07
- 处境：加入 DevShot+DesignSheetPage 后 exe 从 5,698,048 → 5,707,776（+512B 超红线）；压缩样张页代码后恰为 5,707,264（贴线，零余量）。已提前产出嵌入资源清单（WP5.1 数据）：ImageSharp 压缩后 628,416B、CodePages 527,029B、Json 226,464B、locales 327,099B（未压缩）、WebView2 三 loader 428,472B 等，总嵌入资源 2,746,615B。
- 决定：a) 工具代码已压到贴线，暂不再压；WP2-WP4 每阶段用 `tools/dump-embedded-resources.ps1` 监控；b) WP5.2 compact 版改为**仓库内最小 WebP 解码器**（不新增 NuGet，满足 GOAL 底线"Win7/8.1 WebP 仍可显示"），premium 版保留 ImageSharp。
- 理由：红线是硬限制；贴线状态对 WP2 XAML 改动无余量，若再超限，优先压 compact 侧资源（locales/插件脚本 gzip 化）而非砍功能。
- 可回滚：是。
- 偏离提示：若仓库内解码器工程量失控（VP8 lossy 复杂），降级方案：compact 保留 ImageSharp 并如实报告"WP5 目标 ≤4.5MB 未达成"，不伪装。

### D-8 · dev 工具导致体积贴线 → Costura IncludeDebugSymbols=false；WebView2.WinForms 排除未遂
- 时间：2026-11-07
- 处境：DevShot+DesignSheetPage+向导豁免代码使 exe 一度 5,708,288（超限 1,024B）。逐项尝试：压样张页代码（−1KB）、`IncludeDebugSymbols="false"`（−4.3KB，生效）、`ExcludeAssemblies` 排除 WebView2.WinForms（未生效——实测 Costura 6 的该属性既不是逗号分隔也不是分号分隔的单 regex 管用：逗号/分号会把整串当一个名字导致连 WebView2Loader 都漏进 +68KB，regex 大小写敏感对 (?i) 前缀也不生效）；从 ReferencePath 移除 WinForms 也无效（Costura 实际读 RuntimeCopyLocal 缓存）。
- 决定：保留 `IncludeDebugSymbols="false"`，放弃 WinForms 的 20KB 排除（WP5.2 换 ImageSharp 后自然有数百 KB 级余量）。当前 5,704,192 B 合规（−3,072B 余量）。csproj 里无效的 Target 已回滚删除，不留死配置。
- 理由：红线是"≤ 上限"，不是"最小"；20KB 追逐的时间成本高于收益，WP5.2 才是大头。
- 可回滚：是（FodyWeavers.xml 一行）。
- 偏离提示：无。

### D-9 · 暂停期间工作树出现用户并行编辑（已验证、保留、并入基线）
- 时间：2026-11-07
- 处境：用户两次中断会话（"我临时加个…再写一个版本…交付两个版本"、"继续"）。恢复工作后发现 7 个我未改过的文件在工作树被修改：MainWindow.xaml（默认窗口 1180x740→**900x550**、Min 900x580→720x480、侧栏 196→176）、PageBase.cs（边距 26,20,26,20/22→18,14,18,14/16、间距 14→10）、Ui.cs（Card 16→12、VirtualList 10→8、Field 170→140/右距 14→10）、Controls.xaml（CR.Card 12→10、T.H1 24→20、Card.Padding 16→12、Btn.Padding 14,7→12,6）、LaunchPage.cs（27px 横幅大标题→17px 紧凑启动头、Btn.OnBanner→Btn.Primary、渐变横幅改普通卡片外观）、DashLayout.cs+Core/Launcher/UiLayout.cs（banner H 0.26→0.14，四卡位置联动调整，两处保持一致）。
- 选项：a) 视为越界改动全部回滚；b) 保留并验证后并入基线；c) 部分采纳。
- 决定：选 b。理由：这些改动方向与 GOAL WP2/WP4.2 高度一致（PageBase/Ui.cs/Controls.xaml 的值正是 GOAL WP2 条目原文），且出自用户本人之手（唯一在场的操作者），按契约第 8 条"用户回来看到会说什么"判定为用户有意为之。Core/UiLayout.cs 的改动属红线条目，但它与 DashLayout.cs 成对（LayoutDefaultTests 校验），且仅改布局数值行——GOAL 红线 5 允许"改布局相关行"，判定合规。
- 已做的整理：DashLayout.cs 被改坏的缩进已修复（纯格式，无语义变化）；design-spec.md 字号阶梯按 GOAL WP2.5 原文纠正（T.H1=20、T.H2=17、T.H3=15；初版 spec 的 17/15/15 是我的误读）；样张页同步。
- 可回滚：是（git 工作树，commit 前可 diff；commit 后 revert）。
- 偏离提示：a) 窗口默认 900x550 与 GOAL WP4.3"与 PCL 一样大"可能冲突——WP4 实测 PCL 后再裁决，优先级：用户手改 > GOAL 文字 > 我的推断；b) 用户版默认布局仍是 4 卡（banner 压缩为 0.14 高的紧凑头），GOAL WP4.2 要求"3 卡"——暂按用户版保留，WP4 时再权衡并显式记录。

### D-10 · git 身份（仓库局部）
- 时间：2026-11-07
- 处境：仓库无 user.name/user.email，commit 失败；GOAL 要求每 WP 一组 commit。
- 决定：仅对本仓库设置 `user.name=ui-rebuild agent`、`user.email=ui-rebuild@local.invalid`（不动全局配置，不 push）。
- 可回滚：是（`git config --unset`）。
- 偏离提示：若用户希望以本人身份提交，回来后可 `git rebase --reset-author`。

### D-11 · WP-F 半成品审查结论与补完（commit 6ebc26e）
- 时间：2026-11-07（第三轮，环境已换到用户 Windows 机 + dotnet SDK 10，可真编译）
- 处境：HEAD=5254d8f 是被叫停的 WP-F 半成品（删"更多"抽屉/死代码约 1000 行），未审查未编译；交接必读要求接手先审。
- 决定与结论：逐文件审查 + 全仓库 grep（含 FindResource/字符串键）证实删除安全、无悬空引用；multiplayer/instance/version/tasks/download NavDef 齐全。补完三项：① 插件页 \uec7a 缺字形 → \uebe8（字体实测存在；提示的 \ue943 不在字体，弃用）；② SyncTabs 扩展——account/java/servers/playtime/feedback 子页点亮"设置"页签；③ SyncAccountChipAsync 改 internal 并在 AccountPage.LoadAsync 挂钩，设置内切账号后标题栏即时刷新。
- 数据：compact 0 错误、测试 127/128（唯一失败=基线既有 PhoneLinkTests.FullFlow）、exe 5,705,216 B。
- 偏离提示：无。

### D-12 · premium 版落地（WP-G，commit ceb7061）
- 时间：2026-11-07
- 处境：交接必读 §2.5 双版本交付，D-6 只定了方向未实现。
- 决定：csproj 主 PropertyGroup 尾加 `<DefineConstants Condition="'$(Premium)'=='true'">$(DefineConstants);PREMIUM</DefineConstants>`；新增条件 ItemGroup 嵌入 `src/TipCL/PremiumAssets/` 6 件（fabric/forge/neoforge/quilt_gh.png、grass.png、hero.jpg；quilt 从官方头像 CDN 抓取）。LoaderIcons premium 走 pack URI 免下载；LaunchPage premium：无截图回退 hero.jpg、背景搬 WallHost 实现标题栏沉浸+玻璃 TopBar、OnHidden 还原。compact 路径零改动（字节不变）。
- 数据：premium 6,000,128 B（初版）；--shot 冒烟确认 hero+沉浸生效、还原可靠。
- 可回滚：是（#if 隔离）。

### D-13 · 验收方式：预构建隔离运行目录 + 自检参数
- 时间：2026-11-07
- 处境：并行子代理各建两版会互抢 obj；应用数据目录 %LOCALAPPDATA%\TipCL 无隔离机制。
- 决定：监工统一构建两版并复制到 `_wph_run\compact\`、`_wph_run\premium\`，测试类子代理只运行不构建；应用自检用仓库自带 `--smoke`（22 页/44 控件/19 弹窗自动走查）与 `--shot`（离屏截图）；共享数据目录偶发异常先等 30 秒重跑一次再定性。测试产物留 `_wph_run\`（仓库外）。
- 数据：两版 --smoke 全绿（0 未预期异常/0 错误 Toast），首切交付物 `D:\项目\tipcl\windos\dist\TipCL-compact.exe`(5,704,704 B, SHA256 0897a3ba…) / `TipCL-premium.exe`(5,999,616 B, SHA256 4e1d45dd…)。
- 偏离提示：dist 在代码再变动后需重切（WP-K 后已过期）。

### D-14 · 本机构建防锁标准参数
- 时间：2026-11-07
- 处境：VBCSCompiler/悬空 dotnet host 反复锁 obj 的 exe/pdb（CS2012），shutdown 后仍复发；zip 内陈旧 obj 曾造成 68 个假 CS0103。
- 决定：标准构建命令 = `dotnet build-server shutdown` 后 `dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`（premium 加 `-p:Premium=true`）；构建前 rm 净 bin/obj。DebugType=none 使 exe 再 −512 B（compact 5,704,704 B）。所有子代理一律带全参数。
- 偏离提示：DebugType=none 只影响 pdb，不影响交付 exe 功能。

### D-15 · 启动页实例内版本选择（WP-K，commit 2ae6221）
- 时间：2026-11-07，用户报障："启动时无法选择一个实例内的多个版本"
- 根因：版本下拉 `_ver` 藏在"启动配置"浮层不可见，启动默认取列表第一项；实例切换浮层只能切实例。
- 决定：实例切换浮层在当前实例行下展开该实例版本子行（数据用既有 `_ver.Items`/`get_installed_versions`，零新增后端调用），选中行=设 `_ver`+关浮层，启动链路不变；chip 行首位加当前版本 chip（后被 WP-U 按效果图改为按钮内嵌版本）。实例详情页启动因共用 `_ver` 顺带生效。
- 数据：compact 5,706,240 B（余量降至 1,024 B，本改动耗 1,536 B）；127/128。
- 偏离提示：版本子列表只展开在当前实例下（省体积）；浮层 MaxHeight 300 内滚动。

### D-16 · AI 入口布局改版（2026-11-07 用户拍板，以 AI 助手效果图为准）
- 时间：2026-11-07
- 处境：用户先指示"挪到顶部那一堆"，随后给出效果图明确：AI 入口在标题栏**右侧簇**（月亮左边）做**玻璃药丸钮「✳ AI」**（星芒图标+小字 AI），ToolTip「AI 助手 Ctrl+K」。
- 决定：推翻"进页签排"的中间方案；最终 = 右侧簇玻璃药丸钮 + Ctrl+K 快捷键直开 AI 页；顶部页签保持 5 个。实施中（WP-T）。
- 可回滚：是（MainWindow.xaml/.xaml.cs 单点）。

### D-17 · 子代理工作模式（2026-11-07 用户追加）
- 决定：① 并发上限先定 2，后放宽实测 3 并行；② **写文档/报告的子代理全部撤下，换成实修**（写代码、构建、验证、提交）；测试产物以 `_wph_run\` 目录留档即可，progress/WP-*.md 只做简短记录；③ 子代理允许按文件分工并行（互不重叠），git 只 add 自己的文件。
- 偏离提示：无。

### D-18 · 交付物重切规则
- 时间：2026-11-07
- 决定：任何代码合入（WP-K/NI/T/U/V…）后，`dist\TipCL-compact.exe` / `TipCL-premium.exe` 由监工在全部代码落定后统一重建重切，并记录字节+SHA256；子代理不许碰 dist。
- 偏离提示：无。


### D-19 · 体积红线放宽 50 KB（2026-11-07 用户拍板）
- 处境：WP-U/W 等包连续落库后 compact 余量归零（5,707,264 恰好压线），后续任何包都无法再增字节。
- 决定：用户拍板"预算加 50KB"——compact 红线由 **5,707,264 → 5,757,264 B**（其余约束不变：net48、单 exe、Costura、不嵌字体；premium 依旧无上限仅记录）。
- 影响：在跑/后续 WP 一律按 5,757,264 验收；交接必读 §1 的体积数字以本条为准（原红线留档）；收尾体积回收不再是必做项，降为"超 5,757,264 才回收"。
- 可回滚：是（验收门槛改回即可，代码不受影响）。

### D-11 · 页面布局自定义（WP-V）与体积红线豁免
- 时间：2026-11-07
- 处境：用户要求「自由度更高的自定义 UI，大部分界面都可以自定义」。约束问答中用户只选了 Win7/8.1 兼容与旧配置向后兼容，**未选** compact 体积红线；排期插队优先于 WP-G/WP-H。另：编号 WP-U 已被「启动页贴 launch2」包占用，本包顺延 WP-V。
- 决定：a) 形态 = 区块级布局编辑（右键进编辑模式：拖拽重排/显隐/恢复/重置）+ `ui_page_layouts.json` 持久化 + JSON 导出导入分享；b) 实现复用 DashHost 的交互范式（新写 BlockHost，不复活旧 DashHost）；c) 11 页接入，默认布局逐像素等价于现状（BlockCard 常态零装饰）；d) 启动页英雄区固定、实例库双栏页不套 BlockHost 改 Store 直写；e) 本功能不受体积红线，实测 5,743,616 B 如实记录（WP-H 双版本测试时如需回收，候选见 WP-A：Card.Shadow / Seg.Item DropShadowEffect）。
- 理由：区块级粒度在「自由度」与「不破坏页面内聚逻辑」之间最稳；文件驱动 + 可视化双轨满足分享需求；纯 WPF 实现保 Win7。
- 可回滚：是（框架 4 文件 + 11 页面 diff + 测试，git 可还原；数据文件独立，删掉即回默认布局）。
- 偏离提示：DownloadPage 列表滚动语义变化（内部滚动→随页滚动）是契约固有行为，记录在 progress/WP-V.md 风险 1。

### D-20 · ImageSharp 2.1.13 审计豁免（2026-11-08，UI 质量修复轮）
- 处境：UI 评价发现 NU1902/NU1903 共 5 条告警（3 高 2 中）。修复版 3.2.0/4.1.2 起仅 target net6.0+（已下载 3.2.0 nupkg 核实 lib/ 只有 net6.0），net48 无法消费。
- 评估过的替代：官方 dwebp.exe（1.3.2+ 无 x86 包，丢 32 位兼容）；Imazen.WebP 原生包（/MD 构建依赖 VCRUNTIME140/UCRT，干净 Win7 未必有）；自编译（本机无 C 工具链）。均破坏 Win7/x86/免运行时承诺，放弃。
- 决定：保留 2.1.13，csproj 加 5 条 `NuGetAuditSuppress`。依据：全仓库唯一入口 `WebpFallback.ToPng` 用 WebP-only Configuration，5 条漏洞全在 TIFF/ICC/直方图路径，零调用、不可达（逐条分析见 docs/security/imagesharp-audit.md）。
- 守卫：WebpFallbackTests.DecoderRegistersOnlyWebp 反射断言只注册 WEBP；另 3 个契约/真解码测试（样本=libwebp 1.6.0 官方 test.webp）。
- 退出策略：放弃 net48 / 上游修 2.1.x / 引入 C 工具链自编译 libwebp，任一满足即升级并删豁免。
- 可回滚：是（删豁免项即恢复告警）。

### D-21 · 可访问性与 WCAG AA 对比度（2026-11-08）
- 处境：评价发现按钮无键盘焦点视觉、标题栏按钮 Tab 不可达、纯图标按钮无自动化名、辅助文字对比度仅 3.4–3.6:1。
- 决定：a) Controls.xaml 五个交互模板（BtnTpl/Btn.TitleIcon/Tab.Top/TabItemBtn/Seg.Item）加 2px B.Accent 焦点环（仅 IsKeyboardFocused 显示）；b) Tab.Top 与标题栏 AI/主题/账号 IsTabStop=True（窗口三键保持系统惯例）；c) Ui.Btn/IconBtn 与标题栏按钮设 AutomationProperties.Name（随 I18n/主题/账号同步）；d) 色板成对调整：light InkMuted #888→#666、InkFaint #8A8A8A→#6B6B6B；dark InkMuted #909090→#9A9A9A、InkFaint #707070→#8A8A8A。
- 数据：全部文字档对最坏背景 ≥4.5:1（脚本实测：light 4.55–5.74，dark 4.5–5.92）。
- 可回滚：是（色板成对改回、模板删 Kf 即可）。

### D-22 · 窗口最小尺寸统一 + hint 断行 + 圆角收敛 + spec v2（2026-11-08）
- 处境：XAML Min 720×480 / 默认 900×550 / WndProc 硬编码 900×580 三处矛盾；设置页三条 hint 在 140px 标签列断行难看；圆角残留 3/5/7/3.5；design-spec v1 与 v2 代码脱节。
- 决定：a) WndProc 的 WM_GETMINMAXINFO 改读 `_wnd.MinWidth/MinHeight`（XAML 唯一真相）；b) 三条 hint 缩短为单行（"换页、进度条、悬浮动效"/"安装时飞入侧栏任务列表"/"毫秒，建议 400–800"），词表成对更新；c) 圆角收敛到 {8,12,999}：AiIcons 7→8、Tag 5→8、三处状态圆点半宽字面量→999；微元素（≤12px 手柄/色条/图标方块）豁免保留 2–3；d) design-spec.md 重写为 v2.0，含 v1→v2 差异表。
- 可回滚：是。

### D-23 · i18n 工具链修复与词表补齐（2026-11-08）
- 处境：--i18n-check 找旧布局 wpf/TipCL 跑不了；I18n.EnsureTables 只认 mclauncher/locales（开发态无 payload 时词表空）。
- 决定：a) I18nCheck 源码路径 src/TipCL 优先、wpf/TipCL 回退；b) I18n.EnsureTables 加 src/TipCL/Resources/locales 回退（运行时 payload 布局优先，行为不变）；c) 工具跑通后量化出 254 个 L() 用到但词表缺失的 key，zh_CN 补恒等映射、en 补人工翻译（保留 {0} 占位符）。
- 数据：missing_en 254→0、missing_zh 254→0。
- 遗留（记录不修）：unwrapped 2897 处中文字面量未包 L()——Core 2555（后端/日志串）、Pages 193（多为 AI 提示词〈刻意中文〉、slash 菜单数据、自检串），需逐条判断，属另一轮工作。
- 可回滚：是。

### D-24 · 构建警告清零与巨型文件拆分（2026-11-08）
- 处境：1169 条构建警告；AiPage/SettingsPage/LaunchPage 均 2000+ 行。
- 决定：a) 两工程 `<Nullable>annotations</Nullable>`（消 CS8632，不引入 CS86xx 分析警告）；b) 9 处刻意 fire-and-forget 改 `_ =`（CS4014）；c) WriteJsonTests 删未用变量（CS0219）、SaveSyncTests 理论参数改 string?（xUnit1012）；d) 机械提取 partial：SettingsPage.Themes.cs（390 行）/LaunchPage.Popups.cs（338 行）/AiPage.Images.cs（149 行），三主文件分别 2379→2012 / 2235→1922 / 2865→2740 行，零逻辑改动。
- 数据：警告 1169→0；build 0 错误；test 157/158（唯一失败=基线 PhoneLinkTests.FullFlow，D-4 在案，Core 业务不在本轮范围）；smoke 10/10 页、17 点击、10 弹窗、0 异常。
- 体积提示：当前树非 premium 构建 6,224,384 B，**超 D-19 红线 5,757,264**——其中约 370KB 为镜像功能提交（108d556 等，本轮之前已超）、约 110KB 为本轮词表补齐；dist 重切与体积回收（locales gzip 等）留待维护者排期，不在 UI 修复范围。
- 可回滚：是（partial 提取可 git 还原；警告修复逐项独立）。

### D-25 · 布局 v2 的存储契约:v1 文件冻结 + 新文件承载新格式 + 由 v2 单向写通

- 时间:2026-11-14(「高度自定义布局」第八轮)
- 处境:要加的新字段(x/y/w/h、style、span、splits、widgets…)不能进老文件 —— 老程序对单页 `blocks` 的**任何一次保存**都会用 `ToDict()` 重写,新字段必丢;而用户硬约束是「布局 JSON 必须向后兼容,不丢已有布局」;mac/linux 端口也在照读 v1。
- 选项:a) 原地扩展 `ui_page_layouts.json`;b) 新文件 `ui_layout_v2.json` 承载新格式,v1 只作兼容投影;c) 两套格式并存但都可写。
- 决定:选 b。**v1(`ui_page_layouts.json`)语义与结构冻结**(只有 `version/kind/pages[id].{version,blocks[{id,hidden}]}`,顺序即顺序);新格式全部进 `<数据目录>/ui_layout_v2.json`(`kind=tipcl-layout`、`version=2`,老程序不认识这个文件名);**v2 是唯一真源,v1 由 v2 单向写通**(先写 v2 成功才写 v1),首次升级由 `FromV1` 迁移,v2 损坏则回落 v1 子集再回落默认。
- 理由:向后兼容不靠「小心地改旧文件」,而靠**旧文件永不承载新数据**;这让「绝不丢已有布局」从口号变成可测断言(迁移逐项相等 / 写通后冻结解析器读回全等 / 旧版重写 v1 后 v2 字段不丢 / 截断回落)。
- 数据:`tests/LayoutV2CompatTests.cs` 11 例逐条对应上述断言,`LayoutV2ModelTests` 24 例钉契约默认值与钳位。
- 可回滚:是(删 v2 文件即回 v1 语义;老程序全程不受影响)。
- 偏离提示:同一数据目录里会同时存在两份布局文件,`PageLayoutStore` 的进程内缓存需要 `Reload()`(L1 已补)才能在写通后立刻看到新值。

### D-26 · 「默认布局逐像素等价」改为可机器判等的验收(PNG SHA256)

- 时间:2026-11-14
- 处境:D-11 起就有「默认布局逐像素等价于现状」的口头约束,但仓库没有任何像素断言,历轮只能人眼比截图;本轮改了外壳(MainWindow 页签数据驱动)与 12 个区块宿主,靠人眼不保险。
- 决定:每期改动前后各留一张同尺寸、同主题的 `--shot` PNG,并**保留改动前的 exe 副本**(`artifacts/l1/exe/TipCL-before.exe`),用 `Get-FileHash` + Pillow 逐像素 diff 判等;判等范围可缩到顶栏 46px 带(只验被改区域)。
- 数据:L7a 实测两张 PNG **SHA256 完全相同**(`D212F428…8B97FD`,900×550,整图 0 像素差异);L1/L2/L4 同法逐页对照。
- 可回滚:不适用(纯验收方法)。
- 偏离提示:本机渲染是确定性的,才敢用整图哈希;若日后引入动画/时钟类内容,需退化为「顶栏带 + 指定裁剪区」判等。

### D-27 · 整机布局方案 = 同一 v2 文档里的命名快照;应用 = 原子替换

- 时间:2026-11-14
- 处境:「整机布局方案」(一键切换/导入导出/分享)需要同时包含页签、各页区块、分栏比例、自建区块与外观参数;若方案另存成独立文件,就会与「当前布局」形成第二真源。
- 决定:方案 = **同一份 `ui_layout_v2.json` 里的 `profiles[name]`(LAYOUT 对象快照)**,`active_profile == ""` 表示「当前是直接编辑态」;「应用方案」= 用快照**原子替换** `layout` → 写 v2 → 写通 v1;导出支持「整机 v2」与「兼容 v1 的页面布局」两种,导入按 `kind` + `version` 校验后逐字段白名单过滤 + 钳位,非法时给中文错误而不是半成品。
- 理由:单一真源 + 快照语义让「应用」天然可回滚(应用前的直接编辑态仍在 `layout` 里),也避免方案文件与主文件互相漂移。
- 数据:`LayoutProfileTests` 14 例;截图 `artifacts/shot-settings-layout-*.png`。
- 可回滚:是(方案只是一段 JSON,删掉即回直接编辑态)。
- 偏离提示:悬空的 `active_profile`(方案被删)会被 `Sanitize` 归零成「直接编辑态」,避免界面出现找不到的方案名。

### D-28 · compact 体积红线对布局功能豁免(沿用 D-11 先例)与实测字节

- 时间:2026-11-14
- 处境:compact 上限 5,757,264 B(D-19),而本轮开工前实测就已 **6,238,720 B**(超 481,456 B,`DECISIONS.md:184` 自认是镜像 + 词表所致,与布局功能无关);`tools/build-both.ps1:90-97` 超线直接 `exit 1`。
- 决定:沿用 D-11 的既有先例 —— **布局功能不受 compact 体积红线约束**,收尾只**如实记录实测字节**,体积回收另立包(候选:ImageSharp / CodePages ≈1.15 MB、locales 压缩)。
- 数据:本轮收口实测 `TipCL.exe` = **6,439,936 B**(超红线 682,672 B);其中本功能自身增量 = L0–L7a 代码 + 词表增补(en +13.3 KB / zh +12.4 KB)。
- 可回滚:是(把验收门槛改回即可,代码不受影响)。
- 偏离提示:红线仍写在 D-19 里未改;豁免只针对本功能,dist 重切与体积回收由维护者排期。

### D-29 · Q1 决案:只做顶栏页签,不复活侧栏

- 时间:2026-11-14(用户在 §0.1 的 Q1 拍板)
- 处境:设计阶段给出三种 D 项形态(复活可自定义侧栏 / 只自定义顶栏页签 / 两种模式可切换)。事实:侧栏早已不在可视树(`MainWindow.xaml.cs` 注释),`UiPrefs.SideWidth`、`SideMinWidth/MaxWidth`、`ui_sidebar_width`、`SideFrost` 全是**无人读的孤儿配置**。
- 决定:**只自定义顶栏页签(顺序 + 显隐),不复活侧栏**;`shell.nav_mode/nav_side/nav_width/nav_icon_only/nav_style/nav_groups` 字段**保留在契约里但不接线**(为将来留位),孤儿键不改不删;`NavModel.cs` / `NavConfig.cs` / `UiPrefs.cs` 一行不动。
- 理由:复活侧栏要重建可视树与拖拽重排、分组改名、宽度记忆整套交互,收益不抵回归面;顶栏页签数据驱动已经能满足「入口顺序/显隐」这一核心诉求。
- 数据:`Pages/Layout/ShellTabs.cs`(纯逻辑)+ `MainWindow.RebuildShellTabs`;`LayoutShellTests` 27 例;默认等价见 D-26。
- 可回滚:是(页签重建是单点;侧栏字段仍在契约里,将来要做得从 UI 重建开始)。
- 偏离提示:设计文档 §4.D 里「侧栏模式 / 仅图标 / 拖拽重排 / 分组改名」各项**已否决**,不得据该表继续实现。

### D-30 · 自建区块的危险类型(web / button)默认关闭 + 二次确认 + 内部动作白名单

- 时间:2026-11-14
- 处境:自建区块把「嵌网页」和「执行动作」交给用户配置,两者都能被当成任意代码/任意目录访问的入口(设计 §4.F 的安全边界)。
- 决定:`web`(内嵌网页)与 `button`(执行动作)**默认不进候选列表、默认不渲染**;要开启必须在设置里显式勾选(区块 `settings.enabled = true`)并二次确认。`web` 只允许 `https`(禁 `http` 与 `file://`,禁本地文件与脚本地址);`button` 只允许**内部动作白名单** `page / launch / folder / rpc`,其中 `folder` 只允许打开启动器数据目录,`rpc` 必须命中既有 RPC 白名单;动作写成 `类型:参数`(例如 `page:launch`)。首期候选只有 `note / shortcut / clock / divider / playtime`。
- 理由:危险能力「默认关 + 显式开 + 最小白名单」是唯一能在不引入沙箱的前提下守住边界的组合。
- 数据:`Pages/Layout/Widgets/DangerousWidgets.cs`、`WidgetCommon.cs`、`WidgetUI.cs`;`LayoutWidgetTests` 44 例含「危险类型默认关闭」断言;截图 `artifacts/l6/tasks-widgets-dangerous-dark.png`。
- 可回滚:是(关掉即回默认不渲染)。
- 偏离提示:白名单是**产品决定的安全边界**,不要为了「更自由」放开 `file://` 或任意命令。

### D-31 · 插件 UI 贡献新增 kind `block`(三处白名单必须同步)

- 时间:2026-11-14
- 处境:插件原本只能贡献 `page / aiView / dashCard / overlay / settings / action`,没有任何「区块」级贡献点;而布局 v2 的块 id 命名空间已定 `plugin:<plugin>:<blockId>`(与老画布的 `plugin:<plugin>:<cardId>`、用户自建区块 `custom:<widgetId>` 区分)。
- 决定:新增 UI 贡献 kind **`block`**,渲染语义与 `dashCard` 一致(WebView 或原生 WPF);**三处白名单必须同值** —— `Core/Plugins/PluginManifest.cs:77`、`Core/Plugins/PluginHostApi.cs:232`、`Core/Plugins/PluginManager.cs:1259`(当前均为 `page / aiView / dashCard / block / overlay / settings / action`);`PluginUi.CreateBlock` 负责装配;插件卸载/禁用后区块保留并显示「不可用」。
- 理由:三处白名单历史上就是「改一处必须同步三处」的坑(设计 §5.2 明确标注),写进决策避免下次漏改。
- 数据:`Pages/Layout/Widgets/PluginBlocks.cs`、`Pages/Plugins/PluginUi.cs:251`;`LayoutWidgetTests` 44 例;夹具 `artifacts/l6/widget-fixture.ui_layout_v2.json`。
- 可回滚:是(删 `block` 一行即回到旧白名单,但已保存的区块会显示「不可用」)。
- 偏离提示:kind `block` 只对**布局 v2** 生效;老卡片画布 `dashCard` 链路按 D-2/§5.3 保持冻结。

### D-32 · Core→Pages 依赖方向债务:移植 mac/linux 前必须先把 4 个纯模型文件移进 Core

- 时间:2026-11-14
- 处境:布局 v2 的 4 个**纯模型层**文件(`LayoutDocV2.cs` / `LayoutSchema.cs` / `LayoutStoreV2.cs` / `LayoutJson.cs`,禁 WPF、只用 BCL 与 `TipCL.Core.Launcher.Utils`)按 L0 的任务书放在了 `src/TipCL/Pages/Layout/` 下。这形成 **Pages → Core** 的依赖方向,而 mac/linux 端口(`mac/`、`linux/` 树)复用的是 Core;若直接移植,模型层会被锁在 WPF 侧。
- 决定:接受本轮的目录现状(**不在包内搬迁**,避免与并发成员撞文件),但记为**方向债务**:移植 mac/linux 前,必须先把这 4 个文件整体移到 `src/TipCL/Core/Launcher/Layout/`(命名空间与 `using` 同步调整),Pages 侧只留 WPF 相关的 `LayoutEngine / LayoutEditor / GridCanvasPanel / GuideLayer / PageLayoutHost / WidgetRegistry / ShellTabs`。
- 理由:模型层本就无 WPF 依赖(现由 net8 测试宿主直接加载,就是证据),搬进 Core 后端口能零成本复用契约与迁移/写通逻辑;但现在搬会与正在改这些文件的成员冲突。
- 数据:4 个文件当前位于 `Pages/Layout/`(`LayoutDocV2.cs` 38 KB、`LayoutSchema.cs` 21 KB、`LayoutStoreV2.cs` 12 KB、`LayoutJson.cs` 8 KB);跨端复用面见设计 §3.3 的兼容矩阵。
- 可回滚:是(搬迁是纯机械移动 + 改命名空间)。
- 偏离提示:这是**已知的方向反了**,不是设计意图;下一轮做移植时必须先还这笔债,否则 mac/linux 端口要么复制一份模型、要么反向依赖 WPF。

### D-33 · i18n 词表按「审计差集」补齐,不采信转述清单

- 时间:2026-11-14(D2 包)
- 处境:设计收尾时多个成员各自报过新增串条数(L5 报 108、L2 报 11、L1 报 1),加起来与真实缺口对不上;而 `--i18n-check` 在本会话环境跑不起来(见 PROGRESS 第八轮的「环境事实」)。
- 决定:a) 补齐一律**用脚本重抓**布局相关文件的 `I18n.L(...)` / `I18nCore.Tr(...)` 中文字面量,与 `en.json` + `zh_CN.json` 求差集,以差集为准;b) 规则:`key = 中文原文`、`zh_CN` 恒等自映射、`en` 人工翻译、保留 `{0}` / `{0:P0}` 占位符、**既有键的值一个都不许改**、按文件既有格式(顶部插入新键、1 空格缩进、LF、UTF-8 无 BOM)逐行文本插入;c) 顺带补 HEAD 就缺的 `自定义布局`;d) 复核必须给数字:JSON 合法、两表键集合一致、既有行逐行原样保留、**差集归零**。
- 数据:扫出 557 条去重 key、缺 **178** 条;`en.json` 2774 → 2952(+178),`zh_CN.json` 2773 → 2952(+179);差集 en 0 / zh 0。
- 顺带发现(记录不修):1 两处**既有近似重复键**只差标点宽度(`(` U+0028 vs `（` U+FF08、`,` U+002C vs `，` U+FF0C),会让源码字面量在 en 下查不到译文;2 两个文件**既有**键顺序自第 365 键起分叉(集合一致),本次未动既有顺序。
- 可回滚:是(词表为纯新增,删掉新增行即回原状)。
- 偏离提示:补的是「源码字面量的原样形式」,因此那两处近似重复会**同时存在两个 key** —— 这是刻意的(改 .cs 字面量或改既有 key 都会动到别人的文件/既有译文,留给维护者定)。

### D-34 · 仓库文本一律 LF:`Set-Content` / `WriteAllLines` 会整文件翻 CRLF,写完必须核对 numstat 与 CRLF 计数

- 时间:2026-11-14
- 处境:本轮两次改仓库文本(词表增补、工程记录)时用了 PowerShell 的 `Set-Content` 与 `[IO.File]::WriteAllLines`。这两个调用**按平台默认换行写整个文件**——只想插几行,却会把文件从 LF 整体翻成 CRLF,`git diff` 立刻从"插入 N 行"变成"整文件重写",真实 diff、blame 与后续合并全部被冲掉(本轮实际踩到两次,都在写完后才发现)。
- 决定:仓库里所有文本文件(源码、词表、`*.md`)**必须是 LF**。改文件只用两条路:1) 逐行文本插入(读全文 → 拼新内容 → `WriteAllText(path, text, UTF8Encoding(false))`,换行沿用 `ReadAllText` 探测到的 `\r\n` / `\n`);2) 编辑器工具。**每次写完必须核对两件事**:`git diff --numstat -- <file>` 的增删行数是否等于预期(出现"整文件级数字"就是被翻过),以及 CRLF 计数 `([regex]::Matches($text, "`+"`r`n"+`")).Count` 必须为 0。
- 理由:仓库是 mac/linux 端口共用的基线(LF 是唯一不会互相污染的选择);而被翻过的 diff 无法人工复核,等价于把改动藏起来。
- 数据:本轮 `PROGRESS.md`(已提交版本 214 行)/`DECISIONS.md`(274 行)都实测 CRLF=0;`Set-Content` 对它们会产出 214/274 行全量重写。
- 可回滚:是(重新以 LF 写回即可),但已提交的"整文件重写"只能靠 revert 修。
- 偏离提示:如果将来引入 `core.autocrlf` / `.gitattributes`,先在这里补一条决策;当前仓库**没有**换行属性文件,全靠人工纪律。

### D-35 · 联机页的截图回归必须先归一化局域网地址枚举顺序,否则假报警

- 时间:2026-11-14
- 处境:默认渲染等价的截图回归(逐字节判等)里,联机页(`multiplayer`)两张 PNG 总是有差异,乍看像布局回归。实际差异只在**局域网设备列表**:该页会枚举本机所在网段的局域网地址,枚举顺序/内容随机器状态(网卡、在线设备)变化,与布局改动无关。
- 决定:对含动态内容的页做截图判等时,先**归一化再比**:把动态区(局域网地址文本块)按行排序或整体抹平后再比较,或者在比对脚本里只比"动态区之外"的区域;并把这条写进回归脚本的说明,避免下一轮又把它当回归排查。
- 理由:逐字节判等是最强的默认等价证据,但它的前提是"同一输入";动态内容必须先固定或排除,否则会为了假报警去改真代码。
- 数据:联机页差异集中在局域网地址区;其余站点(launch/library/download/settings/account 等)在改动前后 SHA256 相同。实现见 `artifacts/` 下的比对脚本与 D-26 的口径。
- 可回滚:是(纯判等方式,不动产品代码)。
- 偏离提示:不要为了"让截图一致"去固定或伪造局域网枚举结果 —— 那是改功能来迁就测试。

### D-36 · 体积回收(D-28)本轮结论:写范围内没有一个"可证明安全"的候选,维持不改

- 时间:2026-11-14(P2 包)
- 处境:compact 6,443,520 B 超出 D-19 红线 5,757,264 B 共 **686,256 B**(非布局功能造成,D-28 已按 D-11 先例豁免);P2 要求"在不动功能的前提下能收则收"。硬约束:不破坏 Win7/8.1、不加新 NuGet、不删 WebP 能力(D-20)、不削弱代码页解码。
- 决定:**本轮不改任何资源/工程配置**。实测当前 exe 内嵌资源 **35 条 / 3,157,337 B**,逐个候选判断后,两个大头(ImageSharp 628,416 + CodePages 527,029 = **1,155,445 B**)被硬约束直接禁止;其余能在写范围内动的(locales / model_rules / plugin-host 压缩)都要改**资源释放路径与 Python 侧读取**,超出 P2 写范围;图片类(hero.jpg 275,898 B、4 张预设壁纸 77,129 B)与架构 loader(arm64 147,520 / x86 120,944)属"降质或砍架构",不是可证明安全的回收。
- 理由:回收的收益(≤ 0.4 MB)远小于"静默改掉 I18n / 插件宿主读取路径"或"砍掉 32 位与 ARM64 支持"的风险;而红线已有 D-28 豁免,不影响本轮交付。**"不改 + 给数字与前置条件"是合格交付。**
- 数据(2026-11-14 实测,compact exe 6,443,520 B):内嵌资源 top:ImageSharp 628,416、CodePages 527,029、`TipCL.g.resources` 403,990(其中 `premiumassets/hero.jpg` **275,898**、BAML+图标字体 ≈ 60,206、4 张壁纸 77,129)、System.Text.Json 226,464、locales en 196,506 + zh 189,174(合计 385,680)、WebView2 loader x64 159,344 / arm64 147,520 / x86 120,944、WebView2 core 141,114 + wpf 39,429 + winforms 20,199、System.Memory 60,947、model_rules 39,748、plugin-host 脚本 94,048、Encodings.Web 35,106、Numerics.Vectors 33,140、其余 ~60 KB。合计 3,157,337 B。
- 可回滚:无需回滚(未改一个字节)。
- 偏离提示(要在正常机器上真正回收,必须先解决这些):
  1. **把资源释放路径纳入范围**:`Core/Services/Payload.cs`(把内嵌资源解到数据目录给 Python 读)不在本包写范围;locales / ai/model_rules / plugin-host 三类 JSON+mjs 压缩(实测可省 ≈ 390 KB)必须"构建期 gzip + Payload 解压 + Python 侧照读"三处一起改,还要保证 mac/linux 端口同步。
  2. **图片降质需要用户拍板**:`PremiumAssets/hero.jpg` 单文件 275,898 B(占 `g.resources` 68%),4 张预设壁纸 77,129 B;压它们 = 用户可见的画质变化,必须先拿到"允许降质到什么程度"的结论,并给逐图对照。
  3. **ImageSharp + CodePages(1.16 MB)与硬约束冲突**:D-20 要求保留 WebP 能力、E1-E5 要求不削弱代码页解码;若要动,必须先由用户重新裁决 D-19/D-20(例如允许 compact 侧只留 WebP 解码器)。
  4. **架构 loader 裁剪 = 功能裁剪**:arm64(147,520)+ x86(120,944)是 32 位与 ARM64 Windows 上 WebView2 的运行时依赖,砍掉等于放弃这两类机器(Win7/8.1 有大量 32 位),按本仓既有纪律属"须报备的裁剪",默认不做。
  5. **WebView2 WinForms(20,199)**:D-8 已试过 `ExcludeAssemblies` 与从 ReferencePath 移除,均未生效;要收益只能换 `ResolvePackageAssets` 思路,20 KB 收益 < 破坏 Costura 单包的风险。

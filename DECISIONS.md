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

# PROGRESS（工作包总表）

每个工作包的详细记录在 `progress/WP-X.md`（子代理各写各的，避免并发冲突）。主 agent 汇总到这里并负责 git 提交。

| 包 | 内容 | 主要文件（独占） | 状态 |
|---|---|---|---|
| WP-A | 窗口外壳：Win10 圆角(SetWindowRgn)、矢量窗口按钮、标题栏（5 页签含插件、AI 图标、头像、C 桥仅断开显示）、全局圆角/卡片样式 token | MainWindow.xaml(.cs)、Controls.xaml 及主题资源字典、Services/Backdrop.cs | 完成待验（未编译）。含 download 接线。资源键清单见 progress/WP-A.md。遗留：启动页背景铺到标题栏下；设置子页时“设置”页签不亮；Win10 Region 圆角锯齿/缩放闪烁需实测 |
| WP-B | 启动页 v2：左对齐、截图背景+渐变、去 HeroGrid/光晕/胶囊、底部新闻卡 | Pages/LaunchPage.cs | 完成待验（未编译）。详见 progress/WP-B.md；LaunchBackground.cs 已并入 LaunchPage.LoadShot。遗留：标题栏延伸到截图上→WP-A；B.HeroGrid/HeroGlow 键可删→WP-F；删抽屉时保留 multiplayer NavDef |
| WP-C | 下载页 v2 + 新安装页（加载器卡片、版本下拉、Fabric API、OptiFine、底部汇总）+ 加载器官方图标运行时下载缓存 | Pages/Download*、新建安装页文件、新建图标缓存服务 | 完成待验（未编译）。新建 DownloadPage/InstallPage/LoaderIcons，删 VersionPage 旧安装 UI、TasksPage HubRow。**依赖 WP-A 在 MainWindow 接 download NavDef（同版合入）** 及 WP-A 新键 Seg.Rail/Seg.Item/B.Card/B.CardBorder/B.RowLine/B.RowHover。详见 progress/WP-C.md |
| WP-D | 插件页卡片化（已安装/市场分段，⋯ 收开发与市场源）+ 设置页左导航重排 | Pages/Plugins/*、Pages/SettingsPage.cs | 未开始 |
| WP-E | 实例详情页（分段 模组/存档/资源包/光影/设置，模组行开关），实例库“管理”进入 | 实例相关页面 | 未开始 |
| WP-F | 收尾：删“更多”抽屉、统一圆角、全局一致性审查、DECISIONS 追加 | 全局 | 未开始 |

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

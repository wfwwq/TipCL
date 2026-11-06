# PROGRESS（工作包总表）

每个工作包的详细记录在 `progress/WP-X.md`（子代理各写各的，避免并发冲突）。主 agent 汇总到这里并负责 git 提交。

| 包 | 内容 | 主要文件（独占） | 状态 |
|---|---|---|---|
| WP-A | 窗口外壳：Win10 圆角(SetWindowRgn)、矢量窗口按钮、标题栏（5 页签含插件、AI 图标、头像、C 桥仅断开显示）、全局圆角/卡片样式 token | MainWindow.xaml(.cs)、Controls.xaml 及主题资源字典、Services/Backdrop.cs | 未开始 |
| WP-B | 启动页 v2：左对齐、截图背景+渐变、去 HeroGrid/光晕/胶囊、底部新闻卡 | Pages/LaunchPage.cs | 未开始 |
| WP-C | 下载页 v2 + 新安装页（加载器卡片、版本下拉、Fabric API、OptiFine、底部汇总）+ 加载器官方图标运行时下载缓存 | Pages/Download*、新建安装页文件、新建图标缓存服务 | 未开始 |
| WP-D | 插件页卡片化（已安装/市场分段，⋯ 收开发与市场源）+ 设置页左导航重排 | Pages/Plugins/*、Pages/SettingsPage.cs | 未开始 |
| WP-E | 实例详情页（分段 模组/存档/资源包/光影/设置，模组行开关），实例库“管理”进入 | 实例相关页面 | 未开始 |
| WP-F | 收尾：删“更多”抽屉、统一圆角、全局一致性审查、DECISIONS 追加 | 全局 | 未开始 |

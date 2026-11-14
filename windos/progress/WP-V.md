# WP-V 四页贴图实修（2026-11-07，完成，Windows 编译+截图验证通过）

基于 99aec85（WP-U 后）。多代理并行共享树，构建多次撞他人瞬态语法错误，轮询重试通过。

## 逐页对照结论（效果图 docs/mockups-hark/v2 vs _wph_run/shots 旧截图）
- **实例库 library2**：3 处硬差异已修（见下）。其余（圆点+名称、元信息一行、启动+管理、页内切详情）原实现已贴图。
- **下载 download2**：代码核对全部已贴图——灰轨道 7 分段、任务 N **已接 TaskStore.Added/Updated 实时刷新**（DownloadPage.cs 构造器订阅 + SyncTasks，OnShown/Load 也同步）、搜索+三筛选（选中 Btn.Soft 粗体）、白卡列表行高 46 行尾安装（悬停 Btn.Soft→Btn.Primary 实心）。零改动。
- **插件 plugins2**：代码核对已贴图——Seg.Rail 已安装/市场、从文件安装+⋯、卡片（36×36 logo 方块/名称/开关/描述两行高36/版本·开发链接/状态·开发·本地·.NET·有更新标签/⋯）。截图环境无插件只验了分段与头部，卡片结构经代码比对与 WP-D 记录一致。零改动。
- **设置 settings2**：截图已贴图——左导航 7 项（选中白底胶囊+粗体）、右侧分组卡片、组标题灰小字在卡片外（T.GroupTitle）。零改动。

## 改动文件
| 文件 | 内容 |
|---|---|
| `src/TipCL/Pages/LibraryPage.cs` | ① 标题 T.H1 上覆盖 FontSize=21/ExtraBold（=T.PageTitle 规格，效果图 21/800）；② 圆点改为**每张卡都有**（当前实例 B.Accent 红、其余 B.InkFaint 灰，效果图 .dot 规则）；③ 卡片 B.Paper/B.Line/圆角10 → B.Card/B.CardBorder/圆角12（v2 Card 规格）。 |
| `src/TipCL/Themes/Controls.xaml` | **体积回收**（按 WP-A 记录的候选，grep 确认无引用）：删 `Card.Shadow` 样式（全仓库零引用）；删 `Seg.Item` 选中态 DropShadowEffect（保留白底+粗体，视觉差异极小）。提交前 exe 超 512 B，删后回到红线内。 |

## 验证
| 项 | 值 |
|---|---|
| compact 构建 | 0 错误 |
| compact TipCL.exe | **5,707,264 B = 红线**（基线 5,706,240 +1,024；含其他在飞代理对 InstallPage/SettingsPage 的改动。我自己的改动约 +1KB 量子，靠删 Card.Shadow/Seg.Item 阴影抵回） |
| premium 构建 | 0 错误，6,002,688 B（无上限，仅记录） |
| 测试 | **127/128**（唯一失败 PhoneLinkTests.FullFlow，基线既有） |
| 截图 | 8 张 `D:\项目\tipcl\windos\_wph_run\wpv\compact-{library,download,plugins,settings}-{dark,light}.png`，逐页与效果图对照通过（选中分段无阴影后视觉仍贴图） |

## 遗留
- compact 余量 **0 B**：后续任何代理再增字节必须先等量删减。下一个回收候选：Seg.Item 选中阴影已删尽；再超需动 MainWindow 或功能面（需监工决策）。
- 实例库卡片无悬停效果（效果图 .card 也无 hover，不算差异）。
- 插件卡片视觉效果只做了代码级核对（截图环境无已装插件），装插件后可复验。

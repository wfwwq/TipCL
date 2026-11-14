# WP-N premium 完整化（Fabric API 版本号 / 模组行版本 / 清单核查）—— 完成待验（2026-11-07，双版编译+测试+--shot 通过）

基于 master 853a296（WP-X 后）。改动全部在 `#if PREMIUM` 内，compact exe 字节不变。

## 清单结果
| # | 项 | 结果 |
|---|---|---|
| 1 | Fabric API 具体版本号 | **做成**。InstallPage premium：Fabric API 行名称下加 11.5px 灰字版本号（如 0.92.2+1.21.1）。数据走既有 Modrinth 通道 `list_catalog_files`（extra={name:"Fabric API", source:"Modrinth", slug:"fabric-api", game_version:<MC版本>}，CatalogFiles._list_mr → Mods.ListVersions 已按 game_version 过滤），取第一个 release_type=release 的 `version_number`（无 release 则第一个）；失败/离线 `TryCallAsync` 回退空 → 静默不显示。compact 不发请求（该行保持原提示文案「大多数 Fabric 模组都需要」，premium 用版本号替代提示行）。 |
| 2 | 安装页路径提示 | **已在 #if PREMIUM 内**（WP-X 实现：`_pathHint` 声明、body 插入、`SyncPath` 全在 PREMIUM 分支），未动。 |
| 3 | 加载器卡视觉 | **跳过**。逐项对照 install2.html CSS：`.li` 36×36 圆角 9（一致，WP-X 已核）；勾标 margin (0,9,10,0)（一致）；卡底/描边/选中 B.AccentTint（对应 .ld.on #fffafa+主题描边，一致）。效果图的彩色浅底（#f1f5ee 等）在 premium 不可见——我们的图标是满铺 GitHub 头像（非透明底 logo），底色永远被盖住，加了也无效，故不做。 |
| 4 | 模组行增强 | **降级为只显示版本**。ModEntry（Models/ModEntry.cs）字段：Filename/Name/Enabled/Version/Size，**无作者字段**，后端 `get_installed_mod_entries` 不返回作者 → 作者做不了。premium 模组行副行改为「版本 · 文件名」（有版本才带版本前缀），compact 保持纯文件名。 |

## 改动文件
| 文件 | 改动 |
|---|---|
| `src/TipCL/Pages/InstallPage.cs` | ① `_apiVer` 字段（PREMIUM）；② Fabric API 行 premium 自建 46 高行（名称+版本竖排，compact 原样）；③ `LoadAsync` 尾部 premium 触发 `LoadFabricApiVersionAsync()`（异步，不阻塞页面）。约 +30 行。 |
| `src/TipCL/Pages/InstanceDetailPage.cs` | 模组行 sub premium/compact 分支，约 +6 行。 |
| `src/TipCL/Services/LoaderIcons.cs`、`TipCL.csproj` | 未改（无需新素材）。 |

## 验证数据（2026-11-07，Windows）
| 项 | compact | premium |
|---|---|---|
| 构建（-m:1 全防锁参数） | 0 错误 | 0 错误 |
| TipCL.exe | **5,707,264 B**（与 WP-X 验收值字节一致，D-19 红线 5,757,264 内） | **6,003,712 B**（无上限，仅记录） |
| 测试 | **127/128**（唯一失败 PhoneLinkTests.FullFlow，基线既有，未碰） | 同一次跑出（测试工程不受 Premium 影响） |

## --shot（premium exe，离屏）
- `--shot launch --theme dark` → `_wph_run/wpn/launch_dark.png`：启动页正常——标题栏 5 页签、AI/主题/账号钮、实例状态行、主按钮组、底部新闻条均未碰坏（shot 环境无截图/hero 条件，走渐变底，正常）。

## 遗留
- Fabric API 版本号依赖网络；离线时该行只有名称无版本（静默降级，符合要求）。
- 模组行作者显示：后端无字段，需 Core 侧支持才能做（超出本包权限）。
- 真窗对照 install2.png（含 Fabric API 版本行）留监工 --shot install 流程验证（安装页需经下载页原版列表进入，DevShot 是否支持 pageId=install 未验证）。

# WP-I premium 版自动化验收 + 交付 —— 完成（2026-11-07，Windows）

验收对象：监工预构建 premium exe（HEAD 源码 + `-p:Premium=true`，构建 0 错误）。
产物目录 `_wph_run\`（仓库外），本包未改任何源码、未做 git 操作。

## 1. --smoke 全走查（T1）

命令：`_wph_run\premium\TipCL.exe --smoke --smoke-timeout 600`，输出留档 `_wph_run\premium-smoke.log`，
canonical 报告 `C:\Users\Administrator\AppData\Local\TipCL\_wpf_smoke\run-20261107-091828\report.json`（含 22 张页面截图与弹窗截图）。

| 项 | 值 | 阈值 | 判定 |
|---|---|---|---|
| 退出码 / 状态 | 0 / finished（elapsed 50,231 ms） | — | 通过 |
| Bridge | 已连上 | — | 通过 |
| 访问页面 | **22/22 全 ok**（launch, library, instance, version, mod, mods, modpack, datapack, resourcepack, shader, world, account, java, ai, multiplayer, servers, playtime, feedback, settings, tasks, download, plugins） | 全量 | 通过 |
| 真实点击控件 | **44/44 ok**（≥20 达标；每页至多 4 个白名单安全按钮） | ≥20 | 通过 |
| 真实 WPF 弹窗自动应答 | **19 个**（≥10 达标）+ 9 个系统文件框模拟取消，全部按取消/关闭收场 | ≥10 | 通过 |
| issues / 异常 / 错误 Toast 记录 | **0** | 0 | 通过 |
| i18n 缺词 | 0（zh_CN） | — | 通过 |

- 点击清单（摘）：新建实例、导入官方启动器、刷新、版本设置、链接安装、导入本地文件、检查更新（mod 3,520ms / mods 1,345ms 在预算内完成）、统一通行证、皮肤站登录、添加离线账号、扫描系统 Java、新对话、导入、查看、查看系统信息（4,864ms）、保存为主题包、加载、重新运行 等；无「到整轮结束仍未收场」的挂起操作。
- 弹窗清单（摘）：新建实例、版本设置、各 Catalog 页链接安装/导入、模组更新提示「1 个模组有更新」、三种登录框、系统信息、保存主题包、「欢迎使用 TipCL」首启欢迎框——全部为预期业务弹窗，无异常报错弹窗。
- **豁免项核对**：无网络/未登录类友好业务提示出现在 issues（issues 为空），无需豁免判定。
- **flake 判定**：全部通过且 issues=0，无需重跑复验；共享 %LOCALAPPDATA%\TipCL 下与其它并行实例（WP-H 同期在跑 compact/premium 截图）未发生配置冲突，未触发 30 秒重试预案。
- 间接覆盖：smoke 从 launch 依次导航到其余 21 页，等价实跑了 WP-G 的沉浸态进入/还原路径（OnHidden → SyncImmersive），0 异常。

## 2. --shot 视觉复核（T2）

| 截图 | 结论 |
|---|---|
| `_wph_run\shots\premium-launch-dark.png`（760,091 B） | **符合 WP-G 描述**：hero.jpg 全景背景在（雪山/湖面/森林全景，非纯渐变底）；背景铺到标题栏下，TopBar 为低透明度深色玻璃 scrim + 白字（TipCL 标题、五个页签、通知徽标 1、窗口钮均白）；页签选中红下划线（跟随主题色，WP-G 刻意取舍）；左侧深渐变上「就绪 · E:\minecraft / .minecraft / Java·自动 / 4 GB / 启动游戏 / 联机 / 新闻条」均可读。 |
| `_wph_run\shots\premium-launch-light.png`（766,851 B） | **符合 WP-G 描述，无深色残留**：沉浸态两主题本就同款 hero+玻璃白字（WP-G 设计），主题切换图标由太阳变为月亮证明主题真实生效；无深色实底面板/深色 Ink 残留（「TipCL」标题为白，TextElement.Foreground 修复有效）。 |

复核方式：Read 目检两图，并对照 WP-G §「--shot 冒烟」描述逐条核对。

## 3. 交付产物（T3）

| 项 | 值 |
|---|---|
| 路径 | `D:\项目\tipcl\windos\dist\TipCL-premium.exe` |
| 字节 | **5,999,616 B** |
| SHA256 | `4e1d45dd3e6a7b5e54c1b58e1374782a31dc7551ff060be05daec6f8428ea220` |
| 一致性 | 与 `_wph_run\premium\TipCL.exe` SHA256 逐字节一致 |

备注：WP-G 记录的 premium exe 为 6,000,128 B，本次预构建副本为 5,999,616 B（-512 B）——监工在 WP-G 记录之后重编过（HEAD 未变），属正常差异；premium 无体积红线，仅记录。

## 4. 发现的问题与建议修复 diff

**无。** 本轮 smoke 全绿、截图全符合，未发现需要修复的缺陷，无 diff 提出。

## 5. 覆盖缺口与风险

1. **launch 页与 multiplayer/tasks/download/plugins 页点击数为 0**：这些页面无白名单安全按钮（launch 只有启动游戏——被 NeverClick 排除），属冒烟器设计使然，非缺陷；对应交互（启动游戏、联机大厅等）仍靠人工验收覆盖（GOAL §6 手工清单）。
2. **微软正版登录**按冒烟规则跳过（report.json `skipped` 字段已声明），未覆盖。
3. **未复验 library 页沉浸还原截图**（WP-G 已验过且本轮 smoke 导航 0 异常间接覆盖）；如需留档可补 `--shot library`。
4. `--shot`/`--smoke` 复核的是**自检渲染路径**（离屏窗口、ShowActivated=false），与真实前台窗口的 DWM 玻璃/命中测试存在理论差异；WP-G 已在真实窗口验证过沉浸实现，风险低。
5. 共享数据目录 `%LOCALAPPDATA%\TipCL` 无隔离：本轮与 WP-H 并行未互踩，但**后续并行验收仍建议错峰**或按监工安排串行，避免 smoke 的 `_wpf_smoke` 运行目录与配置写入互相干扰。
6. 产物 `_wph_run\premium-smoke.log` 中 JSON 段的中文经控制台 GBK 编码转写，个别字显示为乱码——canonical 数据以 `report.json`（UTF-8）为准，日志仅作留档。

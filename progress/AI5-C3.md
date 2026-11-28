# AI5-C3 · 工具选择:数据报告与方案 1b 实施记录

日期:2026-11-28
分支:`ai5/linux-tools`(worktree `C:\tipcl-ai5\linux-tools`,基于 `b4cd7a2`)
状态:**用户已裁决「方案 1 + 1b 档(只补关键词)」,本包已实施**;「33 个裸写工具」按裁决另立包、本轮不做;回放探针已入库到 `tools/ai-tool-selection-probe`。
本文所有「1b 前」= commit `d78bd54`(C1 已落地、关键词未补),「1b 后」= 本提交。数字都可用入库探针跑出来(见 §8)。

---

## 0. 结论摘要

| # | 结论 | 1b 前 | 1b 后 |
|---|---|---|---|
| 1 | **真实 Trace 不可用**:全机 `ai_trace` 目录 110 个,全部来自自检 / 单元测试的临时 `TIPCL_HOME`,没有一条真实用户回合;Linux 侧当时也还没有 `tool_schema_miss` 事件(C2 未实施)。改用**离线回放 + 人工标注** | 可用真实回合 **0** | 同左 |
| 2 | **漏选率(典型请求集)**,每百回合 | **17.9**(7/39) | **0.0**(0/39) |
| 3 | **漏选率(口语变体集)**,每百回合 | **41.7**(5/12) | **0.0**(0/12) |
| 4 | **漏选率(合并 51 条有标注)**,每百回合 | **23.5**(12/51) | **0.0**(0/51) |
| 5 | **tools token / 轮**(典型 / 变体) | 5150.8 / 4967.4 | **5188.0(+0.7%)** / **5073.5(+2.1%)** —— 都远在「不超过现状 +10%」的验收线内 |
| 6 | **一次漏选的代价**(C2 用全量表重发一次) | ≈ 10.1k tools + 3.0k 上下文 ≈ **1.3 万 token / 次**;典型集折算 **23.5 万 token/百回合** | **0**(这一集不再漏) |
| 7 | **关键词路线的天花板**:对抗性留出集(故意用表外说法、不参与补词) | 60.0(6/10) | **50.0**(5/10)—— 换个没补过的说法照样漏,这是方案 2(目录 + `find_tools`)当初上桌的理由 |
| 8 | **附带发现(裁决:另立包,本轮不做)**:现状 token 的大头是 `ACTION_RE` 命中就把 **33 个写工具全带上**,13/40 条请求平均多带 7.1k token;收掉这一支 tools token/轮 **−44.5%** | — | 未实施 |

一句话:**方案 1b 把量出来的漏选全消掉了,代价是 tools token +0.7%~+2.1%**(不到预算线的四分之一);长尾仍在(留出集 50/百回合),要根治得靠方案 2 或「补更多说法」的长期投入。

---

## 1. 数据来源与限制(必读)

### 1.1 真实 Trace 不够用

- `Trace.Record` 把每轮轨迹写在 `$TIPCL_HOME/cache/ai_trace/YYYYMMDD.jsonl`,保留 14 天。
- 本机实测:全盘只找到 **110 个 `ai_trace` 目录**,全部位于 `%TEMP%\tipcl-tests-*` / `%TEMP%\tipcl-selftest-*` 这类临时 `TIPCL_HOME` 下,共 373,004 B / 110 个 jsonl,**没有一条真实用户回合**。
- 开工时本分支 `grep -r "tool_schema_miss"` 零命中(那是 C2 包的内容,还没做);Windows 树同理没有真实分布数据。

→ 「`tool_schema_miss` 的出现率(每百回合)」只能给**代理指标**,口径见 §1.2。任务书也允许这么做(「真实数据不够时,用一份 30-50 条的典型请求集离线回放」)。

### 1.2 代理指标的定义

- **一轮** = 一次用户请求(一次 `Tools.SelectToolSchemas` 调用对应的那一回合)。
- 每条请求人工标注「本轮必须能被调用的工具集 `ideal`」(依据:`AiData.SYSTEM_PROMPT` 写明的流程 + 工具语义;多轮请求标的是**最后一轮**)。
- **漏选** = `ideal` 里有工具不在本轮下发列表里 —— 这正是 C2 要抓的 `tool_schema_miss` 的成因:模型要调它却没有声明 → 用全量表重发一次。
- **漏选率(每百回合)** = 有漏选的请求数 ÷ 有标注的请求数 × 100。
- **token** = 项目自己的估算口径(`Tokens.EstimateText`:CJK 1 / 其余 0.25),对每个 tool schema 的 JSON 序列化求和;**报的是 tools 部分**,不含系统提示与历史。
  - 同一口径下:系统提示 2955、`CORE_TOOLS` 18 个 2243、全量 78 个 13770 —— 与 a1 报告记的「系统提示约 5.0k / 18 个常驻工具约 2.1k / 78 个全发约 12.0k」同量级(前两项差异来自两套估算口径,不影响本文内部的相对比较)。
  - 这是**估算,不是计费真值**。
- **往返成本**:流式接口每多发一轮请求都要重发「系统提示 + 全部历史 + tools」。按「系统提示 + 消息」实测 ≈ 2982 token/轮。provider 有 prompt caching 时这一项会便宜很多(§4.3 有敏感度)。

### 1.3 请求集的构造原则

- **典型请求集 40 条**:覆盖装模组 / 光影 / 材质 / 数据包 / 地图、启动排错、卡顿优化、存档备份还原、版本升级、整合包造包导出、服务器、联机、账号皮肤、布局、配置、日志文件,加 4 条多轮续话(场景 19 的变体)与 1 条无标注对照(「你好」)。
- **口语变体集 12 条**:同一批意图换常见口语说法,量「漏选率对说法有多敏感」。
- **关键词补丁集 10 条**:1b **针对**的说法(含 SH-1 场景 18a 的「好卡」)。**它是「因」不是「果」**,补完必然 0 漏选,不能拿来证明真实覆盖率。
- **对抗性留出集 10 条**:**故意**用关键词表里没有的说法,且**不参与补词**。它只量一件事:方案 1 还剩多少长尾。
- 多轮请求按真实形状装配历史:`system` → 上一轮 `user` → `assistant` → `tool`(带一段像真实工具回填的 JSON)→ 本轮 `user`。工具结果文本也进关键词窗口(C1 之后如此,与 Windows 一致)。

---

## 2. 请求集与逐条结果

`下发数` = 本轮下发的工具个数;`tools` = 该轮 tools 部分的估算 token。
「漏选(老口径)」列 = 把关键词匹配退回「只看最后一条用户消息」时的漏选 —— 用来对照 C1 的作用。

### 2.1 典型请求集(40 条,有标注 39 条)

| id | 场景 | 用户话 | ideal | 漏选 1b 前 | 漏选 1b 后 | 漏选(老口径) | 下发数 | tools |
|---|---|---|---|---|---|---|---|---|
| R01 | 装模组 | 帮我给 1.20.1 的 Fabric 装上钠 | search_mods, ask_user, install_mod | — | — | — | 51 | 9715 |
| R02 | 装光影 | 帮我装个光影包 | search_content, install_shader, ask_user | — | — | — | 53 | 10022 |
| R03 | 按硬件推荐光影 | 我这台电脑跑得动什么光影 | recommend_shaders, ask_user | — | — | — | 21 | 2625 |
| R04 | 换材质包 | 帮我把材质包换成舒服点的 | search_content, install_resourcepack, ask_user | — | — | — | 54 | 10263 |
| R05 | 数据包改配方 | 帮我装个数据包,把钻石配方改简单点 | create_datapack, validate_content | — | — | — | 54 | 10178 |
| R06 | 闪退排错 | 游戏闪退了,帮我看看 | get_launcher_state, diagnose_launch, get_latest_log, get_crash_report | — | — | — | 18 | 2243 |
| R07 | 卡顿优化 | 好卡啊,帧数只有 20 | get_system_info, diagnose_lag, tune_jvm | — | — | — | 21 | 2771 |
| R08 | 内存不够 | 我的内存是不是设小了,帮我调大点 | get_system_info, diagnose_lag, tune_jvm | — | — | — | 21 | 2771 |
| R09 | 禁用模组(命令) | 帮我把这个模组关掉 | disable_mod | —(靠命令分支捎带) | — | — | 51 | 9715 |
| R10 | 禁用模组(提问) | 怎么把这个模组关掉? | disable_mod | **disable_mod** | — | **disable_mod** | 20 | 2384 |
| R11 | 模组冲突 | 小地图和钠冲突了,帮我看看留哪个 | scan_mod_conflicts, inspect_mod, ask_user | — | — | — | 22 | 2551 |
| R12 | 查服务器延迟 | 帮我看下服务器 mc.example.com 的延迟 | ping_server | — | — | — | 22 | 2681 |
| R13 | 加服务器 | 把 mc.hypixel.net 加进多人游戏列表 | add_server | — | — | — | 22 | 2681 |
| R14 | 备份存档 | 帮我备份一下存档再升级 | list_saves, backup_save, plan_instance_upgrade, apply_instance_upgrade | — | — | — | 56 | 10265 |
| R15 | 还原存档 | 把我的生存存档还原到昨天的备份 | list_save_backups, restore_save_backup | — | — | — | 54 | 9951 |
| R16 | 升级到新版本 | 升级到 1.21.1 吧 | plan_instance_upgrade, apply_instance_upgrade, search_versions | — | — | — | 54 | 10109 |
| R17 | 导出整合包 | 帮我把这个整合包导出成 mrpack | check_modpack_distribution, export_modpack | — | — | — | 56 | 10759 |
| R18 | 一句话造包 | 帮我做一个科技风的整合包 | plan_modpack, ask_user, create_instance, install_game, install_mods_batch, scan_mod_conflicts, preflight_check, export_modpack | — | — | — | 26 | 4429 |
| R19 | 读 latest.log | 帮我看看 latest.log 里写了啥 | fs_read, get_latest_log | — | — | — | 21 | 2926 |
| R20 | 打开模组文件夹 | 把模组文件夹打开 | open_folder | — | — | — | 52 | 9868 |
| R21 | 改模组配置 | 帮我把 config 里的 sodium 配置改一下 | list_mod_configs, read_mod_config, write_mod_config | — | — | — | 51 | 9715 |
| R22 | 启动前预检 | 启动前帮我检查一下能不能启动 | preflight_check | — | — | — | 19 | 2448 |
| R23 | 联机开房 | 帮我开个房和朋友一起玩 | multiplayer | — | — | — | 19 | 2422 |
| R24 | 换皮肤 | 我要换个皮肤 | list_accounts, set_skin | — | — | — | 52 | 9798 |
| R25 | 改界面布局 | 布局太乱了,帮我重排一下 | layout_get, layout_apply | — | — | — | 20 | 3069 |
| R26 | 加离线账号 | 我想加个离线账号 | add_offline_account, list_accounts | — | — | — | 22 | 2635 |
| R27 | 导出诊断报告(口语) | 崩溃原因我搞不懂,想发到群里问问别人 | export_diagnostic_report, get_crash_report | **export_diagnostic_report** | — | **export_diagnostic_report** | 19 | 2417 |
| R28 | 查新版本 | 帮我看看有没有新的游戏版本 | search_versions, list_installed_versions | — | — | — | 21 | 2583 |
| R29 | 卸载模组 | 帮我把 JEI 卸了 | delete_mod, disable_mod | **delete_mod, disable_mod** | — | **delete_mod, disable_mod** | 21 | 2447 |
| R30 | 写 KubeJS | 帮我用 KubeJS 写个脚本,玩家进服给提示 | write_kubejs_script, validate_content | — | — | — | 21 | 3473 |
| R31 | 装地图存档 | 帮我装张地图玩玩 | search_worlds, install_world | — | — | — | 20 | 2413 |
| R32 | 缺 Java(口语) | 游戏说找不到运行环境,帮我弄一下 | download_java, get_java_list | **get_java_list** | — | **get_java_list** | 52 | 9757 |
| R33 | 看 jar 信息(口语) | 这个 sodium.jar 装的是哪个版本? | inspect_mod, list_mods | **inspect_mod** | — | **inspect_mod** | 22 | 2654 |
| R34 | 建个文件写说明(口语) | 帮我建个文件写点说明 | fs_write | **fs_write** | — | **fs_write** | 21 | 2926 |
| R35 | 验证内容包 | 我按你说的改了数据包,进游戏没生效 | validate_content | — | — | — | 23 | 3445 |
| R36 | 打招呼(无标注) | 你好 | (无) | — | — | — | 18 | 2243 |
| R37 | 多轮:再来一个 | 帮我装个光影包 → 再来一个 | search_content, install_shader | — | — | **search_content, install_shader** | 21 | 2625 |
| R38 | 多轮:怎么才能装好 | 帮我安装个整合包 → 怎么才能装好呢? | search_modpacks, install_modpack, ask_user | — | — | **search_modpacks** | 26 | 4429 |
| R39 | 多轮:发我一份 | 帮我把这次的崩溃日志导出一下 → 顺便也发我一份 | export_diagnostic_report | **export_diagnostic_report** | — | — | 26 | 4490 |
| R40 | 多轮:光影也来一个 | 装钠 → 那光影也来一个 | search_content, install_shader | — | — | — | 21 | 2625 |

> 「漏选(老口径)」列里 1b 之后只剩 R37 / R38 两条 —— 这正是 **C1(聚合最近 12 条)** 负责的那一半:C1 与 1b 互补,两条都得留着。

### 2.2 口语变体集(12 条)

| id | 场景 | 用户话 | ideal | 漏选 1b 前 | 漏选 1b 后 | 下发数 | tools |
|---|---|---|---|---|---|---|---|
| V01 | 停用模组 | 帮我把那个模组停用一下 | disable_mod | **disable_mod** | — | 20 | 2384 |
| V02 | 别加载了 | 这个模组别加载了,关掉吧 | disable_mod | —(「关掉」命中命令分支) | — | 51 | 9715 |
| V03 | 卸掉材质包 | 帮我把之前那个材质包卸了 | delete_mod, disable_mod | **delete_mod, disable_mod** | — | 26 | 3630 |
| V04 | 日志发给作者 | 想把崩溃日志整理一下发给作者 | export_diagnostic_report | **export_diagnostic_report** | — | 19 | 2417 |
| V05 | 推荐光影(口语) | 我这台电脑装哪个光影包合适 | recommend_shaders, ask_user | —(「光影」命中) | — | 21 | 2625 |
| V06 | java 报错 | 游戏报错说 java 有问题,帮我处理一下 | download_java, get_java_list | —(「java」命中) | — | 20 | 2346 |
| V07 | 这是哪个模组 | 这个 jar 是什么模组 | inspect_mod | **inspect_mod** | — | 19 | 2314 |
| V08 | 写说明文件 | 帮我写个说明文件放到实例目录 | fs_write | **fs_write** | — | 22 | 3079 |
| V09 | 备份存档(口语) | 帮我把存档备份到另一个地方,别丢了 | backup_save, list_saves | — | — | 54 | 9951 |
| V10 | 掉帧查内存 | 帧数掉得厉害,帮我看看是不是内存问题 | get_system_info, diagnose_lag, tune_jvm | — | — | 21 | 2771 |
| V11 | 换皮肤(口语) | 我想换个更好看的皮肤 | list_accounts, set_skin | — | — | 52 | 9798 |
| V12 | 调页面顺序 | 帮我把页面顺序调一下 | layout_get, layout_apply | — | — | 52 | 9852 |

### 2.3 关键词补丁集(10 条:1b 针对的说法)

| id | 场景 | 用户话 | ideal | 漏选 1b 前 | 漏选 1b 后 | 下发数 | tools |
|---|---|---|---|---|---|---|---|
| T301 | 好卡(t-win 场景 18a 原文) | 好卡 | get_system_info, diagnose_lag, tune_jvm | —(Linux 表里本来有「好卡」) | — | 21 | 2771 |
| T302 | 有点卡 | 游戏有点卡,帮我看看 | 同上 | **三个全缺** | — | 21 | 2771 |
| T303 | 一卡一卡的 | 一卡一卡的,怎么调 | 同上 | **三个全缺** | — | 21 | 2771 |
| T304 | 优化模组卸了 | 帮我把优化模组卸了 | delete_mod, disable_mod | **delete_mod, disable_mod** | — | 24 | 2975 |
| T305 | 没有运行环境 | 游戏说没有运行环境,帮我装一个 | download_java, get_java_list | **get_java_list** | — | 52 | 9757 |
| T306 | 这个 jar 里面是什么模组 | 这个 jar 里面是什么模组 | inspect_mod | **inspect_mod** | — | 19 | 2314 |
| T307 | 日志发到群里 | 把这些日志发到群里问问 | export_diagnostic_report, get_latest_log | **export_diagnostic_report** | — | 19 | 2417 |
| T308 | 写个说明文件 | 帮我写个说明文件 | fs_write | **fs_write** | — | 21 | 2926 |
| T309 | 新建个文件 | 新建个文件记点东西 | fs_write | —(「新建」命中命令分支,靠倒写工具捎带) | —(现在关键词直出) | 53 | 10159 |
| T310 | 模组停用了 | 把那个模组停用了 | disable_mod | **disable_mod** | — | 20 | 2384 |

> T309 与 V02 说明一件事:补词前这两条「没漏」其实是**侥幸**——靠命令分支把 33 个写工具全倒进来才通过的。补词后它们变成关键词直出,不再依赖那支霰弹。

### 2.4 对抗性留出集(10 条:故意用表外说法,不参与补词)

| id | 场景 | 用户话 | ideal | 漏选 1b 前 | 漏选 1b 后 | tools |
|---|---|---|---|---|---|---|
| H01 | 把钠关了 | 把钠关了 | disable_mod | **disable_mod** | **disable_mod** | 2243 |
| H02 | 从 mods 里拿掉 | 帮我把 JEI 从 mods 里拿掉 | delete_mod, disable_mod | **delete_mod, disable_mod** | **delete_mod, disable_mod** | 2243 |
| H03 | 老是一卡 | 游戏怎么老是一卡 | get_system_info, diagnose_lag, tune_jvm | **三个全缺** | —(「卡」覆盖到了) | 2771 |
| H04 | 发论坛 | 日志整理一下,我要发论坛 | export_diagnostic_report | **export_diagnostic_report** | **export_diagnostic_report** | 2243 |
| H05 | 模组文件是啥版本 | 这个模组文件是啥版本 | inspect_mod | **inspect_mod** | **inspect_mod** | 2583 |
| H06 | java 版本不对 | java 版本不对,帮我换个 | download_java, get_java_list | —(「java」命中) | — | 9928 |
| H07 | 来份诊断 | 给我来份诊断 | export_diagnostic_report | **export_diagnostic_report** | **export_diagnostic_report** | 2243 |
| H08 | 好卡+电脑配置 | 好卡呀,帮我看下电脑配置 | get_system_info, diagnose_lag, tune_jvm | — | — | 10002 |
| H09 | 别启用了 | 把那个模组先别启用了 | disable_mod | —(「启用」命中) | — | 2384 |
| H10 | 配置项关掉 | 把 config 里的 sodium 项关掉 | write_mod_config | —(命令分支捎带) | — | 9715 |

**留出集 1b 后仍有 5/10 = 50/百回合**,剩下的都是「同义词换法」:关/拿掉/发论坛/模组文件/诊断。

---

## 3. 漏选统计

### 3.1 汇总

| 请求集 | 条数 | 有标注 | 1b 前有漏选 | 每百回合 | 1b 后有漏选 | 每百回合 |
|---|---|---|---|---|---|---|
| 典型请求集 | 40 | 39 | 7 | **17.9** | 0 | **0.0** |
| 口语变体集 | 12 | 12 | 5 | **41.7** | 0 | **0.0** |
| 两集合并 | 52 | 51 | 12 | **23.5** | 0 | **0.0** |
| 关键词补丁集 | 10 | 10 | 8 | 80.0 | 0 | 0.0 |
| 对抗性留出集(不设闸门) | 10 | 10 | 6 | 60.0 | 5 | **50.0** |

### 3.2 1b 前最常漏选的工具(两集 + 补丁集合计)

| 工具 | 次数 | 单条 schema token | 现有关键词(哪里没覆盖) |
|---|---|---|---|
| `disable_mod` | 4 | 73 | 「禁用 / 启用 / disable / enable」;漏的:「停用」「提问式关掉」 |
| `export_diagnostic_report` | 3 | 174 | 「诊断报告 / 导出报告 / 导出日志 / 发给别人 / 求助 / report」;漏的:「发到群里」「发我一份」「发给作者」「导出一下」 |
| `delete_mod` | 2 | 63 | 「删除 / 删掉 / 卸载 / 移除 / delete / uninstall / remove」;漏的:「卸了」「卸掉」 |
| `fs_write` | 2 | 239 | 「latest.log / 日志文件 / 读文件 / 写文件 / 列目录 / 文件内容 / 这个文件 / debug.log」;漏的:「建个文件」「写个说明」 |
| `inspect_mod` | 2 | 71 | 挂在「冲突 / 缺依赖 / 不兼容 / conflict / inspect」这一行;漏的:「这个 jar 是什么模组」 |
| `get_java_list` | 1 | 42 | 「java / 运行时 / JRE」;漏的:「运行环境」 |
| `diagnose_lag` / `get_system_info` / `tune_jvm` | 各 2 | 187 / 100 / 241 | 「卡顿 / 很卡 / 好卡 / 掉帧 / 帧数 / fps / lag / 优化」;漏的:单独一个「卡」 |

### 3.3 根因:关键词表收的是**书面词**

- 典型集 17.9/百回合 vs 口语变体集 41.7/百回合:**同一个意图换个说法就掉工具**。
- 更细的证据:R09「帮我把这个模组关掉」(命令式)没漏、R10「怎么把这个模组关掉?」(提问式)漏了 —— 说明现在只有两条路:**要么关键词命中,要么把 33 个写工具全倒进来**,中间没有台阶。
- 补词前后对照也说明:这不是「少一两个词」,而是「表里的词偏书面」(禁用/卸载/导出报告 vs 停用/卸了/发到群里)。

---

## 4. 单轮请求的 token 影响(决策依据,1b 前口径)

### 4.1 现状基线(1b 前 = commit `d78bd54`)

| 项 | 值 |
|---|---|
| tools token / 轮(典型集) | **5150.8** |
| tools token / 轮(变体集) | 4967.4 |
| 下发工具数(典型集) | 最小 18 / 中位 22 / 最大 56 |
| 其中 ≥50 个工具的请求 | 13/40(单轮 tools ≈ 9.7k token) |
| 全量 78 个工具 / `CORE_TOOLS` 18 个 / 系统提示 | 13770 / 2243 / 2955 |

### 4.2 两方案对单轮请求的 token 影响

口径:tools 部分估算 token / 轮。方案 2 的模型 =「`CORE_TOOLS` 完整 schema 常驻 + 非 CORE 工具的**一行目录**常驻 + `find_tools`/`load_tools` 两个 schema 常驻 + 本轮真正要用的非 CORE 工具按需加载」。

| 方案 | 典型集 | 相对现状 | 变体集 | 相对现状 | 说明 |
|---|---|---|---|---|---|
| **现状(1b 前)** | 5150.8 | — | 4967.4 | — | CORE 2243 + 关键词带出的完整 schema |
| **方案 1a**:漏选过的 6 个工具**并入 CORE_TOOLS** | 5626.6 | **+9.2%** | 5404.4 | **+8.8%** | 离验收线 +10% 只剩 0.8 个百分点 |
| **方案 1a'**:只把 Top3 并入 CORE | 5360.1 | +4.1% | 5174.1 | +4.2% | `disable_mod` / `export_diagnostic_report` / `delete_mod` |
| **方案 1b**:只**补关键词**(缺什么补什么) | 5173.5 | **+0.4%** | 5025.2 | +1.2% | **本轮采用** |
| **方案 2**:一行目录 + find/load + 按需加载 | 4124.6 | **−19.9%** | 4063.3 | −18.2% | CORE 2243 + 目录 1390 + find/load 189 + 按需加载 302/轮 |
| 方案 2 变体:目录按 30 字截断 | 3997.6 | −22.4% | 3936.3 | −20.8% | 目录 1263 |

任务书 C3 的验收线是「平均请求 token 不超过现状的 +10%」:方案 1 三档都在线内(1b 只用掉 0.4 个百分点),方案 2 的常驻部分远低于线。

### 4.3 不能只看常驻表:多发一轮的往返成本

（数字取自入库探针的同一份输出;比早期草案更准 —— 早期把「漏选工具数」当分母了,这里按「漏选请求数」算。)

| 口径 | 频率 | 单位成本 | 每百回合 |
|---|---|---|---|
| 现状(1b 前):漏选 → C2 用**全量表**重发一次 | 典型集 17.9/百回合(7 条请求) | 10114(tools 差值)+ 2982(上下文)≈ **1.3 万** | **≈ 23.5 万 token** |
| 同上(变体集) | 41.7/百回合(5 条请求) | 11247 + 2980 ≈ 1.4 万 | **≈ 59.3 万 token** |
| 方案 2:需要非 CORE 工具的轮次多一次 `load_tools` 往返 | 典型集 37/40 = 92.5/百回合 | 2982 | **≈ 27.6 万 token** |
| 方案 2(变体集) | 12/12 = 100/百回合 | 2980 | **≈ 29.8 万 token** |

**净账(典型集,每百回合)**

- **方案 1a**:常驻多花 9.2% × 5150.8 × 100 ≈ +4.7 万;消掉 7 次漏选重发 ≈ −23.5 万 → **净省约 18.8 万 token/百回合**。
- **方案 1b(采用)**:常驻只多 **+0.4 万**(实测 +0.7% ≈ 37 token/轮);把漏选降到 0 → **净省约 23.5 万 token/百回合**,还少 7 次往返延迟。
- **方案 2**:常驻省 19.9% × 5150.8 × 100 ≈ −10.3 万;但加载往返 **+27.6 万** → **净多花约 17.3 万 token/百回合**。
  - **敏感度**:若 provider 有 prompt caching,重发的上下文按约 1/5 计价(27.6 万 → 约 5.5 万),方案 2 转为**净省约 4.8 万**;缓存完全未命中时净多花 17.3 万。**这个结论取决于缓存计价与命中率,本机没有真实账单数据可验。**

### 4.4 非 token 取舍

| 维度 | 方案 1(补关键词 / 并入 CORE) | 方案 2(一行目录 + find_tools/load_tools) |
|---|---|---|
| 实现面 | 改一张数据表,1 个文件,零新工具(**本轮 1b 实际就是 +7 行关键词**) | 新增 2 个工具 + 目录生成 + 加载状态管理 + 系统提示要加「先找工具再干」 |
| 漏选率 | 补到的降到 0,没补到的照旧(长尾,见留出集 50/百回合) | 理论上归零(目录里什么都有) |
| token | +0.4% ~ +9.2%,无额外往返 | 常驻 −19.9%,但 92.5% 的轮次多一次往返 |
| 延迟 | 不变 | 每轮多 1 次模型往返(用户可见「慢一拍」) |
| 风险 | 表只会变大;写工具误带概率略升 | 目录必须与 schema 同步;`load_tools` 结果要算进压缩预算 |

---

## 5. 实施:方案 1b(已落地)

### 5.1 补了什么(全部落在 `Tools.C3Keywords`,`Tools.cs` 一张表)

| # | 关键词 | 带出的工具 | 修掉的漏选 |
|---|---|---|---|
| 1 | 停用 / 别加载 / 不加载 / 模组关掉 / 关掉这个模组 | `disable_mod` `enable_mod` `list_mods` | R10、V01、T310 |
| 2 | 卸了 / 卸掉 | `delete_mod` `disable_mod` `enable_mod` | R29、V03、T304 |
| 3 | 发到群里 / 发给群里 / 发给作者 / 发给开发 / 发给管理员 / 发我一份 / 发你一份 / 发给我一份 / 整理一份 / 整理一下发 / 导出一下 | `export_diagnostic_report` | R27、R39、V04、T307 |
| 4 | 建个文件 / 写个说明 / 写点说明 / 写进文件 / 存到文件 / 保存到文件 | `fs_list` `fs_read` `fs_write` | R34、V08、T308、T309 |
| 5 | 是什么模组 / 哪个模组 / 这个 jar / 这个jar / jar 是什么 / 装的是哪个版本 / 装的哪个版本 | `inspect_mod` `list_mods` | R33、V07、T306 |
| 6 | 运行环境 / 运行库 / java 环境 | `get_java_list` `download_java` | R32、T305 |
| 7 | 卡 | `get_system_info` `diagnose_lag` `tune_jvm` | T301、T302、T303、H03 |

**没有做的事**(按边界):

- 没动 `CORE_TOOLS`(那是方案 1a);
- 没动 `ACTION_RE` / `QUESTION_RE`(C1 已经量过:命令判定扫窗口会把 33 个写工具全带上,tools token 从 +2.3% 涨到 +9.1%);
- 没动 `ToolRunner.cs` / `AiAgent.cs`;没实施方案 2;没碰那 33 个裸写工具。

### 5.2 「卡」这一条(t-win 的发现,已核实)

- Windows 树 `windos/.../ExtraTools.cs:69` 的卡顿行是
  `卡顿 / 很卡 / 掉帧 / 帧数 / fps / FPS / lag / 优化 / 流畅 / 内存 / JVM / jvm / GC / 电脑配置` —— **没有「卡」,也没有「好卡」** → 用户说「好卡」时首轮不下发那三个工具(t-win 场景 18a,成立)。
- Linux 树 `AiData.KEYWORD_TOOLS` 的同一行**已有「好卡」**,所以 Linux 侧「好卡」不漏(T301 补词前就是 0 漏选);但**「有点卡」「一卡一卡」照样漏**(T302/T303 补词前三个工具全缺)。
- 处理:Linux 侧补了单独一个「卡」(覆盖「卡顿 / 很卡 / 好卡 / 有点卡 / 一卡一卡」全部说法)。**Windows 树的那张表不在本包文件边界内**(`windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs` 不是我的属主文件),需要 Windows 侧照第 7 行补一次 —— 请 lead 转派给 win-* 的属主。

### 5.3 前后数据(入库探针的同一份输出)

| 请求集 | 漏选 1b 前 | 漏选 1b 后 | tools 1b 前 | tools 1b 后 | 变化 |
|---|---|---|---|---|---|
| 典型请求集(39 条有标注) | 7/39 = 17.9/百回合 | **0/39 = 0.0** | 5150.8 | **5188.0** | **+0.7%** |
| 口语变体集(12 条) | 5/12 = 41.7/百回合 | **0/12 = 0.0** | 4967.4 | **5073.5** | **+2.1%** |
| 关键词补丁集(10 条) | 8/10 = 80.0/百回合 | **0/10 = 0.0** | 3843.0 | **4124.5** | +7.3%(这一集本来就是「漏最多」的) |
| 两集合并(51 条) | 12/51 = 23.5/百回合 | **0/51 = 0.0** | — | — | — |
| 对抗性留出集(10 条,不设闸门) | 6/10 = 60.0 | 5/10 = **50.0** | 4582.7 | 4635.5 | +1.2% |

**验收对照(任务书 C3)**

- [x] 同一个请求集上漏选率下降:17.9 → **0.0**、41.7 → **0.0**、合并 23.5 → **0.0**
- [x] 平均请求 token 不超过现状 +10%:典型 **+0.7%**、变体 **+2.1%**(补丁集 +7.3%,也在线内)
- [x] 每百回合总成本不高于现状:漏选重发那一项从 23.5 万降到 **0**
- [x] 场景 19 与 C1 的新用例不回归(全量 `dotnet test` 见 §5.4)

### 5.4 验证

```
# 探针(带闸门;三个集全 PASS,退出码 0)
cd tools/ai-tool-selection-probe
$env:DOTNET_ROOT='C:\Program Files\dotnet'; dotnet run -c Release -m:1 -p:UseSharedCompilation=false -- --check
→ PASS 典型请求集 :漏选请求 0(基线 0) ;tools 5188.0(基线 5188.0,上限 ×1.10)
→ PASS 口语变体集 :漏选请求 0(基线 0) ;tools 5073.5(基线 5073.5,上限 ×1.10)
→ PASS 关键词补丁集 :漏选请求 0(基线 0) ;tools 4124.5(基线 4124.5,上限 ×1.10)

# 全量单测(排除会碰真实联机服务 / 弹窗 / 浏览器三类)
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
→ 通过 1615 / 跳过 11 / 失败 0 / 总计 1626
```

基线(AI5-0)是 1594 过 / 11 跳 / 0 败 / 总 1605 → 本轮共 +21 条用例(C1 的 7 条 + 本包的 14 条),**零新增失败**;构建 0 error,只有基线那条 `LayoutEditor.cs:196 CS0649`。

新增用例:纯函数级 14 条(`tests/TipCL.Tests/Core/AiToolKeywordCoverageTests.cs`),把 §5.1 的 7 组说法逐条钉住(13 条 Theory + 1 条「补词不许放宽纯提问口径」)。**CI 不跑探针**(它是 dev 工具),所以这些单测是 CI 侧的防线。

### 5.5 长尾说明(必须说清)

留出集 1b 后仍是 50/百回合(5/10):`把钠关了`、`从 mods 里拿掉`、`发论坛`、`模组文件是啥版本`、`来份诊断`。
所以:**「0 漏选」只是「我们量过的那些说法不漏了」**,不等于真实覆盖率。要继续压这条长尾,只有三条路:

1. 继续补说法(边际收益递减,而且表会越来越长);
2. 方案 2(目录 + find_tools),理论上归零,但每轮多一次往返(§4.3);
3. 靠 C2 的 schema-miss 兜底(全量表重发一次)—— 它只是「兜底」,每次仍要花约 1.3 万 token。

---

## 6. 附带发现:`ACTION_RE` 命中就把 33 个写工具全带上(裁决:另立包,本轮不做)

`Tools.SelectToolSchemas` 末尾这段:

```csharp
// 写操作不在关键词表里时:只有明确下命令才带(纯提问不给写工具,减少误装)
if (!ToolMeta.Get(name)!.@readonly && action && !question) { result.Add(schema); }
```

| 口径 | tools token/轮 | 相对现状 | 受影响请求 | 这些请求平均多带 |
|---|---|---|---|---|
| 现状(典型集,1b 后) | 5188.0 | — | 13/40 | 7098 token「裸写工具」 |
| 收掉这一支(只留心关键词命中的写工具) | **2881.0** | **−44.5%** | — | — |
| 现状(变体集) | 5073.5 | — | 4/12 | 7112 |
| 同上收掉 | 2714.6 | −46.5% | — | — |

**按用户裁决:本轮不做,只记录。** 它的收益(−44.5%)比方案 1 和方案 2 都大,但它改的是「写工具的下发行为」(用户说「帮我装个光影」时,模型手里会有 33 个写工具可以选),属于产品行为取舍,需要单独拍板/立包。

---

## 7. 裁决落实与剩余开口

已落实(用户 2026-11-28 裁决):

| # | 裁决 | 落实 |
|---|---|---|
| 1 | 体积沿用 D-11/D-28 先例,本轮不回收,如实记录 | 本包只记录(§9),不做任何体积回收,不声称合格与否 |
| 2 | C3 方案:**方案 1** | 已实施(补关键词,不并入 CORE) |
| 3 | C3 档位:**1b 只补关键词** | 已实施:tools token +0.7%(典型)/ +2.1%(变体),远低于 +10% 线 |
| 4 | 「33 个裸写工具」另立包,本轮不做 | 只记录(§6),生产代码未动 |
| 5 | C3 回放探针**入库到 `tools/`** | 已入库 `tools/ai-tool-selection-probe`(含四套请求集与标注、README) |

仍开口:

1. **Windows 树的「卡」缺口**(§5.2):`windos/.../ExtraTools.cs:69` 需要照第 7 行补一次;不在本包文件边界内。
2. `docs/DECISIONS.md` 需要追加一条:**C3 选方案 1 + 1b 档**,以及「裸写工具另立包」「探针入库」两项。归谁写请 lead 指定。
3. 长尾(§5.5)要不要继续补,以及要不要在 C2 落地后重新量一次(那时 `tool_schema_miss` 会有真实计数器,可以把「代理指标」换成真指标)。

---

## 8. 复现方式(探针已入库)

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'      # DSH 会话里要覆盖,用户级变量已成旧值
cd tools/ai-tool-selection-probe
dotnet run -c Release -m:1 -p:UseSharedCompilation=false             # 打印全部数据
dotnet run -c Release -m:1 -p:UseSharedCompilation=false -- --check  # 当回归闸门(退出码 1 = 不达标)
```

- 输出四段:逐条 / 每集汇总 / 方案对比 / 基线对照 + 闸门;
- 探针**不进产品构建**(`TipCL.Linux.sln` 只登记三个工程、CI 只 build/test 那三个 csproj、打包脚本只拷发布产物),**也不影响 i18n 基线**(`tools/i18n-audit.py` 只扫 `src/`);
- 基线(`Before` 常量)抄自 commit `d78bd54`;要自己重跑基线,`git worktree add <tmp> d78bd54` 后把探针目录拷进去即可(README 里有逐条命令)。

**复核清单**(给后续包用):

- [x] 同一个请求集上,有漏选的请求数下降
- [x] tools token / 轮 ≤ 现状(5150.8)× 1.10 → 实测 5188.0
- [x] 每百回合总成本不高于现状的 23.5 万
- [x] 场景 19 与 C1 的新用例不回归;`dotnet test` 全过

---

## 9. 体积与文件清单

**体积(如实记录,不裁决)**:本包只改 Linux 树,不碰 Windows 的 compact 单 exe;
Linux 侧新增的是关键词字符串常量,`src/TipCL.Core/bin/Release/net8.0/TipCL.Core.dll`
**2,151,936 B → 2,152,960 B(+1,024 B)**(只作记录,不回收、不声称合格)。

| 文件 | 变更 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/Tools.cs` | 新增 `C3Keywords` 关键词表(7 行)并在 `SelectToolSchemas` 的匹配链里接上 |
| `tests/TipCL.Tests/Core/AiToolKeywordCoverageTests.cs` | 新增 14 条用例(逐条钉住补词 + 一条「不许放宽纯提问口径」) |
| `tests/TipCL.Tests/Core/AiToolSelectionWindowTests.cs` | 改 1 条:`EarlierTurnCommand_DoesNotEnableBareWriteTools` —— 补词后 `delete_mod` 会通过窗口关键词合法出现,断言改为拦 `delete_instance`(裸写工具) |
| `tools/ai-tool-selection-probe/` | 新增 dev 工具:`Program.cs`、`RequestSets.cs`(4 套请求集与标注)、`ai-tool-selection-probe.csproj`、`README.md` |
| `progress/AI5-C3.md` | 本文件 |
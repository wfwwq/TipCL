# WP-H compact 版自动化验收 + 交付 —— 完成（2026-11-07，smoke 全绿）

被验对象：`_wph_run\compact\TipCL.exe`（5,704,704 B，HEAD 6ebc26e，WP-G 注明 compact 路径 IL 与验收基线字节一致）。
本包为测试任务：未改任何源码、未做 git 操作。

## T1 smoke 全走查结果（本包亲自运行）

- 命令：`TipCL.exe --smoke --smoke-timeout 600`，进程退出码 **0**（0=无问题）。
- run 标签：**run-20261107-092136**，产物目录 `C:\Users\Administrator\AppData\Local\TipCL\_wpf_smoke\run-20261107-092136\`（report.json + 22 张页面截图 + 19 张弹窗截图）。
- 桥：**已连上**（log 头部为 `[bridge] GET /health、GET /events、POST /rpc…` 正常流量，无异常栈）。
- 总耗时 **39,849 ms**（约 40s，远低于 600s watchdog）。

| 指标 | 值 | 阈值 | 判定 |
|---|---|---|---|
| 访问页数 | **22**（NavDefs 全量） | — | 全部 ok=true |
| 点击控件 | **44** | ≥20 | 通过 |
| 真实 WPF 弹窗（in-app） | **19** | ≥10 | 通过 |
| 系统文件框（模拟取消，不计阈值） | 9 | — | 记录 |
| 未预期异常 / issue | **0** | 0 | 通过 |
| 点击失败 / 悬挂（hung）/ 慢收尾（late） | 0 / 0 / 0 | — | 通过 |
| 页面加载超时（9s 页预算） | 0 | — | 通过 |
| 截图失败 | 0 | — | 通过 |
| i18n 缺词回落（zh_CN） | 0 | — | 通过 |
| 豁免项 | 仅 1 条设计内跳过：微软正版登录（无账号，按要求跳过） | — | 预期内 |

22 页明细（id / 加载 ms / settle ms / 点击数）：launch 1/4/0 · library 63/0/1 · instance 48/0/4 · version 7/0/1 · mod 100/428/4 · mods 16/80/4 · modpack 13/86/3 · datapack 15/374/3 · resourcepack 13/1318/3 · shader 11/3277/3 · world 10/1217/3 · account 20/0/4 · java 39/0/2 · ai 104/0/1 · multiplayer 64/105/0 · servers 17/0/1 · playtime 44/0/2 · feedback 19/0/1 · settings 130/0/4 · tasks 8/0/0 · download 41/0/0 · plugins 36/0/0。最长单次点击收尾 feedback「查看系统信息」4.2s、mod「检查更新」3.1s（均在预算内正常返回）。

**旁证（去 flake）**：并行会话在 09:18:28 独立跑过一轮同参数 compact smoke（run-20261107-091828，exit 0，22 页/44 点/28 弹窗/0 issue，39.8s→50.2s elapsed），两轮结果一致，排除共享数据目录互扰导致的 flake。期间 %LOCALAPPDATA%\TipCL 确有并行 premium 实例（`--shot`）同时在跑，未观察到任何配置/启动异常，无需触发「等 30 秒重试」条款。

## T2 交付产物

| 项 | 值 |
|---|---|
| 路径 | `D:\项目\tipcl\windos\dist\TipCL-compact.exe`（dist 目录为本包新建，仓库外） |
| 字节数 | **5,704,704 B**（与验收红线基线一致，≤ 5,707,264 红线） |
| SHA256 | `0897a3ba83602f4670e0baa00d8c094d696de0bfe878df73352fc5cbc1763645` |
| 与源文件一致性 | `_wph_run\compact\TipCL.exe` 同哈希，复制无损 |

（dist 目录内另有并行包放置的 `TipCL-premium.exe` 5,999,616 B，非本包产物，仅知悉。）

## T3 日志与记录路径

- stdout/stderr 全量留档：`D:\项目\tipcl\windos\_wph_run\compact-smoke.log`（75,039 B；头部为 bridge 流量行，尾部为缩排 report.json 全文，与 run 目录内 report.json 一致）。
- 结构化报告：`C:\Users\Administrator\AppData\Local\TipCL\_wpf_smoke\run-20261107-092136\report.json`。
- 应用侧错误日志 `%LOCALAPPDATA%\TipCL\tipcl-error.log` 在本轮时间窗（09:21:36–09:22:2x）内仅出现常规 `[guard] global hooks installed` 心跳，无任何异常记录。

## 发现的问题

**无**。本轮 22 页 × 44 点击 × 19 弹窗 0 issue，无需定位根因，无建议修复 diff。

备注（非应用问题）：本包首次用 `cmd /c` 包引号启动失败（cmd 多引号解析吃掉首尾引号致「文件名、目录名或卷标语法不正确」），改为 bash 原生重定向后正常；与 exe 无关。

## 覆盖缺口与风险

1. **白名单页点击覆盖为 0**：launch / multiplayer / tasks / download / plugins 五页 smoke 点击数为 0——smoke 的 SafeButtons 白名单不含这些页的按钮（如「启动游戏」在 NeverClick 刻意不点），属测试框架设计取舍；页面加载与渲染本身已验证 ok。后续可考虑给这几页补白名单安全按钮（如 download 的刷新类）。
2. **真实登录/网络写入流未覆盖**：微软正版登录按要求跳过；统一通行证/皮肤站登录弹窗仅打开后取消；AI 连接、启动游戏等长链路未真跑（无凭据/会动真实文件）。
3. **系统文件对话框为模拟取消**（Smoke.AutoCancelSystemDialog 记录，非真实 Windows 文件框交互）。
4. **共享数据目录无隔离**：%LOCALAPPDATA%\TipCL 多实例并行时可能互相干扰（本轮实测无碍，但 CI 上建议串行或 TIPCL_HOME 隔离）。
5. 单测基线既有失败 PhoneLinkTests.FullFlow（127/128）与本轮 UI 验收无关，未复验（不在本包范围）。

# R33 全面审计报告（5 路并行 · P0~P2 · 安全不在范围）

> 基线 `b86afe3`（审计期间外部又提交了一个功能 `4df692b feat(设置): 帧率角标`，已一并纳入）。
> 收工时 `main` = 五路审计合并结果，共 **44 个提交 / 109 个文件 / +9274 −1145**。
> 审计方式：5 个独立 worktree 分支并行（A=Core、B=Linux UI、C=WPF、D=测试与构建、E=一致性与文档），
> 各路只在自己范围改代码，跨域问题只报告；由 lead 逐路 rebase + 合并 + 全量回归，**跑了 4 个轮次直到收敛**。

---

## 0 一句话结论

| 维度 | 结果 |
|---|---|
| P0（崩溃/启动失败/数据丢失） | **清零**（修 1 条：AI 页点「清空」必崩，Linux 侧） |
| P1（功能错误/静默失败/主流程中断） | **清零**（修 25 条） |
| P2（边界/健壮性/资源泄漏/文案） | **清零可修项**（修 ~45 条，剩余均为需要产品决策或纯英译工作量） |
| 构建 | Linux `0 警告 0 错误`；Windows(net48) `0 警告 0 错误` |
| 测试 | Linux **913 通过 / 0 失败 / 1 跳过**（基线 781）；Windows **573 通过 / 0 失败**（基线 547） |
| 冒烟 | 10 个页面逐个无头渲染 **10/10 全绿**（exit 0 / png>10KB / 0 WARN / 0 ERROR） |
| 新增回归测试 | **+158 条**（123 测试线 + 12 stack 拖拽 + 14 WPF 冒烟判据 + 9 其余） |

---

## 1 用户点名的两个专项

### 1.1 UI 自定义"丝滑"（布局 v2 拖拽/缩放/动画）

两侧都动过手术，Windows 与 Linux 各修各自的"手感现场"：

| 症状（用户可感知） | 根因 | 侧 | 处置 |
|---|---|---|---|
| 拖到页面上下边缘**停住不动就不滚了**，长页面根本拖不到底 | 边缘滚动只在 `MouseMove` 里做，一次固定 18px；指针不动 = 没有事件 = 不滚，且速度跟着鼠标回报率变 | Windows | 改 16ms 计时器 + 按时间算速度（贴边 300→1500 px/s），滚动量折回指针坐标后立刻重算落点 |
| 滚动条一冒出来，**卡片内容被切一刀** | `ArrangeOverride` 用测量时的旧列宽算行高；滚动条吃掉 ~10px 宽后没重量 | Linux | 抽出 `MeasureGridChildren(width,height)`，宽度差 >0.5px 就按新宽重量，并用宽度去重只补一次 `InvalidateMeasure`（避免两趟布局互激） |
| **拉右边框，框变宽了字还挤在老宽度里**，松手才跳版式 | 折行走 `MeasureOverride`，而 Arrange 只改矩形不补 Measure | Linux | 浮起/落位动画中/canvas 的子元素 Arrange 前补 `Measure`（尺寸没变时走缓存=空操作） |
| **抓起来那一下卡片先跳一下** | 起手先 `FinishAnimations()` 清零，再用槽位矩形当起点 ⇒ 眼睛位置 ≠ 起手位置 | Linux | 清零前记下残留平移，起手矩形 = 槽位 + 残留，从看到的位置接着走 |
| 缩小时**内容糊出卡片边界** | 预览期没裁剪 | Linux | 预览期间 `ClipToBounds=true`，松手/取消统一恢复 |
| stack 纵向流**拖拽完全不跟手**（卡片只降透明度，换位靠瞬移；手抖 ±1px 来回翻） | 设计上"不做插值" + 裸中点判定 | Windows | 卡片浮起跟手（每帧只写一次 `TranslateTransform.Y`，零布局）+ 邻块 FLIP 让位 170ms + **6px 滞回**防抖 + 落位动画收敛；`Motion.Enabled=false` 时逐像素回退旧行为 |
| 拖动时每次重排都新分配 rects List | — | Linux | 复用同一张表 |

**代码级结论（Linux，一次拖动的帧内）**：`PointerMoved → ApplyGridGesture` 每帧只有 1 次 `SetOffset`（纯 RenderTransform）+ 纯数学落点计算；落点没变就 `return` ⇒ **零布局**；变了才 `Snapshot + CompactCells + 一次 Reflow`。已 grep 确认：**没有**每帧读盘、**没有** Render→Save 回路（`_saving` 双守卫）、**没有**每帧重建视觉树、**没有** `MeasureOverride` setter 自激。
**Windows**：`MoveStackFloat` 每帧一次 DP 写入，`TargetIndex` 变了才 `ApplyStackReorder`（CaptureTops → InsertVisual → Flip），松手 `StartStackSettle` 170ms；新增 `StackDragPlanTests`（12 例，含"8 帧抖动序列恒不回头"）。

### 1.2 "坏按钮 / 背后根本没接功能"

两侧都做了机械盘点（Linux：255 按钮 / 105 事件 / 32 菜单项；Windows：141 handler + 199 个 RPC 名 × 239 个后端方法 + 实参名 ⊆ 形参名比对）。

**结论：真空实现 0、死入口 0、RPC 名 100% 对齐。** 问题全集中在"能力早就有了、入口没接 / 参数传错 / 文案过期"，已修：

| 入口 | 原状况 | 处置 |
|---|---|---|
| 设置「智能推荐」 | JSON 字段缺 `JsonPropertyName` ⇒ 键一个都映射不上 ⇒ 弹窗显示「0 GB / 0 核」，点「应用推荐」把内存设成下限 **512MB** 还报「已应用」 | 修字段映射 |
| 服务器「导入」「导出」 | API 早已实现，界面只弹「M3 接入」 | 接上（选 txt → 调 API → 刷新 / 导出取文本并报路径） |
| 主题包「导入」「导出」 | 只传 `{name}`，API 要 `path`/`dest` ⇒ **每次必失败**，失败还被翻译成「M3 接入」 | 改用文件对话框传对参数 |
| 账号「微软登录」「皮肤站登录」 | 能力在「账号」页，入口却什么都不做 | 跳「账号」页 + ToolTip |
| AI「新对话」预设下拉（Windows） | 调 `TryCallAsync("ai_presets")`，而 `BackendApi` **从来没有这个方法**；`TryCallAsync` 吞掉 "unknown method" ⇒ 下拉永远是空的 | 补 `BackendApi.ai_presets()`，复跑比对 `MISSING_RPC=0` |
| 13 处写操作（模组开关、切语言、改内存/分辨率、**停止游戏**、**取消任务**、取消登录…） | 用 `TryCallAsync` = 失败静默 = "点了毫无反应" | 新增 `BridgeClient.MutateAsync`（失败弹提示），逐点替换 |
| 桌面快捷方式 `.lnk`（Windows） | 写的 `-i <inst> launch <ver>` **全仓无人消费**，双击只打开启动器 | 补 `ParseStartArgs/TakeStartArgs` + `StartFromShortcutAsync`，复用既有启动链 |
| 「清理文件」 | 每个删除失败被完全吞掉（**连日志都没有**），界面照样绿字「清理完成」；干净环境下可清理数为 0 却仍可点 | 失败计数+原因落地、0 时置灰并写明原因 |
| 6 处过期文案 | 明明能用的功能说成"后续里程碑提供" / 失败谎报"M3 接入" | 逐条改准 |

---

## 2 高价值 P0/P1（除上述专项外）

### Core（业务层，Linux 移植侧）
| ID | 问题 | 后果 |
|---|---|---|
| A-01 | 布局 v2 **损坏备份整条链缺失**（Windows 有、移植时丢了） | 布局文件坏一次，下次保存就**永久覆盖**用户布局 |
| A-02 | `PyJson` 纯递归无深度上限 | `[[[[…` 直接 `StackOverflowException`，catch 接不住、进程秒退（Python 原本有 RecursionError） |
| A-04 | Linux 装出来的 Java **没有执行位**（`Chmod755` 是空方法、tar/zip 权限位解析了不用） | 启动时 `permission denied` → 「Java ? 无法启动此版本」= 点了没反应 |
| A-05 | Zulu/Microsoft 包内容是 zip，Linux 本地名却用 `.tar.gz`，解压按**后缀**派发 | Linux 上这两个 JDK **永远装不上** |
| A-07 | 前置依赖（如 Fabric API）下载失败**只写日志** | 任务卡一路「模组安装完成」，进游戏才因缺前置崩 |
| A-08 | `InstallModrinthContent` 没把算好的隔离目录传下去 | 隔离实例的光影/资源包/数据包装进共享目录：**列表看不到、游戏不加载** |
| A-10 | AI `AiSession`（内含 HttpClient+Handler）从不释放 | 每轮对话漏一个 handler，长会话「AI 越用越慢、突然连不上」 |
| A-11 | AI 流中断重试前没回滚 `state.Text` | 历史里出现「前半截 + 完整回答」重复段落 |
| A-03/06/09 | GameProcess 持锁调回调 / 词表缺 key / Cleaner 假成功 | P2 |

### Linux UI
| ID | 问题 |
|---|---|
| B-01 (**P0**) | AI 页点「清空」/「/new」无条件 `_messages.Children.Add(_empty)`，而空态一直挂着 ⇒ Avalonia 抛 `InvalidOperationException`，而 `Program.cs` 把 UI 线程异常 `Handled=false` ⇒ **进程退出**（已用探针实测复现 `already has a visual parent`） |
| B-02 | 启动按钮的"禁点/改运行态"在第一个 `await`（账号闸门）**之后**才执行，而任务是分离的 ⇒ 双击起**两个游戏进程** |
| B-03 | 落位动画补间表用 `ReferenceEqualityComparer` 比较装箱的 `("land", id)` ⇒ `Cancel` 永远匹配不上，旧补间的 `Done` 删掉新落位 |
| B-04 | 同一插件区块出现在两页时共用外壳控件 ⇒ 第二次挂父级崩/空白 |

### WPF（Windows 主发布版）
| ID | 问题 |
|---|---|
| C-01 | 启动重复点击并发两条 `launch_game`，`_taskId` 只记后一个 ⇒ 前一个停不掉、两个游戏进程 |
| C-02 | 桌面快捷方式参数没人消费（同上表） |
| C-03 | `--design-sheet` 不在免提权白名单 ⇒ 非管理员跑会 UAC + 分离进程，stdout/退出码全丢 |
| C-04 | 边缘自动滚动停摆（同上表） |
| C-05 | `ai_presets` 后端不存在（同上表） |
| C-06 | stack 拖拽不跟手（同上表） |

### 测试与构建基础设施
| ID | 问题 |
|---|---|
| D-01 | `tools/verify-linux.sh` 默认目录写 `dist/linux-x64`，而出包链路发 `dist/TipCL-linux-x64` ⇒ **裸跑一次自检必然 exit 1**，自检形同不存在 |
| D-02 | `release.yml` 发布前冒烟只跑 `--smoke` 再 `ls smoke.png` ⇒ 进程返回 0 但没画出来、日志一堆 WARN 也算过 = **发布假绿**；已升级为四判据 |
| D-03 | `tools/build.ps1`(8 处)/`package.ps1`(19 处) 含中文但无 UTF-8 BOM ⇒ **PowerShell 5.1 下解析失败**，"一键构建/打包"实际跑不起来 |
| D-04 | `.gitattributes` 无 `*.sh` 规则 ⇒ CRLF 结帐让 bash 脚本在 Linux 直接失败 |
| D-05 | 版本号散在 6 处却无守卫（已加 `RepoConsistencyTests`）、`SmokeTests` 写死 `"1.2.0"`（抬版本必红） |

### 一致性
| ID | 问题 |
|---|---|
| E-01 | `README.md` 里 v1.2.0 的 exe sha256 是**被替换掉的旧 exe** 的摘要（已改为线上的 `a7dba497…` 并注明来源） |
| E-02 | 文档 11 个文件与实现不符（"Linux 侧尚未落地 XXX"清单已过时、"添加自建区块"按钮其实已启用、Android 重建命令里的路径**全都不存在**） |

---

## 3 验证证据

```
dotnet build src/TipCL.App -c Release           → 0 警告 0 错误
dotnet test  (App 批次)                          → 359 通过 / 0 失败
dotnet test  (Core 及其余批次)                    → 554 通过 / 1 跳过 / 0 失败
dotnet build windos/src/TipCL -c Release        → 0 警告 0 错误
dotnet test  windos/tests/TipCL.Tests           → 573 通过 / 0 失败
tools/smoke-matrix.ps1（10 页逐页无头渲染）        → 10/10 通过（exit 0 / png>10KB / 0 WARN / 0 ERROR）
```

稳定性：五路同时构建的负载下重复跑各批次，**计数与结果零抖动**（当前无 flaky）。
冒烟矩阵已接进 CI（ubuntu job），WPF 侧落了 9 条判据 + 14 条测试，i18n 覆盖率落了基线守卫（未接线数一涨就红，上线当天就抓到 19 条回退）。

---

## 4 已知未修 / 需要你决策

1. **版本号必须抬（P1，未擅自改）**：v1.2.0 在 11-19 被**替换过** Windows 附件、`main` 已领先 tag 67 个提交，但版本号仍是 `1.2.0`。`Updater.Newer("1.2.0","1.2.0")=false` ⇒ **已经装了 1.2.0 的用户拿不到这批修复**。建议发 `1.2.1`（Linux 的 AppImage/.deb 也还是 11-15 的构建）。要不要我抬版本号 + 重算清单，你说一声。
2. **i18n 覆盖率 5.73% → 31.04%**，`missing en=0 / missing zh=0`。剩下约 3400 条里，绝大部分是**词表里就没有英文**（`LaunchPage` 57、`SettingsPage.Layout` 61…），要接就得先翻——纯工作量，不是 bug。
3. **两侧都有未逐行读的"策略表"**（`Crash.cs` 200+ 条崩溃特征规则、`Ai` 的子代理/checkpoint 落盘细节）：性质是"提示准不准/恢复点全不全"，不是"会不会崩"。建议按真实用户反馈做定向回归，比再铺一轮全面审计划算。
4. **可选加固 1 处**：`BackendApi.cs:3222` 的 `Crash.AnalyzeLaunch(...)` 没 try/catch（真抛异常会连带吞掉 `game.exit` 钩子与"游戏已退出"返回值）。审下来构造不出让它抛的输入，按"不加防御冗余"的纪律只记录。
5. **需要实机点一遍的清单**（自动化覆盖不到的手感/交互）：
   - stack 拖拽：卡片贴指针、跨中点邻块 170ms 滑让、被拖块不跳、松手滑回；关动画后应退回"交换即落位"
   - 拖到列表上下端**停住不动**应持续滚、到底自停；边缘绕圈不跳格
   - 启动页**双击启动** → 只一个任务；启动中再点不起第二个进程
   - 桌面 `.lnk`：创建 → 关启动器 → 双击应直接启动该版本
   - 装带 `agentPresets` 的插件后「新对话」下拉应列出预设
   - 模组开关失败路径（实例占用/只读）应弹错而不是静默
   - 窄窗/宽窗 + 切页来回 + 编辑态进出：无错位、无滚动跳顶、无编辑态残留
   - 插件 web 页快速切页后 `msedgewebview2.exe` 不残留；非管理员 `--design-sheet` 不弹 UAC

---

## 5 合并记录

```
main  ← audit/A (5 轮)  Core 业务层          11 项修复（P1×6 P2×5）
      ← audit/B (3 轮)  Linux UI 层          25 项修复（P0×1 P1×9 P2×15）
      ← audit/C (4 轮)  Windows WPF 层       17 项修复（P1×6 P2×11）+ 12 条单测
      ← audit/D (2 轮)  测试与构建          15 项修复（P1×3 P2×12）+ 144 条测试
      ← audit/E        一致性与文档          文档 11 文件 + Android 5 文件
```
各路报告全文：`progress/AUDIT-A.md` / `AUDIT-B*.md` / `AUDIT-C.md` / `AUDIT-D.md` / `AUDIT-E.md`（含每条的文件:行、触发路径、验证证据、"已核查无问题"清单，避免下轮重复挖）。

---

## 6 环境备注（复现须知）

- 本机原先**没有 .NET SDK**，审计期间装到 `D:\项目\TipCL\_tools\dotnet`（8.0.428，net48 靠 `Microsoft.NETFramework.ReferenceAssemblies` 包编译，不需要 VS）。
- 跑测试必须设 `DOTNET_ROOT=D:\项目\TipCL\_tools\dotnet`，否则 testhost 报 `hostfxr.dll could not be found`。
- 并发 restore 会撞 `NuGetScratch\lock`（报"无法获取锁定文件访问权限"）；等 1~2 分钟重试，或给每条线独立 `TEMP`/`DOTNET_CLI_HOME` + 共享 `NUGET_PACKAGES`。
- 偶发 `warning NU1900 ... v3-cache ... denied` 是本机缓存目录权限问题，把 `NUGET_HTTP_CACHE_PATH` 指到可写目录即回到 0 警告；**不是代码警告**。
- 边界：本机是 Windows，Linux 侧只做了 net8 构建 + 无头渲染验证，**Linux 真机（AppImage/deb）与 WPF 实机交互未验证**，那部分靠 `ci.yml` 的 ubuntu job 与上面第 4.5 节的人工清单。
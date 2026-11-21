# AI3-visual · 视觉巡检(第三轮)

分支 `ai3/visual`(worktree `wt-ai3-visual`,基于 main@b6df4bb)。**本包不改界面实现**,只提交巡检工具、假数据生成器、剧本、测试和报告生成脚本;截图与报告在仓库外 `D:\项目\TipCL\_visual\`。

## 做了什么

1. **`--visual-tour <剧本.json> <输出目录>`**(`src/TipCL.App/Session/VisualTour*.cs`,`Program.cs` 只加一行分发,SmokeRunner 未改):与 `--smoke` 同一条路(Avalonia Headless,`UseHeadlessDrawing=false` + Skia,SetupWithoutStarting,不建系统窗口)。
   - 动作:navigate / click / rightclick / dblclick / hover / key(含 Ctrl+Z、Esc)/ type / scroll(滚到控件并对齐顶部,或滚 N 屏)/ resize / theme / wait / shot / expect / close / dump / reset。
   - 选择器:Name / 可见文字 / 包含 / ToolTip / 占位文字 / 类型 / 祖先 Name / 序号;最上层优先(对话框 → 浮层 → 主窗口);点击 = 算中心点 → MouseDown/MouseUp,不写死坐标(中心落不到目标上时在元素内找点得中的点)。
   - 每步之后推渲染计时器 + settle;截图前丢一帧、把指针停到无悬停处;Motion.Enabled=false,LayoutTween 换成「每次渲染 pass 前进 10 秒」的虚拟时钟。
   - 截图:主窗口 + 独立浮层 + 对话框合成(对话框另存 `.dialog.png`),每张附元素清单 `*.tree.txt` 与实测「被截断的文字」。
   - 找不到目标 = 该步 FAIL 并继续;每步之后重写 `tour-result.json`;退出码 0 / 8(有 FAIL)/ 9(参数或环境不安全)。
   - 护栏:必须显式 `TIPCL_HOME`;出网闸门(全部 503,Modrinth 搜索假结果);ShellOpen / Crash / Auth 钩子只记录;文件选择框一律「取消」;拒点启动类按钮;每步检查是否冒出「启动游戏」任务。
2. **固定假数据目录**(`VisualFixture.cs`):4 个实例(.minecraft Fabric 1.20.1 / 生存 1.21.4 Fabric / NeoForge 整合包 / 1.12.2 OptiFine)、8 个版本(版本 json + 占位 jar)、68 个合法 zip 假模组(fabric.mod.json / neoforge.mods.toml,含禁用态)、7 个存档(gzip NBT level.dat)、servers.dat、截图、日志、离线账号 Steve、本周 6.2 小时 / 上周 4.8 小时游戏时长、版本清单与新闻缓存。拒绝真实用户目录;只在空目录或带标记的目录里重建。
3. **效果图状态**:`tools/visual/shoot-mockups.ps1` + `mockup-states.js/json`(按 hash 点效果图自己的状态按钮,无头 Edge 截图并裁到窗口),17 个 v3 状态。
4. **剧本**:`tools/visual/tours/full.json`(306 步 / 80 张截图,含按 DECISIONS-UI.md 的覆盖:实例库卡片 → 管理 → 实例详情、启动页 780、各页编辑态)。
5. **报告**:`tools/visual/compare.py`(标准库;Pillow 可选加速)→ `_visual\report\index.html`(29 对,SSIM + 主色差 + 热力图,按分数排序);人工审核 `_visual\report\findings.md` + 五份修复清单 `fixlist-*.md`。

## 文件清单

- `src/TipCL.App/Program.cs`(+1 行分发)
- `src/TipCL.App/Session/VisualTour.cs`、`VisualTourScript.cs`、`VisualTourFinder.cs`、`VisualTourNet.cs`、`VisualFixture.cs`(新;均带 `i18n:ignore-file`:开发者工具诊断文字 / 假数据,不是界面文案)
- `tests/TipCL.Tests/App/VisualTourTests.cs`(新,21 个用例)
- `tools/visual/*`(新:run-tour.ps1、shoot-mockups.ps1、mockup-states.js/json、pairs.json、compare.py、vtlib.py、crop.py、tours/*.json、README.md、.gitignore)
- `progress/AI3-visual.md`(本文件)

## 验收数字(2026-11-21 16:39 实测,`_verify_ai3\verify3.ps1 -Name visual`)

- Debug `--no-incremental` 构建 TipCL.Linux.sln:0 错误,警告 3 条(基线 `LayoutMotionAndHeightTests` 的 xUnit1013 ×2 / xUnit2000)。
- 全量测试:通过 1379 / 跳过 7 / 失败 0(共 1386 = 基线 1365 + 新增 21)。
- `--ai-selftest`:通过 108 / 失败 0 / 跳过 0,exit=0。
- `git diff b6df4bb HEAD -- windos`:0 个文件;`git status`:干净。
- 完整巡检 `tools/visual/tours/full.json`:306 步 / OK 298 / FAIL 8 / 截图 80 张(无窗口);8 步 FAIL 全部是「效果图里有、实际没有」的入口(第 9 / 14 / 23 / 38 / 53 / 153 / 154 / 292 步,逐条见 `_visual\report\findings.md` 第 6 节);0 次启动任务、0 次外部打开。
- 报告:29 对对照;findings 41 条(缺功能 10 / 明显缺陷 19 / 视觉偏差 12)+ 可接受差异 6 类;修复清单 5 份共 76 条。

## 遗留 / 已知限制

- 插件页没有示例插件:假数据故意不造插件(启用插件可能拉起 Node 宿主进程,违背「不弹窗」);世界分段(CurseForge)与插件市场离线落空态。
- 启动页实例切换浮层里点版本 = 直接启动游戏(产品行为,findings D9);剧本已绕开,护栏会把任何启动任务记 FAIL。
- 巡检进程关动画,设置里「界面动画」开关显示为关,不代表默认值。
- 本会话后段图片上传通道间歇失败,部分覆盖截图改用元素清单 + 实测截断清单 + 像素抽样审核(findings 开头已注明)。

## 合并冲突点

- `src/TipCL.App/Program.cs`:在 `--smoke` 分发之后插入 2 行(`--visual-tour`)。其它都是新文件。

## 建议改的文档行(本包不改,由集成代理决定)

- `docs/PARITY.md` §11.4:加一行「视觉巡检:`--visual-tour` + tools/visual(AI3-visual)」。
- `README.md` 开发者一节:`tools/visual/README.md` 的入口。

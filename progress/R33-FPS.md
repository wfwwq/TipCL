# R33 · 界面帧率角标(FPS):默认关,开关埋在设置页左下角

> 来源:用户一句话需求 ——「加个功能,显示当前帧率,默认关闭,埋到设置的角落里面」。
> 本文件是收尾报告:做了什么、改了哪些文件、怎么自证、留了什么风险。

## 1. 需求口径(逐条落地)

| 用户要求 | 落地方式 |
|---|---|
| 显示当前帧率 | 主窗口右下角一个半透明小药丸,内容是 `NN FPS`,半秒刷新一次 |
| 默认关闭 | `Config.DEFAULT_CONFIG` 新增 `ui_show_fps = false`;关着时**不注册任何渲染回调**,角标控件 `IsVisible=false`(不进布局、不参与合成) |
| 埋到设置的角落 | 设置页**左导航栏最底部**一行小开关 +「显示帧率」小字(muted 样式)。不做成设置区块 ⇒ 不进 `BlockHost` 布局、不进主题包 / 布局方案、不出现在任何页签正文里 |

## 2. 计帧口径(为什么不写 DispatcherTimer)

`DispatcherTimer` 只能证明「定时器在跑」:界面卡死时它照样准点跳,读数永远是 60。
本轮用 `TopLevel.RequestAnimationFrame` —— Avalonia **每渲染一帧回调一次**,与合成器同一节拍,
所以量到的是界面**真实帧率**(headless 冒烟里读到的就是 5 FPS,而不是定时器名义上的 2 Hz / 60)。

循环结构:`OnFrame` 计一帧 → 立刻排下一帧 → `DispatcherTimer`(500ms)采样 `帧数 / 实际经过毫秒`。
另有**续命兜底**:采样时若发现上一帧回调没回来(窗口最小化 / 渲染循环挂起),补排一次,
免得开关开着却再也不更新。

## 3. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.App/Common/FpsOverlay.cs` | **新增**。角标控件 + 计帧循环 + config 读写门面(`ConfigKey = "ui_show_fps"`)。`Attach(Window)` 幂等、窗口 `Closed` 时整份摘掉;角标 `IsHitTestVisible=false`(不许吃掉窗口右下角的鼠标命中) |
| `src/TipCL.Core/Launcher/Config.cs` | `DEFAULT_CONFIG` 增 `ui_show_fps = false`(`LauncherConfig.Load` 的缺键补写逻辑会自动把老配置补齐) |
| `src/TipCL.App/Shell/MainWindow.axaml.cs` | 构造末尾 `FpsOverlay.Attach(this)`(一行) |
| `src/TipCL.App/Pages/Settings/SettingsPage.cs` | 新增 `_fps` 开关字段 + 变更即生效即落盘;左栏改成 `Auto,*,Auto` 网格,开关行落在最底部 |
| `src/TipCL.App/Pages/Settings/SettingsPage.SaveLoad.cs` | 装载回填(`_sync` 窗口内只改运行态不写盘);`SaveAsync` 键表补 `ui_show_fps`;`WireAutoSave` 纳入 `_fps` |
| `tests/TipCL.Tests/App/FpsOverlayTests.cs` | **新增** 3 条:出厂默认必须关 / 角标默认不可见且钉在右下角、开关跟着走 / 开关真落盘 |

## 4. 自证

- `dotnet build src/TipCL.App/TipCL.App.csproj -c Debug`:0 警告 0 错误。
- `dotnet test --filter "FullyQualifiedName~TipCL.Tests.App"`:**358 通过 / 0 失败 / 0 跳过**(CI batch 1/2 全量;改动前 346 条,本轮净增 3 条 + 之前批次差异)。
- 无头真渲染(`--smoke`,900×550,`UseHeadless + Skia`,`SMOKE_PAGE=settings`):
  - `ui_show_fps=false`:左栏底部「显示帧率」开关为关,画面右下角**没有任何角标**;
  - `ui_show_fps=true`:同一位置开关变绿,右下角出现 `5 FPS` 药丸(读数就是无头会话的真实帧率);
  - `SMOKE_DARK=1` + 启动页(照片壁纸):白字 + 深色半透明底在照片背景上照样读得清。
  - 出图:`_vffps_tmp/shot0/smoke.png`、`_vffps_tmp/shot1/smoke.png`、`_vffps_tmp/shot-dark/smoke.png`(工作区临时目录,不入库)。

## 5. 遗留 / 风险(如实)

1. **滚动会拉低读数**:`RequestAnimationFrame` 只在「有新帧要合成」时触发,界面完全静止时
   Avalonia 不会持续出帧 ⇒ 静止状态下读数可能偏低或停在最后一帧。要恒定刷新得自己每帧
   `InvalidateVisual`,代价是常态占 CPU,故**不做**;这条读数应理解为「当前界面出帧速率」的上界口径。
2. **英文界面下这一行仍是中文**:本行文案没走 `I18nCore.Tr`(全仓 UI 文案仅约 2.5% 接线,
   与本页既有写法一致);要接词表需连 `en.json` / `zh_CN.json` 一起补,属内容决策。
3. **只挂主窗口**:对话框 / 弹层不显示角标(它们不是排障对象,也避免多份循环)。
4. **开关写盘是同步整份重写 config.json**(与 D-45 同一条已知代价);只在用户手动点开关时发生。
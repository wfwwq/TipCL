# AUDIT-B2 · 布局自定义「丝滑度」专项 + 假按钮盘点

- 分支:`audit/B`(基于最新 main,rebase 后继续提交)
- 环境:`$env:DOTNET_ROOT=D:\项目\TipCL\_tools\dotnet` 必设(不设则测试宿主 `hostfxr.dll` 找不到)
- 判定基线:`dotnet build src/TipCL.App -c Release` = 0 错误 / 0 警告
  (⚠ 若出现 `warning NU1900: 获取包漏洞数据时出错 … v3-cache … denied`,那是**环境侧**共享缓存目录不可写,
  不是代码问题:把 `NUGET_HTTP_CACHE_PATH` 指到可写目录后即回到 0 警告,已实测);
  `dotnet test --filter "~TipCL.Tests.App"` = **358 通过 / 0 失败**

---

# 任务一:布局自定义「丝滑度」专项

## 1. 结论先说

拖拽的**骨架**是对的(像素跟手 + 落点不变时零布局 + 占位框只画不建控件),本轮找到并修掉的 4 个"不丝滑"点都出在**测量/排布两趟的补量**与**起手瞬间的动画残留**上:

| ID | 手感症状(用户描述) | 根因(代码级) | 文件:行 | 修复 |
|----|--------------------|--------------|---------|------|
| **S1** | 「纵向滚动条一冒出来,卡片内容就被切一刀」;窗口拖窄/拉宽后行高不变、文字被裁 | `ArrangeOverride` 用**测量时的旧列宽**算的行高去排布:滚动条出现会吃掉约 10px 可用宽,而 `_rowHeights` 仍是按旧(更宽)列宽量的。Windows 参考件有 `MeasureGridChildren(width)` 的"Arrange 宽 ≠ 测量宽就重量"分支,Linux 移植时整段丢了 | `Pages/Layout/GridCanvasPanel.cs`(原 586-600 Arrange 头) | 抽出 `MeasureGridChildren(width, availableHeight)`;`ArrangeOverride` 在 `\|width − _measuredWidth\| > 0.5` 时按新宽重量行高,并只在"更窄 + 内容总高确实变了"时补一次 `InvalidateMeasure`(`_remeasureWidths` 按宽度去重,防两趟互相触发抖动) |
| **S2** | 「拉右边框,卡片框变宽了,里面的字却挤在左边的老宽度里,松手才跳成新版式」——拖宽/拖高时内容不重排 | Arrange 只改矩形、不补 `Measure`:卡片内部 `TextBlock` 的折行是在 `MeasureOverride` 里算的,`Arrange` 拉宽不会重排文本;`_box.SetFloating()` 又只 `InvalidateArrange` | `GridCanvasPanel.cs`(Arrange 子元素循环) | 对**浮起 / 落位动画中 / canvas** 的子元素在 Arrange 前补 `child.Measure(new Size(target.Width, MeasureHeightFor(id, target.Height)))`。`MeasureHeightFor` 与测量趟同一套高度约束(canvas 用像素高、拖过 height_px 用 HeightPx、吃满视口用矩形高、其余无限高)⇒ **尺寸没变时 Measure 走缓存是空操作**,动画期间不会让整张画布重量(对齐 Windows 同名方法) |
| **S3** | 「抓起来那一下卡片先跳一下 / 跳位」——上一次让位 FLIP 还没走完就抓起同一块 | `StartGesture` 先 `_box.FinishAnimations()`(把所有块的 `RenderTransform` 清零),再用**槽位矩形**当初的起手矩形 ⇒ 眼睛看到的位置 ≠ 起手位置 | `LayoutEditor.cs`(StartGesture) | 清零前先记下每张卡"相对槽位的残留平移"(`residue`),被抓起的块起手矩形 = `LayoutMotion.Offset(槽位矩形, residue)` ⇒ **从眼睛看到的位置接着走**;`_gestureAnchor` 同步用这个矩形(否则列号会差一格) |
| **S4** | 「缩小时内容溢出卡片边界、糊在外面」 | 缩放预览只改 `_float` 矩形,不裁剪 | `LayoutEditor.cs`(ApplyGridGesture 的 resize 分支 + EndBoxGesture) | 预览期间 `card.ClipToBounds = true`,手势收尾(松手/取消/丢捕获)统一恢复 `false`(对齐 Windows `ResizeFloat`) |
| **S5** | 拖动时落点每变一次就重排,拖动期间 GC 有轻微锯齿 | 每次 `ArrangeOverride` 都 `LayoutGrid.Rects(...)` **新分配一个 List** | `GridCanvasPanel.cs` | `RebuildGridRects()` / `RebuildCanvasRects()` 复用同一个 `_rects`(对齐 Windows) |
| **S6** | 上一轮修过补间键(ValueTuple 装箱),本轮复查"取消覆盖面" | `LayoutTween.Active` 键按值比较后,`FinishAnimations()` 里 `Cancel(("land", id), false)` 才真正生效 | `LayoutMotion.cs`(上一轮已改) | 复查确认:落位补间现在会被 `FinishAnimations` 干净取消;`Start` 同一块的旧补间也会先取消 |

修复文件:`GridCanvasPanel.cs`、`LayoutEditor.cs`、`LayoutMotion.cs`(各一处小改动,无重构、无新依赖)。

## 2. 「拖动一帧内到底做了什么」(代码级证据)

grid 模式拖动,`PointerMoved` → `LayoutEditor.OnBoxDragMove` → `ApplyPointer` → `ApplyGridGesture`:

1. **每帧必做(与落点无关)**:
   - `LayoutMotion.SetOffset(card, dx, dy)` → 只写 `RenderTransform`(TranslateTransform),**不触发 Measure/Arrange**;
   - `GridDropTarget(...)`(纯数学:`round((anchorX+dx)/列步长)`、`LayoutGrid.RowAt` 查行)+ `SideBySideSlot` 只有单块拖拽时才跑。
2. **落点没变 → `return`**:这一帧**零布局**(不 Measure、不 Arrange、不建控件)。
3. **落点变了才**:`RestoreGestureSnapshot()`(从起手快照恢复,避免逐帧累加)→ 改被拖块的 X/Y → `ArmFlip()` → `CompactCells()` → `LayoutGrid.Compact`(重力上浮+左右互换)+ `Reflow()`(一次 `InvalidateMeasure|Arrange|Visual`)。
   下一趟布局:Measure 走 Avalonia 的尺寸缓存(尺寸没变的块是空操作)、Arrange 里让位的块起 FLIP(160ms)。

不存在的东西(逐项 grep 确认):
- **没有**每帧读盘(`LayoutStoreV2` 只在提交时 `Load/Save`;`LayoutEditPrefs.AutoPack` 走内存 config);
- **没有** `LayoutChanged → 重排 → 再 Save` 回路(`LayoutEditor._saving` 守卫 + `BlockHost` 跳过自己那次);
- **没有** 每帧重建视觉树(`Render()` 只在提交/重载时调;拖动中只动 `RenderTransform` 与一次重排);
- **没有** `MeasureOverride` 里 setter→Invalidate 自激(本轮补的 `InvalidateMeasure` 有 `_remeasureWidths` 按宽度去重,只补一次)。

## 3. 确定性断言(仓库外探针,18 条全 PASS)

`D:\项目\TipCL\_tools\tmp-B\probe`(引用 `TipCL.App`,不进 git;`tests/**` 归另一位同事,没往那写):

```
PASS drop-target 起点不动 = (2,0)
PASS drop-target 列单调不减                    # dx ∈ [-2000,2000] 步长 7,列号不回退
PASS drop-target 列夹在 [0, cols-w]
PASS row-at 第 3 行 / 越过末尾给新行 / 负 y 归零
PASS resize 宽不小于 minW / 不超过 maxW
PASS resize 同一 dx 结果稳定(从快照重算不累加)  # 连续帧不漂移
PASS resize 左上角不动
PASS snap-height 下限 / 按 8px 取整
PASS ease 0→0 / 1→1 / 单调不减
PASS offset 折进位置                            # S3 的基础
PASS flip 阈值内不动画 / 反向偏移 = 旧视觉位置 − 新位置
PASS lerp 端点
ALL-PASS
```

## 4. 核实"已经顺"的部分(不因文档写着"已修"就跳过)

| 检查项 | 结论 / 证据 |
|---|---|
| 拖拽跟手 | 像素级 `RenderTransform`;落点不变时零布局(见 §2) |
| 落点预览 | 起手一次 `SetFloating + ShowPlaceholder`,此后只 `InvalidateArrange`;ghost 由 `GridCanvasPanel.Render` 直接画(**不新建控件**,主题色现取) |
| 并排落位 | `SideBySideSlot` 仅在"单块拖拽 + 目标行已有别的块 + 原宽放不下"时触发;`(px−段头)*2 >= 段长` 才判"想过半",不会一碰就收窄 |
| 边缘自动滚动 | `DispatcherTimer(30ms)` + `UpdateLayout()` + 立刻用新指针坐标重算落点;`_pointerDown` 兜住"拖出去丢了释放事件" |
| 尺寸精度 | 全部基于起手快照 `_gestureRects` + 绝对 dx/dy 重算(探针钉住无漂移);吸附档 `{0,4,8,16,24}` 与拖拽格线解耦 |
| 折叠 / 全部折叠 | 只 `Reflow()` 一次,不重建卡片 |
| 编辑态进出 | stack→grid 只折算一次;一次没动就原样还原且**不写盘**;卡片缓存在 `_cards`,内容不重建(插件块除外) |
| 主题切换 | `RefreshThemeVisuals` 只重刷蒙版/占位框配色,不重排 |

## 5. 任务一:已知取舍(不修,写清楚)

1. **松手时同步落盘**:`CommitBox → Persist`(Load+Save+Reload v1 缓存)在 UI 线程同步跑,慢盘上"松手后动画晚启动一帧"。与 Windows 同口径(崩溃安全优先),不动。
2. **进编辑态滚动位置回顶**:stack 与 grid 用的是两个 ScrollViewer,切换即换容器;两种模式下内容高度语义不同,偏移无法等价映射 ⇒ 与参考件一致,不修。
3. **grid + 「吃满视口」块时外层纵向滚动被关**:若同页其它块总高已超过视口,底部内容不可达(需拖走块)。这是"AI 输入区贴底"的必要代价,记录为已知边界。

---

# 任务二:假按钮 / 没接功能的入口

## 1. 扫描面与结论

机械扫过:`255` 处按钮/图标按钮构造、`105` 处 `Click +=` / `PointerReleased +=`、`32` 处 `MenuItem`,以及**全部** `CallAsync / TryCallAsync / StartTaskAsync` 的方法名(两轮正则,第二轮支持 `Dictionary<string,object>` 这类嵌套泛型)。

- **真空实现 0 处**:没有空 handler(`Click += (_, _) => { }` 0 命中);`Btn(..., null)` 3 处(AccountPage 皮肤「清除」、ModsDialog/SavesDialog 的「删除」)都在下一行挂了 Click,不是假按钮。
- **死入口 0 处**:`PlaceholderPage` 全树只有定义、无引用;每个页签都有真页面。
- **方法名 100% 对齐 IApi 方法表**:没有任何 `CallAsync("xxx")` 指向不存在的方法。
- 真正的问题是三类:**能力已有但入口没接 / 参数传错 / 文案过期**。

## 2. 清单表

| ID | 位置(文件:行) | 用户看到的文案 | 类别 | 用户感知 | 处置 |
|----|---------------|---------------|------|---------|------|
| **T2-01** | `Pages/Settings/SettingsPage.Cards.cs:279` + `:310`(`SmartRecStub`) | 「智能推荐」 | 假实现(能力已有,字段映射错) | 弹窗显示「0 GB 内存 / 0 核 CPU」,点「应用推荐」把内存设成下限 **512 MB** 还报「已应用」 | **已接上**:`SmartRecStub` 加 `JsonPropertyName`(`memory_mb`/`java_major`/`window_width`/`window_height`/`gc_preset`/`cpu_count`/`total_ram_gb`)。根因:`Coerce` 的 JsonSerializer 只忽略大小写,不会吃掉下划线 |
| **T2-02** | `Pages/Settings/SettingsSubs.cs:474-475` | 服务器「导入」「导出」 | 假实现(`import_servers`/`export_servers` 已实现) | 只弹「servers.txt 导入/导出后端 M3 接入」 | **已接上**:导入 = 选 `*.txt` → 读文本 → `import_servers` → 刷新并报新增条数;导出 = `export_servers` 取文本 → `SaveFile` 落盘并报路径 |
| **T2-03** | `Pages/Settings/SettingsSubs.cs:95-96` | 账号「微软登录」「皮肤站登录」 | 假实现(`start_microsoft_login`/`start_authlib_login` + 「账号」页已完整实现) | 只弹「登录后端 M3 接入,暂不可用」 | **已接上(不重复实现)**:跳「账号」页 + ToolTip 说明那里有设备码/皮肤站登录 |
| **T2-04** | `Pages/Settings/SettingsPage.SaveLoad.cs:563-592` | 主题包「导入」「导出」 | 假实现(参数传错) | 每次必弹「主题包 import_theme 后端 M3 接入,暂不可用」 | **已接上**:`import_theme(path)` 选文件;`export_theme(name, dest)` 选保存路径。原代码一律只传 `{ name }`,而 API 要 `path`/`dest` ⇒ 必然失败 |
| **T2-05** | `Pages/Launch/LaunchPage.cs:306` | 「联机」药丸 | 文案与行为不符 | 正常切到联机页,同时弹「联机功能将在后续里程碑提供」 | **已改**:只切页;切不到才提示 |
| **T2-06** | `Pages/Settings/SettingsSubs.cs:119` | 账号空态说明 | 过期文案 | 说「微软 / 皮肤站登录 M3 接入」 | **已改**为「…在「账号」页」 |
| **T2-07** | `Pages/Settings/SettingsSubs.cs:523` | 服务器读取失败行 | 过期文案 | 真失败时说「后端 M3 接入」 | **已改**为「服务器列表读不出来(servers.dat 可能损坏或还没生成)」 |
| **T2-08** | `Pages/Settings/SettingsSubs.cs:696` | 时长统计读失败 | 过期文案 | 同上 | **已改**为「读不到时长统计(playtime.json 可能损坏)」 |
| **T2-09** | `Pages/Settings/SettingsPage.SaveLoad.cs:585` | 主题包 save/load/delete 失败 | 过期文案 | 三个操作**本来能用**,失败却说「后端 M3 接入」 | **已改**为「主题包{op}没能完成,看日志里的原因」 |
| **T2-10** | `Pages/Settings/SettingsPage.SaveLoad.cs:556` | 「测试连接」失败 | 过期文案 | `test_ai_connection` 已实现,失败却说「后端 AI 网关 M3 接入」 | **已改**为「没有拿到回复:请检查接入地址 / 密钥 / 网络后重试」 |
| **T2-11** | `Pages/Ai/AiPage.cs:345` | 「这条命令还没接上」 | 不可达分支 | — | 保留:7 条斜杠命令里 3 条 Local 全部实现,这条只是防御 |
| **T2-12** | `Pages/Launch/LaunchPage.cs:1052` | `throw new NotImplementedException("启动能力尚未接入")` | 防御分支 | `launch_game` 已在 IApi 实现,不会触发 | 保留(仅作为"API 返回 null"的显式失败) |
| **T2-13** | `Common/Api.cs:1331` | `IApi 尚未实现方法 {method}` | 静默失败源 | 页面 `TryCallAsync` 会吞成默认值 | 保留(诊断用,已记 AppLog);本轮已把所有**真实调用名**与方法表对齐校验 |

## 3. 统计

- 扫描入口:`255` 按钮 / `105` 事件订阅 / `32` 菜单项
- 真空实现:**0**
- 死入口:**0**
- 假实现(能力已有、入口没接/参数错):**4 组共 7 个按钮** → 全部已接上
- 文案与行为不符 / 过期:**6 处** → 全部已改
- 防御性分支(保留):2 处(`AiPage:345`、`LaunchPage:1052`)
- 静默失败(建议,未改):加载类 `Run(work)` 不传 `errTitle` 时只写 AppLog(如 `CatalogPage.OnEvent` 的 `Run(LoadInstalledAsync)`)—— 属"失败无提示",但改动面覆盖 5 个页面,本轮不动

## 4. 交给 lead 决定的 2 条

1. **主题包整块卡片**:`save/load/delete` 是真功能,但 Linux 上"主题包"与「设置 → 外观」的主题色/背景预设功能重叠。是否把整块去掉?(本轮只修文案与导入导出,没有删卡片)
2. **「清理文件」卡片**:`cleaner_preview/apply` 是真实现,但在无 mods 库的干净环境里可清理数常为 0,用户点了会觉得"没反应"。要不要在预览为空时把按钮置灰并写明"当前没有可清理的文件"?

## 5. i18n

本轮新增/修改的用户文案都写在**原文件的中文字面量**里(这些文件本来就没走 `I18nCore.Tr`,例如 SettingsSubs / SettingsPage.Cards / LaunchPage 的按钮文案),因此**没有**新增 locale key,`CoreResourcesPackTests` 的 en/zh_CN 成对校验不受影响(未改 `locales/*.json`)。

---

# 构建 / 测试 / 提交

```
dotnet build src/TipCL.App/TipCL.App.csproj -c Release     → 0 错误 0 警告(见文首 NU1900 环境说明)
dotnet test  tests/TipCL.Tests --filter "~TipCL.Tests.App" → 358 通过 / 0 失败
probe(TipCL.App 引用探针,仓库外)                          → 18 条断言 ALL-PASS
```

本轮 commit(全部在 `audit/B`,未 push):

1. `布局丝滑:Arrange 宽≠测量宽时重量行高 + 浮起/缩放预览补 Measure + 起手从残留位置走 + 预览裁剪`
2. `假按钮:智能推荐字段映射 / 服务器导入导出 / 账号登录入口 / 主题包导入导出接上已有能力;过期文案修正`
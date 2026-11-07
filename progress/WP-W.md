# WP-W 窗口按钮规格复核 + Win10 Region 防闪烁 —— 完成（2026-11-07）

基于 HEAD ebacae5。验证：Windows 编译 0 错误、测试 127/128（唯一失败 PhoneLinkTests.FullFlow，基线既有）、premium 真窗三态截图核对通过。

## 1. 窗口按钮规格复核（逐项，Controls.xaml + MainWindow.xaml）
结论：**全部达标，零修改**。
| 规格 | 现状 | 结论 |
|---|---|---|
| 最小化横线 | `M0.5,5 H9.5`（MainWindow.xaml MinBtn 内联 Path） | 达标 |
| 最大化圆角方框 | `GeoMax`：10×10 圆角方框（A1.5 圆弧四角），还原态 `GeoRestore` 双框 | 达标 |
| 关闭 X | `M0.5,0.5 L9.5,9.5 M9.5,0.5 L0.5,9.5`（Btn.CaptionClose 模板内建，忽略 Content） | 达标 |
| 10×10 / 线宽 1.1 / 圆头 | `Ico.Caption`（Width/Height 10、StrokeThickness 1.1，继承 Ico.Line 的 Round 端帽）；Close 模板 Path 同参数 | 达标 |
| 46 宽×标题栏高贴边 | `Btn.Caption` Width=46 + VerticalAlignment=Stretch（标题栏 46） | 达标 |
| 关闭悬停 #C42B1C 满高白字 | Ov Border（B.CaptionHot=#C42B1C，两套 Palette 已核）铺满 Grid，hover Opacity=1；X 悬停切 B.OnCaptionHot(#FFFFFF)；按下 0.82 | 达标 |
| 最小/最大悬停淡底 | B.Ink 0.07 / 按下 0.13 | 达标（规格未约束） |

## 2. Win10 Region 阶段闪烁——保守改进
背景：原实现 `RootBorder.SizeChanged → ApplyRootCorner`，拖拽缩放中每次尺寸变化都 `SetWindowRgn` 重裁 HWND → 闪烁。Backdrop.cs 的 `SetRoundRegion` 本身无需改（r≤0 清区域、失败才 DeleteObject 均正确）；DPI 半径取整 `Math.Round(r*DpiScaleX)` 已有。
改法（全部在 **src/TipCL/MainWindow.xaml.cs**，共 3 处小段）：
1. `_rgnR` 字段后加 `static bool _inSizeMove` + `static MainWindow? _wnd`（~845 行）；
2. `ApplyWindowCorner` 首行 `_wnd = this`（供静态 WndProc 回调实例）；
3. `ApplyRootCorner` 的区域重设分支：`if (_inSizeMove) return;`（缓存字段 _rgnW/H/R 故意不更新，结束时尺寸仍"不同"→ 自动补设；WPF 侧 Clip/CornerRadius 不受影响，拖拽中仍实时圆滑）；期间尺寸未变的纯移动不触发重设，区域随窗口自动移动；
4. **越界说明**：`WndProc`（~1077 行）加 0x0231/0x0232 两段——钩子必须挂在既有 AddHook(WndProc) 上，MainWindow.xaml.cs 已有该静态方法，在此加最小 6 行是唯一干净做法（Backdrop.cs 拿不到实例）。EXITSIZEMOVE 时 `_wnd?.ApplyRootCorner()` 补设。
未做强制 flag：缓存去重逻辑天然覆盖"结束时尺寸没变"的场景。

## 3. 最大化清 Region / 还原恢复——代码路径核对（完整）
最大化：StateChanged → SyncMaxGlyph → ApplyRootCorner(maximized → r=0) → SetRoundRegion(hwnd,w,h,0) → rgn=IntPtr.Zero → SetWindowRgn 清区域；RootBorder.CornerRadius=0、Clip=null、BorderThickness=0。还原：StateChanged 再触发，rr 回到 8×Dpi ≠ 缓存 0 → 重设圆角区域；SizeChanged 更新 rw/rh。真窗截图证实（最大化满屏直角、还原后四角圆角恢复）。

## 4. 验证数据（2026-11-07）
| 项 | 结果 |
|---|---|
| 构建（premium + compact 均跑） | 0 错误（期间遇并行 WP 编辑 SettingsPage.cs 的瞬时语法错误，轮询至其落定后通过） |
| `dotnet test TipCL.sln -c Release` | **127/128**（PhoneLinkTests.FullFlow，基线既有） |
| compact TipCL.exe | **5,707,264 B**，恰好压线 ≤ 红线（我的 MainWindow 改动增量被段对齐吸收） |
| premium 真窗三态（`_wph_run/wpw/normal|maximized|restored.png`） | 正常：圆角 8、按钮矢量线条清晰；最大化：满屏 hero、直角无 Region 残留、还原双框图标；还原：几何/圆角/图标全部恢复，`exited cleanly`（WM_CLOSE 正常退出，无残留进程） |

## 备注
- 未碰：Controls.xaml（规格已达标）、Backdrop.cs（无需改）、Pages/、csproj、版本号。
- 截图脚本 `_wph_run/wpw/shots3.ps1`、`diag.ps1` 为本次验证工具，未 git add（_wph_run 整体未入库）。

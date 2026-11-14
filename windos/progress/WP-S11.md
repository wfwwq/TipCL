# WP-S11：插件安装确认弹窗「同意按钮点不到/被遮挡」修复 + 同类弹窗审计

日期：2026-11-07 · master=b0143d5 · 结论：**通用对话框壳（Dlg）无高度上限导致高内容弹窗按钮被裁出窗口；在 Dlg 层统一修复后全部自建弹窗受益，另修 1 处按钮被卷进滚动区的同类隐患。**

## 根因

安装确认弹窗（`PluginConfirm.ShowAsync`，src/TipCL/Pages/Plugins/PluginEvents.cs:217）走的是通用 `Dlg.Ask` → `Dlg.Shell`。修复前 `Shell`（src/TipCL/Services/Dlg.cs）的结构是：

- 卡片 Border **只有 MaxWidth、没有任何 MaxHeight**；
- 标题、body、按钮行全部堆在一个竖向 StackPanel 里（按钮行不在独立行上）；
- `Dlg.Push` 的 holder 是 `VerticalAlignment.Center` + Margin 24。

弹窗内容总高（标题 + 未审核提示 + system 红色警示 + 权限列表 MaxHeight 300 + 按钮行 + 边距）一旦超过窗口高度（如 720×480 窄窗、或用户调小窗口/低分屏），卡片上下对称溢出窗口，**底部按钮行被裁出可视区**——与用户截图「底部两个横条、按钮点不到」吻合。该弹窗内部的权限列表 ScrollViewer（MaxHeight 300）本身没坏，坏的是外层没有总高度上限 + 按钮行没有被钉住。

## 修复（均在 Dlg 通用层，一处修全部弹窗）

1. **src/TipCL/Services/Dlg.cs `Shell`**：卡片改为三行 Grid `Auto,*,Auto`——行 0 标题、行 1 body（包进 ScrollViewer，Auto 纵向滚动、禁横向）、行 2 按钮行。**按钮行固定钉在卡片底部，永远不被挤走**；body 吃剩余高度、超出才滚。
2. **src/TipCL/Services/Dlg.cs `Push`**：给每张卡片 MaxHeight 绑定宿主窗口 `ActualHeight × 0.9`（新增私有 `WindowPctConverter`，随窗口缩放实时生效），卡片整体永远不会超出窗口。
3. **src/TipCL/Services/Dlg.cs `Panel`**：同样把 body 包进 ScrollViewer（配合 Push 的高度上限），`Dlg.Panel` 系大面板（模组管理、存档管理、权限管理、手机同步等）同享防护。
4. **src/TipCL/Pages/Plugins/PluginsPage.Market.cs `ShowMarketDetail`（约 :337-353）**：审计发现的同类实修——「安装/插件主页」按钮行原来加在滚动 body **内部**（ScrollViewer MaxHeight 560），长简介+更新日志时按钮被卷到滚动区底部，矮窗口下不可见。已把 `actions` 移出滚动区，钉在面板底部。

## 同类审计清单

| 弹窗 | 隐患 | 处理 |
|---|---|---|
| PluginConfirm 安装/更新确认（PluginEvents.cs:263） | 外层无 MaxHeight，按钮随 StackPanel 被裁 | ✅ 由 Dlg.Shell/Push 修复（按钮行已在滚动区外） |
| Dlg.Ask/Confirm/Alert/Prompt/Choose/Form 全部调用点（AccountPage、CrashUi、CatalogPage、InstancePage、ModsDialog、SavesDialog、ServersPage、SettingsPage、VersionPage、VersionManagePage、LaunchPage、LibraryPage、FeedbackPage、FirstRunWizard、PluginsPage.Installed 等） | 同一根因 | ✅ 由 Dlg.Shell/Push 统一修复，未逐文件改 |
| Dlg.Panel 调用点（AccountPage 微信登录、AiPage 权限管理、ModsDialog、SavesDialog、GlobalModsDialog、PhoneSyncPanel、FirstRunWizard、PluginsPage.Installed 权限/日志） | body 直接撑高卡片 | ✅ 由 Dlg.Panel+Push 修复 |
| PluginsPage.Market 详情弹窗 | 安装按钮在滚动区内（MaxHeight 560） | ✅ 已移出滚动区（本工单实修） |
| ModsDialog/SavesDialog/CatalogPage/GlobalModsDialog/VersionSetupDialog/PhoneSyncPanel 内层 smoothScroll 固定 MaxHeight（320–460） | 矮窗口下浪费空间但不再致按钮被裁（外层接管滚动） | 保留（无害，改动收益低） |
| LaunchPage Popup（config/instances/news，`Popup` 独立 HWND + MaxHeight 300/372） | 不同体系（锚定 Popup，非 Dlg 卡片），且 LaunchPage.cs 归 S9 禁碰 | 未改；Popup 带自身滚动条，未见遮挡反馈 |
| AiPage `_slashPop`、ConfirmCard（MaxHeight 180） | 内联浮层/停靠卡片，按钮在滚动区外 | 无隐患，未改 |

## 验证

- 构建：`dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1` 0 错误；`tools/build-both.ps1` compact **5,737,472 B ≤ 红线 5,757,264 B**（余 19,792 B）、premium 6,034,432 B，均 0 错误。
- 测试：`dotnet test` **128 通过 / 1 失败**——失败为既有基线 `PhoneLinkTests.FullFlow`（与 S9 报告一致，本改动无关，本次跑实为 128/129 通过，优于基线 127/128）。
- 离屏复现：DevShot 新增 `--pop plugin-install`（src/TipCL/DevShot.cs，dev-only，构造「大肥鱼玩 Minecraft v1.4.2 未审核」7 项权限 payload 走真实 `PluginConfirm.ShowAsync`）：
  - `_wph_run/wps11/plugin-install-900x550.png`：默认档，按钮「取消/允许并安装」完整可见、钉在底部；
  - `_wph_run/wps11/plugin-install-720x480.png`：窄窗，权限列表在滚动区内滚动（右侧滚动条可见），**按钮行仍在卡片底部完整可见**——布局上按钮行在滚动区外，可点性成立。

## 遗留

- LaunchPage Popup 体系未动（禁碰 + 无反馈问题）；若后续要统一，可把三个 Popup 的 MaxHeight 改为绑窗口百分比。
- 内层各 smoothScroll 固定 MaxHeight 在极矮窗口下只滚外层、内层留白，属体验微调非缺陷。

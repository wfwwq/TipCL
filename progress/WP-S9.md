# WP-S9b: 启动页实例浮层「这个实例还没有版本」根因修复

接盘 WP-S9（无遗留改动，从零开始）。日期：2026-11-07。基线 09c726d。

## 1. 桥实测（新增 DevShot `--probe`，只读 RPC）

对真实数据（scratch home 副本指向 E:\.minecraft，default_instance=.minecraft，单根模式）：

- `get_installed_versions {instance:".minecraft"}` → `["26.3","fabric-loader-0.20.1-26.3"]` ✅
- `get_version_rows {}` → 2 行（26.3 原版 / fabric-loader Fabric，均完全独立，hidden=false）✅
- `get_instances` → .minecraft versions=2 ✅

结论：后端数据与解析全程正常，排除「名字失配 / hidden 过滤 / 实例解析」三条线。

## 2. 根因证据链（路径 c：可稳定复现的新根因）

离屏复现（新增 DevShot `--prefer <inst>|<ver>` 模拟实例库双击版本跳启动页）：

- `--shot launch --pop instances --prefer ".minecraft|26.3"` → 浮层显示「这个实例还没有版本」**稳定复现**（修复前 3/3）。
- 冷加载（无 --prefer）不复现 → 只有「库页跳转/版本偏好」路径触发。
- 插桩（DBG 轮询 + 身份哈希）：同一 LaunchPage、同一 ComboBox（ver 哈希一致），`ReloadVersionsAsync` 两次灌入 count=2、FillDone items=2，随后 `OpenSwitchPopup` 时 `_ver.Items.Count==0`，期间无任何其他 Fill/Clear 调用。
- 唯一嫌疑点：`ApplyPreferenceAsync` 里的
  `_ver.Fill(_ver.Items.Cast<string>(), preferredVersion)` ——
  **把 ComboBox 自身 Items 的活视图（LINQ Cast 延迟枚举）传给 Fill；Fill 先 `Items.Clear()`，活视图被清空后枚举产出 0 项，重填后 _ver 变空**。版本偏好在 → 走这个分支 → 列表被清空 → 浮层/主 chip 全空，且 `_status` 被写成「这个实例还没有版本」。

## 3. 修复

- `src/TipCL/Pages/LaunchPage.cs`
  - `ApplyPreferenceAsync`：选中偏好版本改为直接 `_ver.SelectedItem = preferredVersion`（try/finally 恢复 _syncing），不再用自身 Items 重填（LaunchPage.cs:1347-1360）。
  - `ReloadVersionsAsync`：`TryCallAsync`（吞错返回空列表）改为 `CallAsync<List<string>>`；`BridgeCallException` 时保留现有列表并直接返回，**后端出错不再误清空/误显「还没有版本」**；加 `_verGen` 过期结果守卫（期间实例已切换或有更新重载则丢弃）（LaunchPage.cs:1205-1240）。
- `src/TipCL/Services/UiExt.cs`
  - `Fill` 先快照再 Clear（防活视图自吞，防御所有同类调用）；补 `using System.Linq` 使其可编译（Fill 快照改动来自并行协作者的工作树，我只补了缺的 using）。
- `src/TipCL/DevShot.cs`（dev-only）
  - `--probe`：桥实测 RPC 打印；`--prefer <inst>|<ver>`：模拟库页跳转用户路径（含 items 计数轮询诊断）。

## 4. 构建 / 测试 / 体积

- `dotnet build TipCL.sln -c Release …`：0 错误（standard 与 premium 均过）。
- `dotnet test`：127/128（PhoneLinkTests.FullFlow 为既有基线失败，与本修复无关）。
- compact 体积 5,734,912 ≤ 上限 5,757,264 ✅。

## 5. 离屏复现结论（截图 _wph_run/wps9b/）

- 修复前：`userpath-run1.png`（浮层只有「这个实例还没有版本」，无版本子行）。
- 修复后 3/3（带版本偏好）：`fixed-run1/2/3.png` —— 均出现 2 个版本子行（26.3 选中 ✓、fabric-loader-0.20.1-26.3），无空态文案。
- 修复后 2/2（仅设默认、无版本偏好）：`fixed-nover-run1/2.png` —— 同样 2 个版本子行。

## 6. 备注

- 工作树中 LaunchPage.cs 的最终改动 = 我的修复 + 并行协作者同期落在同文件的 _verGen/SelectedItem 改动（方向一致，已合并验证）；UiExt.Fill 快照化同属协作者改动，我补了缺失 using。LibraryPage.cs（WP-S8 专属）未碰、未提交。
- 复现/验证用数据目录为 `_wph_run/wps9b/home`（用户 config 副本，instances_dir 仍指真实 E:\.minecraft，全程只读调用），未动用户数据。

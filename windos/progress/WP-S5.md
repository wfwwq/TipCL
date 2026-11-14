# WP-S5 — build-both.ps1 `-Both -Dist` 覆盖 bug 修复 —— 完成（2026-11-07）

## 根因
`Invoke-BuildFlavor` 只返回 `Exe = bin\Release\net48\TipCL.exe`（固定路径），复制到 dist 的逻辑放在全部构建完成之后统一执行。`-Both -Dist` 时流程是 compact 构建 → premium 构建（覆盖 bin，rm bin/obj 后全新输出）→ 统一复制，两个 dist 副本拿到的都是 premium 的 exe——因此两哈希相同（dist-final.md 记录的 bug）。

## 修法
把 dist 复制 + SHA256 移进 `Invoke-BuildFlavor`：每个 flavor 构建完（含 compact 红线检查通过后）**立即**复制 `TipCL-<flavor>.exe` 到 `D:\项目\tipcl\windos\dist\` 并就地计算哈希。顺序变为：compact 构建完即复制 → premium 构建（此时覆盖 bin 也无妨，compact 已落盘）→ premium 构建完即复制。汇总段保留，改为对 dist 现盘文件做哈希复核（防再张冠李戴）。单 flavor 构建行为不变（复制仍在构建后立即发生）。

## 自测（`-Both -Dist` 实跑，0 错误，exit 0，dist 已被本次产物覆盖）
| flavor | 字节 | SHA256（dist 现盘复核一致） |
|---|---|---|
| compact | 5,707,264 B（≤ D-19 红线 5,757,264，余量 50,000 B） | `948DC38D97579B5DEE1EC0CDEBB29E2CF33B4B048EFF0EEF697F68F4874AA8F2` |
| premium | 6,003,712 B | `0F6DFF3AC7537E24B21F10A339CD02C4CCBADB020C0993DC24F5EA46AEBF04EA` |

两哈希不同，bug 消除。compact 字节与冻结版一致（5,707,264）；哈希与 dist-final.md 冻结哈希（52a5fe… / 1bfc2b…）不同属预期：当前工作树含并行代理未落定的改动，正式 dist 仍由监工按 D-18 统一重切，本次覆盖是自测允许范围。

## 已知点
- 脚本须保持 UTF-8 BOM（PowerShell 5.1 无 BOM 解析中文报错），本次编辑已保持。

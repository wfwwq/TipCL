# WP-S1 — tools/build-both.ps1 一键双版构建脚本 —— 完成（2026-11-07）

## 功能
- 参数：`-Premium` / `-Compact` / `-Both`（默认）/ `-Dist`（构建后复制到 `D:\项目\tipcl\windos\dist\` + SHA256；D-18 正式重切仍归监工，默认不开）。
- 流程：`dotnet build-server shutdown` → 删 `src/TipCL/bin`、`obj`（强制全量，防 WP-U 记录的 premium→compact 增量 +512B 残留坑）→ `dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`（premium 加 `-p:Premium=true`，D-14 标准参数，脚本内逐条注释）→ 断言 0 错误（退出码非零即失败）→ compact 体积检查 ≤ 5,757,264（D-19，超限打印差值退出非零）→ 输出两版字节。
- CS2012 防锁：遇文件锁 shutdown + 等 10 秒重试，最多 3 次（为与在跑代理并行构建共存）。

## 自测（-Both 实跑，0 错误，exit 0）
| flavor | 字节 | 结果 |
|---|---|---|
| compact | 5,707,264 B | ≤ 5,757,264 合规（余量 50,000 B） |
| premium | 6,003,712 B | 0 错误，只记录 |

注：构建时工作树含并行 WP-N 未提交改动（InstallPage.cs / InstanceDetailPage.cs），字节数含其影响，正式数字以监工全量落定后为准。

## 已知点
- 脚本必须带 UTF-8 BOM 保存（Windows PowerShell 5.1 无 BOM 会按 ANSI 解析中文字符串导致解析错误），文件已是 BOM。
- exe 实际输出在 `src/TipCL/bin/Release/net48/TipCL.exe`。
- 并行风险：本脚本构建前删 bin/obj，若与 WP-N 等并行构建撞车会使其 CS2012——脚本会 shutdown 等 10 秒重试，对方失败重跑即可。

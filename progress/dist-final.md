# 交付物终版（2026-11-07，master = 0c326d6 + f05e79a 文档）

- `D:\项目\tipcl\windos\dist\TipCL-compact.exe`：**5,707,264 B**，SHA256 `52a5fe1978d4a4ccafd29a84f2c9874540f41b3ca32ed509494973a8e7d0d79b`（≤ D-19 红线 5,757,264，余量 50,000 B）
- `D:\项目\tipcl\windos\dist\TipCL-premium.exe`：**6,003,712 B**，SHA256 `1bfc2b4f5a996bea6b99b11543ac8d285ab185c938c35cadac254e37abdf7382`
- 冻结版两版 smoke 回归全绿（progress/R1-final-smoke.md，run-20261107-112124 / 112236，与基线零差异）
- 已知脚本问题：`build-both.ps1 -Both -Dist` 时 premium 后构建覆盖 bin，compact 副本错拿 premium（两哈希相同）——正式重切须分两次构建分别复制；脚本修复留待下轮。

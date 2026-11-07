# R1: 终版 smoke 回归（冻结版 bdf40bb）

日期: 2026-11-07。代码冻结（master = bdf40bb，WP-S3），只测不改。构建用 `tools/build-both.ps1 -Both`（每次构建前删 bin/obj 强制全量），smoke 串行执行（compact → 重建 premium → premium），产物存 `D:\项目\tipcl\windos\_wph_run\final-smoke\`。

## 1. 构建字节

| flavor | 字节 (B) | 红线 | 结论 |
|---|---|---|---|
| compact | 5,707,264 | ≤ 5,757,264 (D-19) | 合规，余量 50,000 B |
| premium | 6,003,712 | 仅记录 | — |

两版均 0 错误（1072 warnings CS8632 nullable 注解，为已知存量噪音）。

## 2. Smoke 数据（`--smoke --smoke-timeout 600`）

| 指标 | compact (run-20261107-112124) | premium (run-20261107-112236) | 基线 (run-20261107-092136) |
|---|---|---|---|
| state / exit_code | finished / 0 | finished / 0 | finished / 0 |
| 访问页数（预期 22） | 22，全部 ok | 22，全部 ok | 22，全部 ok |
| 点击控件数（预期 44） | 44，全部 ok | 44，全部 ok | 44，全部 ok |
| 弹窗数 | 28 | 28 | 28（dialog-01~28 系列） |
| issues / 未预期异常 | 0（`issues: []`） | 0（`issues: []`） | 0 |
| i18n_missing | 0 | 0 | 0 |
| 错误 Toast | 无未预期（无网络/未登录类豁免项均未见异常 Toast） | 同左 | 同左 |
| skipped | 微软正版登录（无账号，按要求跳过） | 同左 | 同左 |
| 耗时 | 49.0 s | 40.2 s | 39.8 s |

## 3. 与历史基线对比结论

run-20261107-091828 / run-20261107-092136 基线均为 22 页 / 44 点击 / issues 空；终版两版与其完全一致，**无新增异常、无新增错误 Toast、无页面缺失、无点击失败**。进程测后确认退出（tasklist 无 TipCL 残留）。

## 4. 异常清单

无（两版 issues 均为空，无需复跑排除 flake）。

## 5. 产物

- `D:\项目\tipcl\windos\_wph_run\final-smoke\TipCL-compact.exe`（5,707,264 B）
- `D:\项目\tipcl\windos\_wph_run\final-smoke\TipCL-premium.exe`（6,003,712 B）
- `smoke-compact.log` / `smoke-premium.log` / `report-compact.json` / `report-premium.json`
- 原始 run 目录: `%LOCALAPPDATA%\TipCL\_wpf_smoke\run-20261107-112124`、`run-20261107-112236`

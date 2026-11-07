# WP-S13：修「连接后端失败：对 config.json.xxxx.tmp 的访问被拒绝」

## 根因（疑点 A 命中）
`Utils.WriteJson`（src/TipCL/Core/Launcher/Utils.cs:790）的两段式写入里，只有 **OsReplace 阶段**有重试；**tmp 写入阶段**（`new FileStream(tmp, FileMode.Create, FileShare.None)` + Write + flushToDisk）一旦抛 `UnauthorizedAccessException`/`IOException` 就直接向上传播——OsReplace 的 12 次重试覆盖不到它。

触发路径：杀软/Defender 实时扫描会在 .tmp 刚建好（MkStemp 的 CreateNew 句柄刚关）到 WriteJson 重新打开之间的窗口里短暂独占该文件；WriteJson 以 `FileShare.None` 重新打开即撞锁抛异常。异常沿 `LauncherConfig.Save()` → 后端 RPC → `ConnectAsync` catch → `MainWindow.xaml.cs:380` 弹「连接后端失败」。

疑点 B（多实例并发 OsReplace）已在原 12 次重试范围内，且 REPLACE_RETRIES 失败模式同属 PermissionError，同样被本次修复兜住；疑点 C（孤立 .tmp）因 MkStemp 用 GUID 名复用概率极低，排除。

## 修复
1. `src/TipCL/Core/Launcher/Utils.cs`（WriteJson，约 790–876 行）：tmp 写入与替换合并为统一的 3 轮重试结构。写入阶段失败（UnauthorizedAccessException/IOException）→ finally 删 tmp → 换新 tmp 名整个重写；OsReplace 阶段重试语义不变（12 次退避，失败抛 lastError）。MkStemp/OsReplace 签名与行为未动。
2. `src/TipCL/Core/Launcher/LauncherConfig.cs`（Save，177–213 行）：捕获 WriteJson 最终失败 → 不抛，写日志（PyLog→stderr）并 2 秒后 Task.Run 后台重试一次；内存配置照用。启动/连接流程绝不再因 config 存盘失败弹「连接后端失败」。
3. `tests/TipCL.Tests/WriteJsonTests.cs`：新增并发测试——4 线程对同一 config.json 各写 125 次（共 500 次），断言无异常逃逸、文件始终是合法 JSON、目录无残留 .tmp。tests 直接引用主工程 net48 程序集，可直测 Utils.WriteJson。

## 验证
- 单测新增 1 项通过；全套 128/129 通过（唯一失败 PhoneLinkTests.FullFlow 为改动前既有失败，基线 127/128）。
- compact：0 错误，TipCL.exe = 5,737,472 ≤ 5,757,264。
- premium（-p:Premium=true）：0 错误。

## 备注
构建/测试期间有并行代理的 TipCL.exe 实例锁住 bin 输出，taskkill 后重试即恢复；这些实例与报错场景（多实例并发写 config）同源。

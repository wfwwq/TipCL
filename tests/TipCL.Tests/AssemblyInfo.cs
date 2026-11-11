using Xunit;

// 全程序集关闭测试并行(Lead 在 A3 验收时加)。
//
// 原因:TestHomeIsolation.cs 里有用例会临时改写环境变量 TIPCL_HOME(其中一条把它清空),
// 而 Ai\FileMemory、Ai\McpServer、Ai\VisionCaps、Sync\PhoneLink 等代码每次都直接调
// Utils.ResolveRoot() 读这个环境变量,不走已缓存的 Utils.ROOT。
// xunit 默认按测试类并行,一旦两者同时跑,被测代码就会在那一瞬间解析到
// 用户真实的 %LOCALAPPDATA%\TipCL。环境变量是进程级状态,只能串行。
//
// 不要删这一行,也不要用 [Collection] 局部并行来"提速"。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

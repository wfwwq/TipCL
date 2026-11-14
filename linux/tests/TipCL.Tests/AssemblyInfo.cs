using Xunit;

// 这些用例共享进程内的全局状态:Application.Resources(主题资源挂载)、TaskStore、
// Config/实例目录。xUnit 默认并行跑不同测试类,会把「构造主窗口即挂载主题资源」这类
// 全局字典写入并发化,导致 ConditionalWeakTable 重复键一类的撕裂失败。统一串行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

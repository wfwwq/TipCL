using System;
using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2d:登录预热开关(<c>BackendApi.DisableAuthWarmup</c>)。
///
/// 背景(D-23):<c>BackendApi</c> 的实例构造函数末尾会排一个后台任务,1.5 秒后
/// <c>AuthHttp.Warmup("https://login.live.com/")</c>(系统代理探测 + DNS + TLS),
/// 因此 A5 起禁止任何测试实例化 <c>BackendApi</c>。C2d 按 B1a 验收的裁决加了**只关不加**的开关:
/// 默认 false(与 Windows 版行为一致),只有测试把它置 true 才能在**不联网**的前提下实例化。
///
/// 期望值来源逐条标注:
/// - "任务书" = C2d 的任务书正文;
/// - "Win 源" = Windows 版 <c>src\TipCL\Core\BackendApi.cs</c> 的构造函数(D-23 记的实测结论);
/// - "实测" = 本包在本机跑出来的结果。
///
/// **纪律**:每个用例必须在 <c>finally</c> 里把开关还原(任务书要求)。
/// </summary>
[Collection(C2dFixtures.Collection)]
public class C2dAuthWarmupTests
{
    /// <summary>实测(任务书要求):开关默认 false —— 默认行为与 Windows 版一致,预热照发。</summary>
    [Fact]
    public void DisableAuthWarmup_default_is_false()
    {
        Assert.False(BackendApi.DisableAuthWarmup);
    }

    /// <summary>默认值下"要不要预热"= true(Win 源:构造函数无条件排那个工作项)。</summary>
    [Fact]
    public void ShouldWarmupAuth_is_true_at_default()
    {
        bool original = BackendApi.DisableAuthWarmup;
        try
        {
            BackendApi.DisableAuthWarmup = false;
            Assert.True(BackendApi.ShouldWarmupAuth());
        }
        finally
        {
            BackendApi.DisableAuthWarmup = original;
        }
    }

    /// <summary>开关置 true 时判断函数立刻变 false(构造函数里那个 if 因此不会排工作项)。</summary>
    [Fact]
    public void ShouldWarmupAuth_is_false_when_suppressed()
    {
        bool original = BackendApi.DisableAuthWarmup;
        try
        {
            BackendApi.DisableAuthWarmup = true;
            Assert.False(BackendApi.ShouldWarmupAuth());
        }
        finally
        {
            BackendApi.DisableAuthWarmup = original;
        }
    }

    /// <summary>
    /// 排队决策函数的两个分支(任务书点名:这条用例存在的意义是"以后谁把守卫删了会红")。
    /// 来源:任务书要求的 decision function;C2d 把它实现成 <c>ShouldWarmupAuth()</c>。
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Queueing_decision_follows_the_switch(bool disabled, bool expected)
    {
        bool original = BackendApi.DisableAuthWarmup;
        try
        {
            BackendApi.DisableAuthWarmup = disabled;
            Assert.Equal(expected, BackendApi.ShouldWarmupAuth());
        }
        finally
        {
            BackendApi.DisableAuthWarmup = original;
        }
    }

    /// <summary>开关可以置位并在 finally 里还原;还原之后判断函数回到默认语义(实测)。</summary>
    [Fact]
    public void Switch_can_be_set_and_restored_in_finally()
    {
        bool original = BackendApi.DisableAuthWarmup;
        try
        {
            BackendApi.DisableAuthWarmup = true;
            Assert.True(BackendApi.DisableAuthWarmup);
            Assert.False(BackendApi.ShouldWarmupAuth());
        }
        finally
        {
            BackendApi.DisableAuthWarmup = original;
        }

        Assert.False(BackendApi.DisableAuthWarmup);
        Assert.Equal(original, BackendApi.DisableAuthWarmup);
        Assert.True(BackendApi.ShouldWarmupAuth());
    }

    /// <summary>
    /// 关掉预热之后 <c>BackendApi</c> 真的能构造出来,而且用的是我们传进去的那只 <c>EventBus</c>
    /// (实测;不联网 —— 判断函数此刻为 false)。
    /// </summary>
    [Fact]
    public void BackendApi_constructs_with_the_warmup_suppressed()
    {
        bool original = BackendApi.DisableAuthWarmup;
        try
        {
            BackendApi.DisableAuthWarmup = true;
            EventBus bus = new EventBus();

            BackendApi api = new BackendApi(bus);

            Assert.NotNull(api);
            Assert.Same(bus, api.Bus);
            Assert.False(BackendApi.ShouldWarmupAuth());
        }
        finally
        {
            BackendApi.DisableAuthWarmup = original;
        }
    }

    /// <summary>
    /// 源码形状钉子:构造函数里那个守卫必须**真的**包住排队那三行。
    /// 断言的是 <c>src\TipCL.Core\BackendApi.cs</c> 的实际字节(C1aFixtures.RepoPath 找仓库根),
    /// 所以以后谁把 <c>if (ShouldWarmupAuth())</c> 删掉、改成 <c>if (true)</c>、或者把守卫挪走,
    /// 这条用例都会红 —— 它补的正是"决策函数存在但没人用"那个缺口。
    /// 期望形状来自本包的实现(任务书:守卫写在构造函数里、排在线程池之前)。
    /// </summary>
    [Fact]
    public void Constructor_guard_wraps_the_queueing_call()
    {
        string path = C1aFixtures.RepoPath("src", "TipCL.Core", "BackendApi.cs");
        string text = System.IO.File.ReadAllText(path);

        const string guard = "if (ShouldWarmupAuth())";
        const string queue = "ThreadPool.QueueUserWorkItem(delegate";
        const string warmup = "AuthHttp.Warmup(\"https://login.live.com/\")";

        // 开关与判断函数各只有一处定义。
        Assert.Equal(1, CountOf(text, "public static bool DisableAuthWarmup"));
        Assert.Equal(1, CountOf(text, "internal static bool ShouldWarmupAuth()"));
        Assert.Equal(1, CountOf(text, "return !DisableAuthWarmup;"));

        // 守卫在构造函数里,且排在线程池那次调用之前。
        int guardAt = text.IndexOf(guard, StringComparison.Ordinal);
        int queueAt = text.IndexOf(queue, StringComparison.Ordinal);
        Assert.True(guardAt > 0, "构造函数里找不到守卫: " + guard);
        Assert.True(queueAt > guardAt, "守卫必须在排队那次调用之前");

        // 守卫后面紧跟的就是那个块的开括号(中间只有空白)—— 见 D-58:这里只用 ASCII 片段比较。
        string between = text.Substring(guardAt + guard.Length, queueAt - guardAt - guard.Length);
        Assert.Equal("{", between.Trim());

        // 守卫全文件只有一处 —— 开关只关"构造时的自动预热"。
        Assert.Equal(1, CountOf(text, guard));

        // 预热调用共两处(实测):构造函数里那处(被守卫)与 start_microsoft_login 里那处
        // (用户点了按钮才走,不该也不许被这个开关关掉),所以是 2。
        Assert.Equal(2, CountOf(text, warmup));
        Assert.Equal(1, CountOf(text, queue));
    }

    /// <summary>实例构造里的"启动后预热"注释仍在那三行之前(守卫没有把注释也吞进去)。</summary>
    [Fact]
    public void Guarded_block_keeps_the_original_sleep_and_url()
    {
        string text = System.IO.File.ReadAllText(C1aFixtures.RepoPath("src", "TipCL.Core", "BackendApi.cs"));

        int queueAt = text.IndexOf("ThreadPool.QueueUserWorkItem(delegate", StringComparison.Ordinal);
        string tail = text.Substring(queueAt, Math.Min(220, text.Length - queueAt));

        Assert.Contains("Thread.Sleep(1500);", tail, StringComparison.Ordinal);
        Assert.Contains("AuthHttp.Warmup(\"https://login.live.com/\");", tail, StringComparison.Ordinal);
        // 形态一字未改:仍是「delegate { 睡 1.5 秒 -> 预热 }」,缩进也照旧(文件用制表符)。
        Assert.Contains(
            "ThreadPool.QueueUserWorkItem(delegate\n\t\t\t{\n\t\t\t\tThread.Sleep(1500);\n\t\t\t\tAuthHttp.Warmup(\"https://login.live.com/\");\n\t\t\t});",
            text,
            StringComparison.Ordinal);
    }

    private static int CountOf(string haystack, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}

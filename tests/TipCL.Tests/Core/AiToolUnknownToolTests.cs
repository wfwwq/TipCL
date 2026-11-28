// AI5-C2 前置修复:分发层吐出来的**错误载荷**不能算「成功」。
//
// 复现(修复前):ToolRunner.RunAsync 把 ExecuteAsync 的返回值无条件标成 COMPLETED,于是
// ExecuteAsync 的 default 分支(`ToolErr(..., "unknown_tool", ...)`)也带着 COMPLETED 状态回去。
// 后果有两处(ci-e 的变异验证就是靠它把漏接藏过去的):
//   1 自检的「status == COMPLETED && 结果非空」判据看不见分发表漏接;
//   2 主循环无法识别「模型叫了本轮没下发的工具」,C2 的全量重发那一半没有判据可用。
//
// 现在:ok 明确为 false 的结构化载荷 ⇒ status = FAILED + call.code = 载荷里的错误码;
// 「这次调用是不是 unknown_tool」只有一处判据 —— ToolRunner.IsUnknownTool(call)。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiToolUnknownToolTests
{
    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static ToolRunner Runner(string mode = "yolo", string instance = "inst-a")
        => new(new FakeApi()) { Instance = instance, PermissionMode = mode };

    /// <summary>工具表里没有的名字:状态必须是 FAILED,并且带上可判定的错误码。</summary>
    [Fact]
    public async Task UnknownTool_IsMarkedFailedWithTheCode()
    {
        var call = new ToolCall("u1", "no_such_tool", new PyDict());
        string text = await Runner().RunAsync(call);

        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.Equal(ToolRunner.UnknownToolCode, call.code);
        Assert.True(ToolRunner.IsUnknownTool(call), "主循环的重发判据必须认得这次调用");
        // 回给模型的文本不变(仍是那一族错误载荷):这次修的是状态与错误码,不是协议
        PyDict payload = Py.D(PyJson.Loads(text));
        Assert.False(Py.Truthy(payload.Get("ok")));
        Assert.Equal(ToolRunner.UnknownToolCode, Py.S(payload.Get("error")));
    }

    /// <summary>
    /// 载荷层的判据(同一份实现:LogsFs 的「执行器认不认这个工具名」探针也走它,
    /// 不再自己解析 JSON —— 两套判据一定会漂)。
    /// </summary>
    [Fact]
    public void PayloadErrorCode_ReadsOkAndError()
    {
        Assert.Equal("unknown_tool", ToolRunner.PayloadErrorCode("{\"ok\":false,\"error\":\"unknown_tool\",\"message\":\"没有这个工具: x\"}"));
        Assert.Equal("unknown_tool", ToolRunner.PayloadErrorCode(PyDict.Of("ok", false, "error", "unknown_tool")));
        Assert.Equal("error", ToolRunner.PayloadErrorCode(PyDict.Of("ok", false)));          // 没有码就兜底
        Assert.Equal("", ToolRunner.PayloadErrorCode(PyDict.Of("ok", true, "items", 1L)));   // 成功载荷
        Assert.Equal("", ToolRunner.PayloadErrorCode("装好了"));                              // 纯文本结果
        Assert.Equal("", ToolRunner.PayloadErrorCode(null));
        Assert.Equal("没有这个工具: x", ToolRunner.PayloadMessage("{\"ok\":false,\"error\":\"unknown_tool\",\"message\":\"没有这个工具: x\"}"));
    }

    /// <summary>MCP 名字在路由阶段失败(Mcp.RouteCall 抛错)→ 异常路径:FAILED,但不是 schema-miss。</summary>
    [Fact]
    public async Task McpRoutingFailure_IsNotASchemaMiss()
    {
        var call = new ToolCall("u2", "mcp_probe_not_connected", new PyDict());
        string text = await Runner().RunAsync(call);

        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.Equal("", call.code);
        Assert.False(ToolRunner.IsUnknownTool(call), "MCP 路由失败不是「模型叫了没下发的工具」");
        Assert.Contains("mcp_probe_not_connected", text);
    }

    /// <summary>别的错误载荷(如界面没接 ask_user)同样不再被标成成功。</summary>
    [Fact]
    public async Task OtherErrorPayloads_AreFailedToo()
    {
        var call = new ToolCall("a1", "ask_user", PyDict.Of("questions", new List<object>()));
        await Runner().RunAsync(call);

        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.Equal("no_ui", call.code);
        Assert.False(ToolRunner.IsUnknownTool(call), "只有 unknown_tool 才算 schema-miss");
    }

    /// <summary>正常结果仍是 COMPLETED、没有错误码(负例:别把整张表都改成失败)。</summary>
    [Fact]
    public async Task Success_StaysCompletedWithoutCode()
    {
        var call = new ToolCall("s1", "list_mods", PyDict.Of("instance", "inst-a"));
        await Runner().RunAsync(call);

        Assert.Equal(ToolCallStatus.COMPLETED, call.status);
        Assert.Equal("", call.code);
        Assert.False(ToolRunner.IsUnknownTool(call));
    }

    /// <summary>抛异常的失败(如参数不合适)不写错误码 —— 它不是 schema-miss,别触发重发。</summary>
    [Fact]
    public async Task ThrownFailures_DoNotLookLikeSchemaMiss()
    {
        var call = new ToolCall("e1", "write_mod_config", PyDict.Of("instance", "inst-a", "path", "", "content", "x"));
        await Runner().RunAsync(call);

        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.False(ToolRunner.IsUnknownTool(call));
    }

    /// <summary>判据本身:IsUnknownTool 只看 (FAILED, code == unknown_tool)。</summary>
    [Fact]
    public void IsUnknownTool_ReadsStatusAndCodeOnly()
    {
        var completed = new ToolCall("p1", "no_such_tool", new PyDict());
        completed.Mark(ToolCallStatus.COMPLETED, result: "x");
        completed.code = ToolRunner.UnknownToolCode;
        Assert.False(ToolRunner.IsUnknownTool(completed), "成功态不算 schema-miss");

        var failedOther = new ToolCall("p2", "list_mods", new PyDict());
        failedOther.Mark(ToolCallStatus.FAILED, error: "别的错");
        failedOther.code = "io_error";
        Assert.False(ToolRunner.IsUnknownTool(failedOther));

        var denied = new ToolCall("p3", "delete_mod", new PyDict());
        denied.Mark(ToolCallStatus.DENIED, error: "用户拒绝");
        denied.code = ToolRunner.UnknownToolCode;
        Assert.False(ToolRunner.IsUnknownTool(denied), "被拒不是未知工具");

        Assert.False(ToolRunner.IsUnknownTool(null));
    }

    /// <summary>分发探针(自检用的「执行器认不认这个名字」)跟着一起走同一份判据。</summary>
    [Fact]
    public async Task DispatchesProbe_StillDistinguishesKnownFromUnknown()
    {
        Assert.True(await ToolRunner.DispatchesAsync("fs_read"));
        Assert.True(await ToolRunner.DispatchesAsync("disable_mod"));
        Assert.False(await ToolRunner.DispatchesAsync("no_such_tool"));
        Assert.False(await ToolRunner.DispatchesAsync(""));
    }
}

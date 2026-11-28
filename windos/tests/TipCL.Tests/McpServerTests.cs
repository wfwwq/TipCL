using System;
using System.Text.Json;
using System.Threading.Tasks;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

public class McpServerTests
{
	private static JsonElement Call(string json)
	{
		string resp = McpServer.Dispatch(json);
		Assert.NotNull(resp);
		return JsonDocument.Parse(resp).RootElement;
	}

	[Fact]
	public void Initialize()
	{
		JsonElement r = Call("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
		Assert.Equal(1, r.GetProperty("id").GetInt32());
		JsonElement result = r.GetProperty("result");
		Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
		Assert.Equal("tipcl", result.GetProperty("serverInfo").GetProperty("name").GetString());
	}

	[Fact]
	public void ToolsListHidesUiOnlyTools()
	{
		JsonElement tools = Call("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}").GetProperty("result").GetProperty("tools");
		string all = tools.GetRawText();
		Assert.Contains("\"list_mods\"", all);
		Assert.Contains("\"diagnose_lag\"", all);
		foreach (string hidden in new[] { "ask_user", "dispatch_subagent", "update_plan", "remember", "forget" })
		{
			Assert.DoesNotContain("\"" + hidden + "\"", all);
		}
	}

	[Fact]
	public void HiddenToolCallRefused()
	{
		JsonElement r = Call("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"remember\",\"arguments\":{\"text\":\"x\"}}}").GetProperty("result");
		Assert.True(r.GetProperty("isError").GetBoolean());
	}

	[Fact]
	public void ErrorsAndNotifications()
	{
		Assert.Equal(-32700, Call("{oops").GetProperty("error").GetProperty("code").GetInt32());
		Assert.Equal(-32601, Call("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"nope\"}").GetProperty("error").GetProperty("code").GetInt32());
		Assert.Null(McpServer.Dispatch("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
	}

	[Fact]
	public void Batch()
	{
		JsonElement r = Call("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}]");
		Assert.Equal(JsonValueKind.Array, r.ValueKind);
		Assert.Equal(2, r.GetArrayLength());
	}

	[Fact]
	public void Sec_Mcp_1_OriginHostContentType()
	{
		Assert.True(McpServer.IsLocalOrigin(null));
		Assert.True(McpServer.IsLocalOrigin("http://127.0.0.1:8080"));
		Assert.False(McpServer.IsLocalOrigin("null"));
		Assert.False(McpServer.IsLocalOrigin("https://evil.example"));
		Assert.True(McpServer.IsLocalHost("127.0.0.1:25585"));
		Assert.True(McpServer.IsLocalHost("[::1]:25585"));
		Assert.False(McpServer.IsLocalHost("evil.example:25585"));
		Assert.True(McpServer.IsJsonContentType("application/json; charset=utf-8"));
		Assert.False(McpServer.IsJsonContentType("text/plain"));
		Assert.False(McpServer.IsJsonContentType(null));
	}

	[Fact]
	public void Sec_Mcp_2_WriteNeedsConfirmation()
	{
		var saved = McpServer.ConfirmWrite;
		var savedTimeout = McpServer.ConfirmTimeout;
		try
		{
			McpServer.ConfirmWrite = null;
			Assert.StartsWith("permission_denied", McpServer.ConfirmWriteOrDeny("delete_mod", "{}"));
			McpServer.ConfirmWrite = (n, a) => Task.FromResult(false);
			Assert.StartsWith("permission_denied", McpServer.ConfirmWriteOrDeny("delete_mod", "{}"));
			McpServer.ConfirmWrite = (n, a) => Task.FromResult(true);
			Assert.Null(McpServer.ConfirmWriteOrDeny("delete_mod", "{}"));
			McpServer.ConfirmTimeout = TimeSpan.FromMilliseconds(50);
			McpServer.ConfirmWrite = (n, a) => new TaskCompletionSource<bool>().Task;
			Assert.StartsWith("permission_denied", McpServer.ConfirmWriteOrDeny("delete_mod", "{}"));
			McpServer.ConfirmWrite = (n, a) => throw new InvalidOperationException("boom");
			Assert.StartsWith("permission_denied", McpServer.ConfirmWriteOrDeny("delete_mod", "{}"));
		}
		finally
		{
			McpServer.ConfirmWrite = saved;
			McpServer.ConfirmTimeout = savedTimeout;
		}
	}
}

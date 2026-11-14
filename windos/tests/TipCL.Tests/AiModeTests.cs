using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>AI 接入只有内置网关 / 自定义端点两种模式。</summary>
public class AiModeTests
{
	[Theory]
	[InlineData("builtin", "", "builtin")]
	[InlineData("public", "https://x.example/v1", "builtin")]
	[InlineData("", "", "builtin")]
	[InlineData("custom", "", "builtin")]
	[InlineData("custom", "https://api.example.com/v1", "custom")]
	[InlineData("newapi", "https://api.example.com", "custom")]
	public void ModeNormalizes(string mode, string baseUrl, string expected)
	{
		Assert.Equal(expected, Client.Mode(PyDict.Of("ai_mode", mode, "ai_base_url", baseUrl)));
	}

	[Fact]
	public void BuiltinIgnoresUserSettings()
	{
		PyDict ep = Client.ResolveEndpoint(PyDict.Of("ai_mode", "builtin", "ai_base_url", "", "ai_api_key", "sk-mine", "ai_model", "gpt-x"));
		Assert.Equal(Defaults.DEFAULT_MODEL, ep["model"]);
		Assert.Equal(Defaults.DEFAULT_API_BASE + "/chat/completions", ep["url"]);
		Assert.True((bool)ep["builtin"]);
	}

	[Fact]
	public void CustomNeverSendsBuiltinKey()
	{
		PyDict ep = Client.ResolveEndpoint(PyDict.Of("ai_mode", "custom", "ai_base_url", "https://api.example.com", "ai_api_key", "", "ai_model", "m1"));
		Assert.Equal("https://api.example.com/v1/chat/completions", ep["url"]);
		Assert.Equal("https://api.example.com/v1/models", ep["models_url"]);
		Assert.False(((PyDict)ep["headers"]).ContainsKey("Authorization"));
		Assert.Equal("m1", ep["model"]);
	}

	[Fact]
	public void CustomUsesOwnKey()
	{
		PyDict ep = Client.ResolveEndpoint(PyDict.Of("ai_mode", "custom", "ai_base_url", "https://api.example.com/v1/", "ai_api_key", "sk-mine", "ai_model", "m1"));
		Assert.Equal("Bearer sk-mine", ((PyDict)ep["headers"])["Authorization"]);
	}
}

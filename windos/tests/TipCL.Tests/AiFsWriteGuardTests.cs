using System;
using System.Collections.Generic;
using System.IO;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D1 的守卫(2026-11-28):fs_write 原来只判大小,不拒任何路径或后缀,而启动器数据目录
/// (Utils.ROOT)本身就在 fs_* 的允许根里 —— 于是 AI 能改写自己的权限库、对话历史、记忆
/// (ai_permissions.json / ai_chats.json / ai_memory.md / mcp_server.json),也能写出 .bat / .exe。
/// 修法照 Linux 版 AiFsTools.FsResolve 的同一段检查:账号/配置文件读写都拒、AI 自己的状态文件只读不写、
/// cache 目录不许写、写的时候拒脚本与二进制后缀;正常写实例文件放行。
/// </summary>
public class AiFsWriteGuardTests : IDisposable
{
	private readonly TestHome _home;

	private readonly string _root;

	private readonly List<string> _roots;

	public AiFsWriteGuardTests()
	{
		// TestHome 同时设 TIPCL_HOME 和 Utils.SetRoot:本用例的「数据目录根」= 临时目录
		_home = new TestHome();
		_root = Utils.ROOT;
		_roots = new List<string> { _root };
	}

	public void Dispose()
	{
		_home.Dispose();
	}

	private static PyDict Args(params object[] kv)
	{
		PyDict d = new PyDict();
		for (int i = 0; i < kv.Length; i += 2)
		{
			d[Py.Str(kv[i])] = kv[i + 1];
		}
		return d;
	}

	[Theory]
	[InlineData("ai_permissions.json")]
	[InlineData("ai_chats.json")]
	[InlineData("ai_memory.md")]
	[InlineData("mcp_server.json")]
	public void WriteRejectsAiOwnStateFiles(string name)
	{
		string path = Path.Combine(_root, name);
		File.WriteAllText(path, "- 原有内容");
		// 绝对路径和「相对数据目录根」两种写法都要拦住
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", path, "content", "改掉了"), _roots, _root));
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", name, "content", "改掉了"), _roots, _root));
		Assert.Equal("- 原有内容", File.ReadAllText(path));
	}

	[Theory]
	[InlineData("accounts.json")]
	[InlineData("config.json")]
	[InlineData("device_id")]
	public void LauncherSecretFilesRejectedForReadAndWrite(string name)
	{
		string path = Path.Combine(_root, name);
		File.WriteAllText(path, "secret");
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsResolve(path, _roots, _root));
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", path, "content", "x"), _roots, _root));
		Assert.Equal("secret", File.ReadAllText(path));
	}

	[Fact]
	public void AiStateFilesAreStillReadable()
	{
		// 移植口径:AI 自己的状态文件是「可以读,不许写」
		string path = Path.Combine(_root, "ai_permissions.json");
		File.WriteAllText(path, "{\"rules\": []}");
		string text = Assert.IsType<string>(ExtraTools.FsRead(Args("path", path), _roots, _root));
		Assert.Contains("rules", text);
	}

	[Fact]
	public void WriteRejectsCacheDirectoryButReadIsAllowed()
	{
		string dir = Path.Combine(_root, "cache", "ai_checkpoints");
		Directory.CreateDirectory(dir);
		string cp = Path.Combine(dir, "cp.json");
		File.WriteAllText(cp, "{\"n\": 1}");
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", cp, "content", "{}"), _roots, _root));
		Assert.Equal("{\"n\": 1}", File.ReadAllText(cp));
		// 读不受影响
		Assert.Contains("n", Assert.IsType<string>(ExtraTools.FsRead(Args("path", cp), _roots, _root)));
	}

	[Theory]
	[InlineData("evil.bat")]
	[InlineData("evil.cmd")]
	[InlineData("evil.ps1")]
	[InlineData("evil.vbs")]
	[InlineData("evil.exe")]
	[InlineData("evil.jar")]
	[InlineData("evil.zip")]
	[InlineData("evil.png")]
	public void WriteRejectsScriptAndBinarySuffix(string name)
	{
		string path = Path.Combine(_root, "instances", "demo", name);
		Assert.Throws<ConfigEditError>(() => ExtraTools.FsWrite(Args("path", path, "content", "x"), _roots, _root));
		Assert.False(File.Exists(path));
		Assert.False(Directory.Exists(Path.Combine(_root, "instances")));
	}

	[Theory]
	[InlineData("notes.txt")]
	[InlineData("mods.json")]
	[InlineData("logs/latest.log")]
	[InlineData("config/options.txt")]
	[InlineData("resourcepacks/pack.mcmeta")]
	public void WriteStillAllowsNormalInstanceFiles(string rel)
	{
		string path = Path.Combine(_root, "instances", "demo", rel.Replace('/', Path.DirectorySeparatorChar));
		string r = Assert.IsType<string>(ExtraTools.FsWrite(Args("path", path, "content", "hello"), _roots, _root));
		Assert.Contains("已写入", r);
		Assert.Equal("hello", File.ReadAllText(path));
	}

	[Fact]
	public void WriteStillAllowsInstanceLocalConfigJson()
	{
		// 同名文件在实例目录里是玩家自己的配置,不该被数据目录根的守卫误伤
		string path = Path.Combine(_root, "instances", "demo", "config.json");
		Assert.IsType<string>(ExtraTools.FsWrite(Args("path", path, "content", "{\"a\": 1}"), _roots, _root));
		Assert.Equal("{\"a\": 1}", File.ReadAllText(path));
	}

	[Fact]
	public void WriteDescriptionTellsTheModelWhatIsRejected()
	{
		// 工具描述里写明会被拒的几类,模型才不会白试一轮
		string json = PyJson.Dumps(ExtraTools.Schemas(), null, ensureAscii: false);
		Assert.Contains("ai_permissions.json", json);
		Assert.Contains("cache", json);
		Assert.Contains(".bat", json);
	}
}
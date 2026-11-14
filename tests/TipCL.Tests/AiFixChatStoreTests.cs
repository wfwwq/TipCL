using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 反馈弹窗「AI 修复」自带 chat_id(fix-时间戳)。以前 _ai_run 对它调 SetActive,
/// 未知 id 静默返回 null,回合落到主界面当前对话上,事件带的 chat_id 与窗口对不上,
/// 窗口 Mine() 把回复全过滤掉,一直卡在「正在想...」。这里锁住修复后的语义。
/// </summary>
public class AiFixChatStoreTests
{
	[Fact]
	public void SetActiveOnUnknownChatIsSilentAndKeepsActive()
	{
		using TestHome home = new TestHome();
		PyDict data = Store.Load();
		string main = Py.S(data.Get("active_id"));
		Assert.NotEqual("", main);

		// 这就是原来的 bug:未知 id 不报错、也不建对话,active_id 保持不变。
		PyDict chat = Store.SetActive(data, "fix-20260101000000");
		Assert.Null(chat);
		Assert.Equal(main, Py.S(Store.Load().Get("active_id")));
	}

	[Fact]
	public void EnsureChatCreatesMissingChatWithoutStealingActive()
	{
		using TestHome home = new TestHome();
		PyDict data = Store.Load();
		string main = Py.S(data.Get("active_id"));

		PyDict chat = Store.EnsureChat(data, "fix-20260101000000", "AI 修复");

		Assert.NotNull(chat);
		Assert.Equal("fix-20260101000000", Py.S(chat.Get("id")));
		Assert.Equal("AI 修复", Py.S(chat.Get("title")));
		Assert.Equal(main, Py.S(data.Get("active_id")));
		Assert.Equal("fix-20260101000000", Py.S(((PyDict)Py.ListCtor(data["chats"])[0]).Get("id")));

		// 落盘了:重启后这个 chat_id 还在,第二次进来不会再建一个。
		PyDict reloaded = Store.Load();
		Assert.NotNull(Store.GetChat(reloaded, "fix-20260101000000"));
		Assert.Equal(main, Py.S(reloaded.Get("active_id")));
	}

	[Fact]
	public void EnsureChatIsIdempotentAndKeepsMessages()
	{
		using TestHome home = new TestHome();
		PyDict data = Store.Load();
		int before = Py.ListCtor(data["chats"]).Count;

		PyDict first = Store.EnsureChat(data, "fix-1", "AI 修复");
		first["messages"] = new List<object> { PyDict.Of("role", "user", "content", "崩了") };
		Store.Save(data);

		PyDict again = Store.EnsureChat(Store.Load(), "fix-1", "AI 修复");
		Assert.Equal("崩了", Py.S(((PyDict)Py.ListCtor(again["messages"])[0]).Get("content")));
		Assert.Equal(before + 1, Py.ListCtor(Store.Load()["chats"]).Count);
	}

	/// <summary>
	/// 源码守卫:未知 chat_id 必须走 EnsureChat,而不是退回主界面当前对话。
	/// </summary>
	[Fact]
	public void AiRunEnsuresUnknownChatId()
	{
		string root = SourceRoot();
		string api = File.ReadAllText(Path.Combine(root, "src", "TipCL", "Core", "BackendApi.cs"));
		Assert.Contains("Store.EnsureChat(pyDict, chat_id", api);
		Assert.Contains("Store.GetChat(pyDict, chat_id) == null", api);
	}

	private static string SourceRoot()
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		Assert.NotNull(dir);
		return dir!.FullName;
	}
}

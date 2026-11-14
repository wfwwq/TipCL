using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Pages.Ai;

/// <summary>
/// 斜杠命令:<see cref="Prompt"/> 非空时发给助手,<see cref="Local"/> 非空时在本地执行
/// (<c>new_chat</c> / <c>clear</c> / <c>rewind</c>)。= Windows <c>Pages\Ai\AiChatOps.cs</c> 的第一个类型。
/// </summary>
public sealed class SlashCommand
{
	public string Name { get; }

	public string Desc { get; }

	public string Prompt { get; }

	public string Local { get; }

	public string[] Aliases { get; }

	public SlashCommand(string name, string desc, string prompt = "", string local = "", params string[] aliases)
	{
		Name = name ?? "";
		Desc = desc ?? "";
		Prompt = prompt ?? "";
		Local = local ?? "";
		Aliases = aliases ?? new string[0];
	}
}

/// <summary>
/// C8a:AI 页里**不依赖界面的小逻辑**(= Windows <c>Pages\Ai\AiChatOps.cs</c>,166 行)—— 单独放出来方便测试:
/// 内置斜杠命令表、前缀/包含匹配、整句精确匹配、候选循环移动、「编辑后重发」要撤回几轮、
/// 以及两条工具结果的文本解析。
///
/// 本文件**零 UI 依赖**(Windows 那份只 using <c>System</c>/<c>Collections.Generic</c>/<c>Linq</c>)⇒ 逐字照搬。
///
/// 与 Windows 的差异:**标点宽度一律以 Windows 的字节为准** —— 斜杠的两种写法是 <c>'/'(U+002F) + '／'(U+FF0F)</c>、
/// 冒号是 <c>'：'(U+FF1A) + ':'(U+003A)</c>(本机按字符码点核过);命令表里的中文文案照抄,不擅自走
/// <c>I18n.L</c>(Windows 也不走,显示时那一层才翻译)。
///
/// <b>M10 补记(这段自述原先与代码不符)</b>:本文件初版把两处提示词里的全角逗号 <c>U+FF0C</c> 写成了半角
/// <c>U+002C</c>(<c>"crash"</c> 与 <c>"fabric"</c> 那两行的 prompt),当时文件头写的是"全角字符一字不改"——
/// 一句与代码不符的自述。已按 Windows 逐字节改回(现在那两行与 Windows 逐字符相等),自述也改成上面这句。
/// 为什么必须较真:i18n 的键就是这句中文本身,半角键在 <c>en</c> 词表里查不到 ⇒ 英文界面回退显示中文;
/// 而 <c>AiChatOpsTests.RequiredSlashCommandsExist</c> 钉的正是全角那个字节(它当初就是被这 2 个字符钉红的)。
/// ⇒ 将来动这里的文案请**按码点核对**,半角与全角在编辑器里长得几乎一样。
/// </summary>
public static class AiChatOps
{
	/// <summary>【Windows 源】<c>:34-43</c> 内置斜杠命令(提示词与欢迎页快捷卡一致)。</summary>
	public static readonly IReadOnlyList<SlashCommand> Commands = new List<SlashCommand>
	{
		new SlashCommand("崩溃分析", "读最近一次崩溃报告找原因", "刚才启动失败了，读一下日志告诉我原因", "", "crash", "bkfx"),
		new SlashCommand("扫模组", "检查版本不兼容和重复模组", "扫一下当前实例的模组冲突", "", "mods", "scan", "smz"),
		new SlashCommand("新实例", "装好最新正式版并建一个实例", "帮我下载最新正式版 Minecraft", "", "instance", "xsl"),
		new SlashCommand("装Fabric", "一步配好 1.20.1 性能优化", "给我装 1.20.1 Fabric，再装钠和 Iris 光影", "", "fabric"),
		new SlashCommand("新对话", "开一个新对话", "", "new_chat", "new", "xdh"),
		new SlashCommand("清空", "删掉当前对话的全部消息", "", "clear", "clear", "qk"),
		new SlashCommand("撤回", "回退上一轮并还原改过的文件", "", "rewind", "undo", "ch"),
	};

	/// <summary>
	/// 输入以 /(或全角 ／)开头且还没输空格时,返回匹配的命令:名字/别名**前缀匹配优先**,其次包含匹配。
	/// 不是斜杠命令的输入返回空列表。
	/// </summary>
	public static List<SlashCommand> MatchSlash(string input, IEnumerable<SlashCommand>? commands = null)
	{
		string text = input ?? "";
		if (!text.StartsWith("/", StringComparison.Ordinal) && !text.StartsWith("／", StringComparison.Ordinal))
		{
			return new List<SlashCommand>();
		}
		string q = text.Substring(1);
		if (q.Any(char.IsWhiteSpace))
		{
			return new List<SlashCommand>();
		}
		List<SlashCommand> all = (commands ?? Commands).ToList();
		if (q.Length == 0)
		{
			return all;
		}
		List<SlashCommand> prefix = all.Where((SlashCommand c) =>
			c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ||
			c.Aliases.Any((string a) => a.StartsWith(q, StringComparison.OrdinalIgnoreCase))).ToList();
		List<SlashCommand> contains = all.Where((SlashCommand c) =>
			!prefix.Contains(c) &&
			(c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
		return prefix.Concat(contains).ToList();
	}

	/// <summary>整句正好是某个命令(如回车直接发了「/新对话」)时返回它,否则 null。</summary>
	public static SlashCommand? ExactSlash(string input, IEnumerable<SlashCommand>? commands = null)
	{
		string text = (input ?? "").Trim();
		if (text.Length < 2 || (text[0] != '/' && text[0] != '／'))
		{
			return null;
		}
		string q = text.Substring(1);
		return (commands ?? Commands).FirstOrDefault((SlashCommand c) =>
			string.Equals(c.Name, q, StringComparison.OrdinalIgnoreCase) ||
			c.Aliases.Any((string a) => string.Equals(a, q, StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>候选列表里上下移动选中项,循环滚动;count 为 0 时返回 -1。</summary>
	public static int MoveSelection(int current, int delta, int count)
	{
		if (count <= 0)
		{
			return -1;
		}
		int i = (current < 0) ? ((delta > 0) ? 0 : (count - 1)) : (current + delta);
		return ((i % count) + count) % count;
	}

	/// <summary>
	/// 「编辑后重发」要撤回几轮:从目标那句话(role=user、内容为 target,从后往前数第
	/// <paramref name="occurrenceFromEnd"/> 个,0 为最后一个)到对话末尾,有几条**非插队**
	/// (id 不以 <c>steer_</c> 开头)的用户消息。目标找不到或本身是插队消息返回 -1。
	/// 与 <c>Rewind.RewindLastRound</c> 每次回退到最后一条非插队用户消息之前的规则一致。
	/// </summary>
	public static int RoundsToRewind(IList<(string Role, string Id, string Content)> messages, string target, int occurrenceFromEnd = 0)
	{
		if (messages == null)
		{
			return -1;
		}
		string t = (target ?? "").Trim();
		int seen = 0;
		int index = -1;
		for (int i = messages.Count - 1; i >= 0; i--)
		{
			(string Role, string Id, string Content) m = messages[i];
			if (m.Role == "user" && (m.Content ?? "").Trim() == t)
			{
				if (seen == occurrenceFromEnd)
				{
					index = i;
					break;
				}
				seen++;
			}
		}
		if (index < 0 || IsSteer(messages[index].Id))
		{
			return -1;
		}
		int rounds = 0;
		for (int j = index; j < messages.Count; j++)
		{
			if (messages[j].Role == "user" && !IsSteer(messages[j].Id))
			{
				rounds++;
			}
		}
		return rounds;
	}

	private static bool IsSteer(string id)
	{
		return (id ?? "").StartsWith("steer_", StringComparison.Ordinal);
	}

	/// <summary>「已删除 2 条: a;b」→「a;b」;其它格式原样返回去掉前缀的内容(不是这个前缀则 null)。</summary>
	public static string? ForgottenOf(string toolResult)
	{
		string r = (toolResult ?? "").Trim();
		if (!r.StartsWith("已删除", StringComparison.Ordinal))
		{
			return null;
		}
		int i = r.IndexOfAny(new char[2] { '：', ':' });
		return (i >= 0) ? r.Substring(i + 1).Trim() : r;
	}

	/// <summary>「版本安装完成: 1.20.1-fabric」→「1.20.1-fabric」;没有就返回空。</summary>
	public static string InstalledVersionOf(string message)
	{
		string m = (message ?? "").Trim();
		const string p = "版本安装完成";
		int i = m.IndexOf(p, StringComparison.Ordinal);
		if (i < 0)
		{
			return "";
		}
		string rest = m.Substring(i + p.Length).TrimStart('：', ':', ' ');
		return rest.Trim();
	}
}

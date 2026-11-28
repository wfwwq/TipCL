using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using TipCL.Core.Plugins;

namespace TipCL.Core.Launcher.Ai;

public static class Agent
{
	public const string SUBAGENT_READONLY_REASON = "子代理只读：不能执行写操作";

	private const string _WAIT_TIMEOUT_KEY = "等待任务超时";

	private static readonly Dictionary<string, CompactState> _COMPACT_STATES = new Dictionary<string, CompactState>();

	/// <summary>每个会话状态最近一次被访问的序号(单调递增),用来做 LRU 淘汰。</summary>
	private static readonly Dictionary<string, long> _COMPACT_TOUCH = new Dictionary<string, long>();

	/// <summary>访问序号发号器。用序号而不是时钟:同一毫秒里的多次访问也能排出先后。</summary>
	private static long _COMPACT_TICK;

	/// <summary>压缩状态缓存容量:满了淘汰最久未访问的那一个,不做整表清空。</summary>
	public const int COMPACT_STATES_MAX = 16;

	private static double _LAST_ACTIVITY_TS;

	private static readonly Regex _ACTION_RE = new Regex("下载|安装|重装|卸载|装(?:个|一下|上|好|到|进|下)|删(?:除|掉|了|个|一下)|移除|清(?:理|掉|空|一下)|修(?:复|好|一下|下)|修改|改(?:成|为|一下|下|掉|到|个)|设(?:置|成|为|定|到)|配置|调(?:成|到|整|一下|大|小|高|低)|换(?:成|到|个|一下)|切(?:换|到|成)|开(?:启|一下|下)|关(?:闭|掉|上|一下)|打开|启动|运行|跑(?:一下|起来|个)|导入|导出|更新|升级|备份|恢复|还原|重启|添加|加(?:个|上|一下|进|到)|新建|创建|重命名|迁移|干活|搞定|弄(?:好|一下|个)|试试|继续|开始|执行|动手|\\b(?:install|download|uninstall|remove|delete|clean|fix|repair|set|change|configure|config|enable|disable|turn (?:on|off)|switch|launch|start|run|import|export|update|upgrade|back ?up|restore|add|create|rename|migrate|do it|go ahead|continue|proceed)\\b", RegexOptions.IgnoreCase);

	private static readonly Regex _QUESTION_RE = new Regex("怎么|怎样|如何|为什么|为啥|什么|哪(?:个|些|里|儿)|是不是|能不能|可不可以|区别|介绍|解释|说说|讲讲|意思|吗[？?]?\\s*$|[？?]\\s*$|\\b(?:how|what|why|which|where|when|does|do|is|are|should)\\b", RegexOptions.IgnoreCase);

	private static readonly Regex _DELEGATE_RE = new Regex("帮我|给我|替我|请你?|麻烦|把|\\b(?:can you|could you|would you|please|go ahead|do it)\\b", RegexOptions.IgnoreCase);

	private static readonly Regex _OBSERVE_RE = new Regex("看(?:看|下|一下|一?眼)?|瞧(?:瞧|下|一下)?|瞅(?:瞅|下|一下)?");

	private static readonly Regex _DIAGNOSE_RE = new Regex("报错|崩溃|日志|闪退|起不来|打不开|蓝屏|错误|异常|排查|哪里出了|什么问题");


	public static object DGet(object d, string k)
	{
		if (d is PyDict pyDict)
		{
			return pyDict.Get(k);
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	public static IEnumerable<object> Iter(object o)
	{
		if (o != null)
		{
			if (!(o is List<object> result))
			{
				if (!(o is PyDict pyDict))
				{
					if (!(o is string s))
					{
						if (o is IEnumerable source)
						{
							return source.Cast<object>().Select(Py.Box);
						}
						throw new TypeError("'" + Py.TypeName(o) + "' object is not iterable");
					}
					return ((IEnumerable<int>)PyDifflib.CodePoints(s)).Select((Func<int, object>)((int cp) => char.ConvertFromUtf32(cp)));
				}
				return pyDict.Keys.Cast<object>();
			}
			return result;
		}
		throw new TypeError("'NoneType' object is not iterable");
	}

	public static long Len(object o)
	{
		if (!(o is List<object> list))
		{
			if (!(o is PyDict pyDict))
			{
				if (!(o is string s))
				{
					if (o is ICollection collection)
					{
						return collection.Count;
					}
					throw new TypeError("object of type '" + Py.TypeName(o) + "' has no len()");
				}
				return PyDifflib.PyLen(s);
			}
			return pyDict.Count;
		}
		return list.Count;
	}

	public static bool SubagentAllows(string name)
	{
		if (name == "dispatch_subagent")
		{
			return false;
		}
		return ToolMeta.Get(name)?.@readonly ?? false;
	}

	private static bool _is_compact_msg(object m)
	{
		if (m is PyDict pyDict)
		{
			return Py.S(pyDict.Get("id")).StartsWith("compact_", StringComparison.Ordinal);
		}
		return false;
	}

	private static List<object> _drop_orphan_tool_head(List<object> msgs)
	{
		int i;
		for (i = 0; i < msgs.Count && msgs[i] is PyDict pyDict; i++)
		{
			if (!Py.Eq(pyDict.Get("role"), "tool"))
			{
				break;
			}
		}
		return msgs.Skip(i).ToList();
	}

	/// <summary>历史（摘要 + 未摘要消息）估算 token 超过上下文窗口的这个比例才压缩。</summary>
	public const double HISTORY_TRIGGER_RATIO = 0.5;

	/// <summary>压完后保留原文的最近消息，token 不超过窗口的这个比例。</summary>
	public const double HISTORY_KEEP_RATIO = 0.25;

	/// <summary>无论多大，至少留最近这么多条原文。</summary>
	public const int HISTORY_MIN_KEEP = 4;

	/// <summary>旧版摘要标记没有 keep 字段，当年固定留 24 条。</summary>
	private const int LEGACY_KEEP = 24;

	/// <summary>从尾部往前数，token 累计不超过 budget 的条数（至少 HISTORY_MIN_KEEP 条）。</summary>
	private static int _keep_count_by_tokens(List<object> msgs, long budget)
	{
		long sum = 0L;
		int n = 0;
		for (int i = msgs.Count - 1; i >= 0; i--)
		{
			long t = Tokens.EstimateMessages(new object[1] { msgs[i] });
			if (n >= HISTORY_MIN_KEEP && sum + t > budget)
			{
				break;
			}
			sum += t;
			n++;
		}
		return n;
	}

	/// <summary>
	/// 拼模型上下文用的历史。以前只要超过 24 条就每轮都重新摘要一遍、存一条新的摘要标记，
	/// 于是每句话后面都弹「更早的对话已压缩成摘要」，还白白多一次模型调用。
	/// 现在：找到最近一条摘要标记，它之前已被摘要的部分直接用这条摘要代替（reused，不提示、不重复落盘）；
	/// 按实际上下文占用触发：摘要 + 未摘要消息的估算 token 超过窗口的 <see cref="HISTORY_TRIGGER_RATIO"/> 才再压一次，
	/// 压完保留的原文控制在窗口的 <see cref="HISTORY_KEEP_RATIO"/> 以内（单轮里的 token 级兜底仍由 autocompact 负责）。
	/// 摘要标记带 keep = 它前面保留原文的条数；旧标记没有 keep，按当年的 24 条算。
	/// </summary>
	public static (List<object> kept, PyDict summary) _trim_history(List<object> history0, Func<List<object>, string> summarize, long contextWindow = 131072L)
	{
		if (contextWindow <= 0)
		{
			contextWindow = 131072L;
		}
		long triggerTokens = (long)(contextWindow * HISTORY_TRIGGER_RATIO);
		long keepTokens = (long)(contextWindow * HISTORY_KEEP_RATIO);
		List<object> list = new List<object>(history0 ?? new List<object>());
		int markerIdx = -1;
		for (int i = 0; i < list.Count; i++)
		{
			if (_is_compact_msg(list[i]))
			{
				markerIdx = i;
			}
		}
		PyDict marker = ((markerIdx >= 0) ? (list[markerIdx] as PyDict) : null);
		int coveredEnd = 0;
		if (marker != null)
		{
			long keep = LEGACY_KEEP;
			try
			{
				if (marker.Get("keep") != null)
				{
					keep = Py.Int(marker.Get("keep"));
				}
			}
			catch (Exception)
			{
			}
			coveredEnd = (int)Math.Max(0L, Math.Min(markerIdx, markerIdx - keep));
		}
		List<object> uncovered = new List<object>();
		for (int j = coveredEnd; j < list.Count; j++)
		{
			if (!_is_compact_msg(list[j]))
			{
				uncovered.Add(list[j]);
			}
		}
		long used = Tokens.EstimateMessages(uncovered) + ((marker != null) ? Tokens.EstimateMessages(new object[1] { marker }) : 0);
		int keepN = _keep_count_by_tokens(uncovered, keepTokens);
		if (used <= triggerTokens || keepN >= uncovered.Count || summarize == null)
		{
			PyDict reused = null;
			if (marker != null)
			{
				reused = marker.Copy();
				reused["reused"] = true;
			}
			List<object> keptAll = (summarize == null && used > triggerTokens) ? uncovered.Skip(uncovered.Count - keepN).ToList() : uncovered;
			return (kept: _drop_orphan_tool_head(keptAll), summary: reused);
		}
		List<object> toSummarize = uncovered.Take(uncovered.Count - keepN).ToList();
		List<object> item = _drop_orphan_tool_head(uncovered.Skip(uncovered.Count - keepN).ToList());
		string text;
		try
		{
			List<object> list4 = new List<object>();
			if (marker != null)
			{
				list4.Add(marker);
			}
			list4.AddRange(toSummarize);
			text = summarize(list4);
		}
		catch (AgentCancelled)
		{
			throw;
		}
		catch (Exception exc)
		{
			object[] extra = new object[2]
			{
				"dropped",
				(long)toSummarize.Count
			};
			Trace.Record("history_trim_summary_failed", null, "", "", exc, null, null, extra);
			PyDict fallback = marker?.Copy();
			if (fallback != null)
			{
				fallback["reused"] = true;
			}
			return (kept: item, summary: fallback);
		}
		if (Py.Strip(text ?? "").Length == 0)
		{
			return (kept: item, summary: null);
		}
		// keep 按原始历史里落在新标记之前、没被摘要的条数算（含其中的旧标记，下轮按位置倒推）
		int keepRaw = 0;
		int seen = 0;
		for (int k = list.Count - 1; k >= coveredEnd && seen < item.Count; k--)
		{
			keepRaw++;
			if (!_is_compact_msg(list[k]))
			{
				seen++;
			}
		}
		return (kept: item, summary: PyDict.Of("role", "user", "content", "[历史摘要]\n" + Py.Strip(text) + "\n（以上是更早对话的摘要，继续当前任务。）", "id", "compact_h" + list.Count, "keep", (long)keepRaw));
	}

	private static string _current_instance()
	{
		try
		{
			return Py.Str(Py.Or(Config.CONFIG.Get("default_instance", "default"), "default"));
		}
		catch (Exception)
		{
			return "default";
		}
	}

	private static bool _task_still_running(object res0)
	{
		if (!(res0 is PyDict pyDict))
		{
			return true;
		}
		if (Py.Truthy(pyDict.Get("timeout")) || Py.Truthy(pyDict.Get("running")))
		{
			return true;
		}
		if (pyDict.Get("ok") == null)
		{
			return true;
		}
		string text = Py.S(pyDict.Get("message"));
		if (!(text == "等待任务超时"))
		{
			return text == I18nCore.Tr("等待任务超时");
		}
		return true;
	}

	private static string _summary_input(List<object> slice, int limit = 60000)
	{
		List<object> list = new List<object>();
		foreach (object item in slice ?? new List<object>())
		{
			if (!(item is PyDict))
			{
				continue;
			}
			PyDict pyDict = (PyDict)Images.StripMessage(item);
			PyDict pyDict2 = PyDict.Of("role", pyDict.Get("role"));
			object obj = pyDict.Get("content");
			if (obj is string { Length: >4000 } text)
			{
				obj = Py.Slice(text, null, 4000) + "…(已截断)";
			}
			pyDict2["content"] = obj;
			if (Py.Truthy(pyDict.Get("name")))
			{
				pyDict2["name"] = pyDict.Get("name");
			}
			List<object> list2 = new List<object>();
			foreach (object item2 in Py.ListOr(Py.Or(pyDict.Get("tool_calls"), new List<object>())))
			{
				if (item2 is PyDict pyDict3)
				{
					PyDict pyDict4 = (Py.Or(pyDict3.Get("function"), new PyDict()) as PyDict) ?? new PyDict();
					list2.Add(PyDict.Of("name", pyDict4.Get("name"), "arguments", Py.Slice(Py.S(pyDict4.Get("arguments")), null, 500)));
				}
			}
			if (list2.Count > 0)
			{
				pyDict2["tool_calls"] = list2;
			}
			list.Add(pyDict2);
		}
		string text2 = PyJson.Dumps(list, null, ensureAscii: false);
		while (text2.Length > limit && list.Count > 2)
		{
			list.RemoveAt(1);
			text2 = PyJson.Dumps(list, null, ensureAscii: false);
		}
		if (text2.Length > limit)
		{
			return Py.Slice(text2, null, limit);
		}
		return text2;
	}

	private static List<object> _system_messages(BackendApi backend, PyDict settings)
	{
		// 插件预设：替换/追加系统提示词，按需不带运行时上下文与长期记忆（DESIGN §5）
		List<object> presetMessages = PluginAgentBridge.SystemMessages(PluginAgentBridge.PresetOf(settings), () => Prompt.SystemPrompt(), () => Tools.RuntimeContext(backend), () => Permission.PermissionNote(settings ?? new PyDict()), delegate
		{
			try
			{
				return Memory.GetMemory().Load(Py.Str(Py.Or((settings ?? new PyDict()).Get("ai_session_id"), "active")));
			}
			catch (Exception)
			{
				return "";
			}
		}, Py.Strip(Py.S((settings ?? new PyDict()).Get("ai_extra_context"))));
		if (presetMessages != null)
		{
			return presetMessages;
		}
		string text = Tools.RuntimeContext(backend);
		List<object> list = new List<object>();
		list.Add(PyDict.Of("role", "system", "content", Prompt.SystemPrompt()));
		list.Add(PyDict.Of("role", "system", "content", "当前启动器状态：\n" + text));
		List<object> list2 = list;
		string text2 = Permission.PermissionNote(settings ?? new PyDict());
		if (text2.Length > 0)
		{
			list2.Add(PyDict.Of("role", "system", "content", text2));
		}
		string text3;
		try
		{
			text3 = Memory.GetMemory().Load(Py.Str(Py.Or((settings ?? new PyDict()).Get("ai_session_id"), "active")));
		}
		catch (Exception)
		{
			text3 = "";
		}
		if (!string.IsNullOrEmpty(text3))
		{
			list2.Add(PyDict.Of("role", "system", "content", "长期记忆：\n" + text3));
		}
		string text4 = Py.Strip(Py.S((settings ?? new PyDict()).Get("ai_extra_context")));
		if (text4.Length > 0)
		{
			list2.Add(PyDict.Of("role", "system", "content", text4));
		}
		return list2;
	}

	private static object _call_confirm(Func<string, PyDict, string, string, object> confirmFn, string tname, PyDict args, string label, string reason)
	{
		if (confirmFn != null)
		{
			return confirmFn(tname, args, label, reason);
		}
		return true;
	}

	private static void _to_phase(TurnState turn, string phase)
	{
		try
		{
			if (turn.phase != "completing" && turn.phase != "error")
			{
				turn.Transition(phase);
			}
		}
		catch (IllegalTransition exc)
		{
			Trace.Record("illegal_transition", null, turn.phase, "", exc, null, null);
		}
	}

	/// <summary>
	/// 取会话的压缩状态。缓存上限 <see cref="COMPACT_STATES_MAX"/>,满了只淘汰**最久未访问**的那一条。
	/// 以前是整表 <c>Clear()</c>:正在用的会话会连带丢掉 CheckRapidRefill 的连击保护和连续失败计数。
	/// 沿用同一个 lock。
	/// </summary>
	public static CompactState _compact_state_for(string sessionId)
	{
		lock (_COMPACT_STATES)
		{
			if (!_COMPACT_STATES.TryGetValue(sessionId, out CompactState value))
			{
				if (_COMPACT_STATES.Count >= COMPACT_STATES_MAX)
				{
					string oldest = null;
					long oldestTick = long.MaxValue;
					foreach (KeyValuePair<string, long> item in _COMPACT_TOUCH)
					{
						if (item.Value < oldestTick)
						{
							oldestTick = item.Value;
							oldest = item.Key;
						}
					}
					if (oldest != null)
					{
						_COMPACT_STATES.Remove(oldest);
						_COMPACT_TOUCH.Remove(oldest);
					}
				}
				value = new CompactState();
				_COMPACT_STATES[sessionId] = value;
			}
			_COMPACT_TOUCH[sessionId] = ++_COMPACT_TICK;
			return value;
		}
	}

	/// <summary>
	/// 丢掉某个会话的压缩状态(子代理跑完清掉自己那份,别占着 16 个缓存槽;测试也用它做隔离)。
	/// </summary>
	public static void ForgetCompactState(string sessionId)
	{
		if (string.IsNullOrEmpty(sessionId))
		{
			return;
		}
		lock (_COMPACT_STATES)
		{
			_COMPACT_STATES.Remove(sessionId);
			_COMPACT_TOUCH.Remove(sessionId);
		}
	}

	/// <summary>当前缓存里的会话 id,最久没用过的排在最前(测试用;生产代码不要调)。不改变访问顺序。</summary>
	public static List<string> CompactStateSessions()
	{
		lock (_COMPACT_STATES)
		{
			return _COMPACT_STATES.Keys.OrderBy((string k) => _COMPACT_TOUCH.TryGetValue(k, out long tick) ? tick : 0L).ToList();
		}
	}

	/// <summary>
	/// 没有 provider usage 时的估算兜底:只算**新增**的消息,不再每轮把整段 messages 估一遍再累加(D7)。
	/// 返回本次要累加的估算值,以及留给下一轮的基准下标。
	/// 基准下标比当前条数还大(中途压缩/裁剪把消息表换掉了)时按全量重算一次。
	/// </summary>
	public static (long delta, long nextBase) EstimateNewMessages(List<object> messages, long baseCount)
	{
		List<object> list = messages ?? new List<object>();
		if (baseCount < 0L || baseCount > list.Count)
		{
			baseCount = 0L;
		}
		long delta = Tokens.EstimateMessages(list.Skip((int)baseCount).ToList());
		return (delta, list.Count);
	}

	public static bool WantsAction(string userText)
	{
		string text = Py.Strip(userText ?? "");
		if (text.Length == 0)
		{
			return false;
		}
		if (!_ACTION_RE.IsMatch(_OBSERVE_RE.Replace(text, "")))
		{
			if (_DELEGATE_RE.IsMatch(text))
			{
				return _DIAGNOSE_RE.IsMatch(text);
			}
			return false;
		}
		if (_QUESTION_RE.IsMatch(text) && !_DELEGATE_RE.IsMatch(text))
		{
			return false;
		}
		return true;
	}

	/// <summary>「光说没做」判定的操作类别:与 ToolMeta.side_effect + 工具名前缀一一对齐。</summary>
	public const string CLAIM_INSTALL = "install";

	public const string CLAIM_REMOVE = "remove";

	public const string CLAIM_MODIFY = "modify";

	public const string CLAIM_CREATE = "create";

	public const string CLAIM_LAUNCH = "launch";

	public const string CLAIM_MEMORY = "memory";

	/// <summary>否定/条件/失败句:这些话里的动词不算「声称已经做了」(「没删成功」「如果安装成功了」)。</summary>
	private static readonly Regex _CLAIM_NEG_RE = new Regex("没有|没能|没法|没删|没装|没改|没做|没成功|未(?:能|曾|完成|成功)|无法|不能|不再|不用|不要|别(?=忘|急|动|关|删|改|用)|拒绝|取消|失败|错误|不成功|尚未|暂时不|如果|假如|要是|一旦|万一");

	/// <summary>把回复切成短句再判:一个句子里出现否定,不影响另一个句子里的完成声明。</summary>
	private static readonly Regex _CLAIM_SPLIT_RE = new Regex("[。!?;,\uFF0C\u3001\n:]");

	private const string _CLAIM_PAT_INSTALL = "(?:已经|已|刚刚|刚|正在|马上|帮(?:你|我|您)|给(?:你|我|您))[^。!?;,\\n]{0,16}?(?:装(?:好|上|完|进|到)|安装|部署|导入|应用上|下载)|装好了|装上了|装完了|装进去了|安装好了|安装成功了|安装完毕|部署好了|导入了|应用上了|\\bI(?:'m| am)\\b[^.!?]{0,24}?\\b(?:downloading|installing)\\b|\\b(?:is|are|has been|have been|I've|I have|we've)\\b[^.!?]{0,24}?\\b(?:installed|downloaded|applied)\\b|\\b(?:installed|downloaded|reinstalled)\\s+(?:and|,)?\\s*(?:ready|done)\\b";

	private const string _CLAIM_PAT_REMOVE = "(?:已经|已|刚刚|刚|正在|马上|帮(?:你|我|您)|给(?:你|我|您))[^。!?;,\\n]{0,16}?(?:删(?:除|掉|了|干净|完)|移除|卸载|清理|清掉|清除|还原|恢复)|删掉了|删除了|删干净了|清理完了|清理掉了|清掉了|卸载了|移除了|还原了|\\bI(?:'ve| have)\\b[^.!?]{0,24}?\\b(?:removed|deleted|uninstalled|cleaned)\\b|\\b(?:is|are|has been|have been)\\b[^.!?]{0,24}?\\b(?:removed|deleted|uninstalled)\\b";

	private const string _CLAIM_PAT_MODIFY = "(?:已经|已|刚刚|刚|正在|马上|帮(?:你|我|您)|给(?:你|我|您))[^。!?;,\\n]{0,16}?(?:改|调|设|配置|禁用|启用|关闭|打开|应用(?!上)|修|优化|处理|备份|换|切换|写)|(?:调到|调成|调为|改成|改为|设为|设置成|设置为|换成|切换到|调整为|修改为)[^。!?;,\\n]{0,12}?了|调好了|改好了|修好了|优化掉了|处理好了|备份好了|写好了|应用了|\\bI(?:'ve| have)\\b[^.!?]{0,24}?\\b(?:set|changed|updated|configured|enabled|disabled|adjusted|fixed)\\b|\\b(?:is|are|has been|have been)\\b[^.!?]{0,24}?\\b(?:set|changed|updated|configured|enabled|disabled|adjusted)\\b";

	private const string _CLAIM_PAT_CREATE = "(?:已经|已|刚刚|刚)[^。!?;,\\n]{0,12}?(?:创建|新建|建好|建成|建立|建)|创建好了|建好了|创建完成|建完了|\\bI(?:'ve| have)\\b[^.!?]{0,20}?\\b(?:created|added)\\b|\\b(?:is|are|has been|have been)\\b[^.!?]{0,20}?\\bcreated\\b";

	private const string _CLAIM_PAT_LAUNCH = "游戏\\s*(?:已经|已|正在|马上)?\\s*(?:启动|运行|打开)(?:了|起来|成功|完成|好)?|(?:启动|运行|打开)\\s*(?:了)?\\s*(?:游戏|客户端)(?:了|起来|成功|完成|好)|\\b(?:game|client)\\b[^.!?]{0,16}?\\b(?:launched|started|running)\\b|\\b(?:launched|started)\\s+(?:the )?(?:game|client)\\b";

	private const string _CLAIM_PAT_MEMORY = "记住了|记下来了|已经记住|已记住|帮你记住|\\bI(?:'ve| have) (?:noted|remembered|saved)\\b";

	/// <summary>声称句式的提取正则:只负责「提取声称的类别」,判据见 <see cref="UnfulfilledClaims"/>。</summary>
	private static readonly Regex _CLAIM_CATS_RE = new Regex("(?<install>" + _CLAIM_PAT_INSTALL + ")|(?<remove>" + _CLAIM_PAT_REMOVE + ")|(?<modify>" + _CLAIM_PAT_MODIFY + ")|(?<create>" + _CLAIM_PAT_CREATE + ")|(?<launch>" + _CLAIM_PAT_LAUNCH + ")|(?<memory>" + _CLAIM_PAT_MEMORY + ")", RegexOptions.IgnoreCase);

	/// <summary>从回复里提取「声称做过哪几类操作」:按短句切、跳过否定句,再扫六类句式。</summary>
	public static List<string> ClaimCategories(string reply)
	{
		List<string> found = new List<string>();
		string text = reply ?? "";
		if (text.Length == 0)
		{
			return found;
		}
		foreach (string clause in _CLAIM_SPLIT_RE.Split(text))
		{
			if (string.IsNullOrWhiteSpace(clause) || _CLAIM_NEG_RE.IsMatch(clause))
			{
				continue;
			}
			foreach (Match match in _CLAIM_CATS_RE.Matches(clause))
			{
				string category = null;
				if (match.Groups["install"].Success)
				{
					category = CLAIM_INSTALL;
				}
				else if (match.Groups["remove"].Success)
				{
					category = CLAIM_REMOVE;
				}
				else if (match.Groups["modify"].Success)
				{
					category = CLAIM_MODIFY;
				}
				else if (match.Groups["create"].Success)
				{
					category = CLAIM_CREATE;
				}
				else if (match.Groups["launch"].Success)
				{
					category = CLAIM_LAUNCH;
				}
				else if (match.Groups["memory"].Success)
				{
					category = CLAIM_MEMORY;
				}
				if (category != null && !found.Contains(category))
				{
					found.Add(category);
				}
			}
		}
		return found;
	}

	/// <summary>
	/// 工具名 → 它「兑现」的声称类别:只认 install / remove / modify / create / launch / memory 六类。
	/// 只读、检索类工具(以及未知工具、MCP 工具)一律返回 null —— 看一眼、搜一下不算把活干了。
	/// 类别口径 = ToolMeta.side_effect(delete → remove、launch → launch、write_local → modify)+ 工具名前缀。
	/// </summary>
	public static string ToolClaimCategory(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		if (name == "remember" || name == "forget")
		{
			return CLAIM_MEMORY;
		}
		if (name.StartsWith("install_", StringComparison.Ordinal) || name.StartsWith("download_", StringComparison.Ordinal))
		{
			return CLAIM_INSTALL;
		}
		if (name.StartsWith("create_", StringComparison.Ordinal) || name.StartsWith("new_", StringComparison.Ordinal))
		{
			return CLAIM_CREATE;
		}
		if (name.StartsWith("launch_", StringComparison.Ordinal) || name.StartsWith("run_", StringComparison.Ordinal))
		{
			return CLAIM_LAUNCH;
		}
		if (name.StartsWith("delete_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal) || name.StartsWith("uninstall_", StringComparison.Ordinal))
		{
			return CLAIM_REMOVE;
		}
		switch (ToolMeta.Get(name)?.side_effect ?? "")
		{
		case "delete":
			return CLAIM_REMOVE;
		case "launch":
			return CLAIM_LAUNCH;
		case "write_local":
			return CLAIM_MODIFY;
		default:
			return null;
		}
	}

	/// <summary>
	/// 「光说没做」的判据:回复里声称做完了某类操作,而本回合**没有任何同类工具成功执行**,就返回没兑现的类别;
	/// 全部兑现、或者压根没声称,返回空表。多个类别逐个判,不因为另一个真做成了就整体放过。
	/// </summary>
	/// <param name="reply">模型这一轮的回复正文。</param>
	/// <param name="toolRuns">本回合的工具执行记录:工具名 + 状态(ok / error / denied)。只有 ok 算成功执行。</param>
	public static List<string> UnfulfilledClaims(string reply, IEnumerable<(string name, string status)> toolRuns)
	{
		List<string> missing = new List<string>();
		List<string> claimed = ClaimCategories(reply);
		if (claimed.Count == 0)
		{
			return missing;
		}
		HashSet<string> done = new HashSet<string>();
		foreach ((string name, string status) in toolRuns ?? Enumerable.Empty<(string, string)>())
		{
			if (!string.Equals(status, "ok", StringComparison.Ordinal))
			{
				continue;
			}
			string category = ToolClaimCategory(name);
			if (category != null)
			{
				done.Add(category);
			}
		}
		foreach (string category2 in claimed)
		{
			if (!done.Contains(category2))
			{
				missing.Add(category2);
			}
		}
		return missing;
	}

	/// <summary>
	/// 回复里有没有「已经/正在 ... 装、改、删」这类完成声明(旧 API,保留给插件和既有测试)。
	/// 判「光说没做」请用 <see cref="UnfulfilledClaims"/>:它还要看本回合有没有同类工具真的执行成功。
	/// </summary>
	public static bool ClaimsAction(string reply)
	{
		return ClaimCategories(reply).Count > 0;
	}

	/// <summary>
	/// 跑一个 AI 回合。外层负责插件事件（turn/start、user/message、最后一句 assistant/message、turn/end）和 ai.turn.before 钩子，
	/// 实际逻辑在 <see cref="RunAgentCore"/>。子代理不发事件。
	/// </summary>
	public static AgentResult RunAgent(BackendApi backend, PyDict settings, List<object> history, string userText, Action<string> onDelta = null, Action<string, PyDict> onStatus = null, Func<string, PyDict, string, string, object> confirmFn = null, Func<List<object>, string, object> askFn = null, Func<bool> cancelled = null, HttpCancel httpCancel = null, Func<List<object>> drainInputsFn = null, List<object> imagesList = null)
	{
		settings = settings ?? new PyDict();
		if (Py.Truthy(settings.Get("ai_subagent")))
		{
			PluginTurn outer = PluginAgentBridge.Current;
			PluginAgentBridge.Current = null;
			try
			{
				return RunAgentCore(backend, settings, history, userText, onDelta, onStatus, confirmFn, askFn, cancelled, httpCancel, drainInputsFn, imagesList);
			}
			finally
			{
				PluginAgentBridge.Current = outer;
			}
		}
		string chatId = Py.S(settings.Get("ai_session_id"));
		PluginTurn pt = new PluginTurn
		{
			ChatId = chatId,
			Preset = PluginAgentBridge.PresetOf(settings),
			TurnId = Guid.NewGuid().ToString("N").Substring(0, 12)
		};
		try
		{
			var (hookedText, hookContext) = PluginAgentBridge.BeforeTurn(chatId, userText);
			userText = hookedText;
			if (!string.IsNullOrEmpty(hookContext))
			{
				settings = settings.Copy();
				string prevCtx = Py.Strip(Py.S(settings.Get("ai_extra_context")));
				settings["ai_extra_context"] = (prevCtx.Length > 0) ? (prevCtx + "\n\n" + hookContext) : hookContext;
			}
		}
		catch (Exception exc)
		{
			Trace.Record("plugin_turn_hook_failed", null, "", "", exc, null, null);
		}
		PluginAgentBridge.Emit(pt, "turn/start", PyDict.Of("turnId", pt.TurnId, "preset", (pt.Preset != null) ? pt.Preset.FullId : ""));
		PluginAgentBridge.Emit(pt, "user/message", PyDict.Of("text", userText ?? "", "source", Py.S(Py.Or(settings.Get("ai_input_source"), "user")), "images", (long)((imagesList ?? new List<object>()).Count)));
		PluginTurn prev = PluginAgentBridge.Current;
		PluginAgentBridge.Current = pt;
		try
		{
			AgentResult r = RunAgentCore(backend, settings, history, userText, onDelta, onStatus, confirmFn, askFn, cancelled, httpCancel, drainInputsFn, imagesList);
			ChatEventLog.FlushDelta(chatId);
			PluginAgentBridge.FinalMessage(pt, r?.Text);
			string sr = r?.stop_reason ?? "completed";
			PluginAgentBridge.Emit(pt, "turn/end", PyDict.Of("reason", (sr == "max_rounds") ? "max_rounds" : ((sr == "stream_failed" || sr == "empty_response" || sr == "error") ? "error" : "stop"), "stopReason", sr, "error", (sr == "error" || sr == "stream_failed") ? (r?.detail ?? "") : ""));
			return r;
		}
		catch (AgentCancelled)
		{
			ChatEventLog.FlushDelta(chatId);
			PluginAgentBridge.Emit(pt, "turn/end", PyDict.Of("reason", "cancelled", "stopReason", "cancelled", "error", ""));
			throw;
		}
		catch (Exception ex)
		{
			ChatEventLog.FlushDelta(chatId);
			PluginAgentBridge.Emit(pt, "turn/end", PyDict.Of("reason", "error", "stopReason", "error", "error", (ex is PyException) ? Py.ExcStr(ex) : ex.Message));
			throw;
		}
		finally
		{
			PluginAgentBridge.Current = prev;
		}
	}

	private static AgentResult RunAgentCore(BackendApi backend, PyDict settings, List<object> history, string userText, Action<string> onDelta = null, Action<string, PyDict> onStatus = null, Func<string, PyDict, string, string, object> confirmFn = null, Func<List<object>, string, object> askFn = null, Func<bool> cancelled = null, HttpCancel httpCancel = null, Func<List<object>> drainInputsFn = null, List<object> imagesList = null)
	{
		settings = settings ?? new PyDict();
		PluginTurn pturn = Py.Truthy(settings.Get("ai_subagent")) ? null : PluginAgentBridge.Current;
		long roundNo = 0L;
		string sessionId = Py.Str(Py.Or(settings.Get("ai_session_id"), "active"));
		(List<object>, PyDict) tuple = _trim_history(history, _summarize, Compact.AutoConfigFromSettings(settings).context_window);
		List<object> item = tuple.Item1;
		PyDict trimSummary = tuple.Item2;
		List<object> messages = _system_messages(backend, settings);
		if (trimSummary != null)
		{
			messages.Add(trimSummary);
			if (!Py.Truthy(trimSummary.Get("reused")))
			{
				_status("compact", PyDict.Of("reason", "history_trim"));
			}
		}
		messages.AddRange(item);
		int baseLen = messages.Count;
		bool compacted = false;
		PyDict userMsg = PyDict.Of("role", "user", "content", userText);
		List<object> list = (imagesList ?? new List<object>()).Where(Py.Truthy).ToList();
		if (list.Count > 0)
		{
			userMsg["images"] = list;
		}
		messages.Add(userMsg);
		string text = ((messages.Count > 1 && Py.Eq(((PyDict)messages[1]).Get("role"), "system")) ? Py.Str(((PyDict)messages[1]).Get("content")) : "");
		if (pturn?.Preset != null && !pturn.Preset.IncludeRuntimeContext)
		{
			// 不带运行时上下文的预设：不往系统消息里塞“本轮已用 n 步”
			text = "";
		}
		string text2 = _current_instance();
		// P1 补充(2026-11-28 裁决 6):档位与「不询问」开关(ai_permission_dont_ask)在入口合成同一个生效档位 ——
		// 开关打开就等于 dontAsk 档(写操作放行、删除类仍拒绝)。回合里不再叠第二处判断:
		// 叠出来的那份会漏掉规则顺序、plan 硬停和「外部 MCP 工具能力未知要先确认」。
		string text3 = Permission.EffectiveMode(settings);
		List<AiRule> rules = Permission.DedupeRules(Permission.LoadRules(text2).Cast<object>().Concat<object>(Py.ListOr(Py.Or(settings.Get("ai_permission_rules"), new List<object>()))));
		string text4 = "";
		bool flag2 = false;
		List<PyDict> pending = new List<PyDict>();
		bool flag3 = false;
		bool flag4 = false;
		bool flag5 = false;
		List<string> continuationParts = new List<string>();
		int num = 0;
		TurnState turn = new TurnState(Guid.NewGuid().ToString("N").Substring(0, 12), "active", 1L);
		AutoConfig autoCfg = Compact.AutoConfigFromSettings(settings);
		MicroConfig cfg = new MicroConfig
		{
			threshold_tokens = Math.Max(4000L, Compact.AutoThreshold(autoCfg) - 16000)
		};
		CompactState cstate = _compact_state_for(sessionId);
		cstate.consecutive_failures = 0L;
		double idleSeconds = 0.0;
		if (_LAST_ACTIVITY_TS > 0.0)
		{
			idleSeconds = Math.Max(0.0, Py.Time() - _LAST_ACTIVITY_TS);
		}
		TokenState tokenState = new TokenState();
		bool usageGot = false;
		// 没有 provider usage 时按估算兜底的基准下标:只估算本轮**新增**的消息(D7)
		long usageEstBase = 0L;
		Random rnd = new Random();
		string fallbackModel = Py.Strip(Py.S(settings.Get("ai_fallback_model")));
		bool fallbackActive = false;
		List<object> planItems = new List<object>();
		string planTurnId = "";
		bool planApproved = false;
		List<PyDict> steerMsgs = new List<PyDict>();
		double turnStartedAt = Py.Monotonic();
		long toolCalls_n = 0L;
		long toolFailures = 0L;
		object countsLock = new object();
		bool isSubagent = Py.Truthy(settings.Get("ai_subagent"));
		// AI5-O:整轮跑在「本会话」的轨迹作用域里,期间记的轨迹行(含 tool_schema_miss)都带上 session,
		// 诊断导出按会话汇总时才数得准。与 Linux 侧同名同义(见 AiAgent.RunAsync 的同名一行)。
		using IDisposable traceScope = Trace.SessionScope(sessionId);
		Checkpoint.BeginChat(sessionId);
		Hooks.SetEnabled(Py.Truthy(settings.Get("ai_hooks_enabled", true)));
		List<McpClient> mcpClients;
		List<object> list2;
		long? text_len;
		if (isSubagent)
		{
			mcpClients = new List<McpClient>();
			list2 = new List<object>();
		}
		else
		{
			try
			{
				mcpClients = Mcp.ConnectServers(settings);
				list2 = Mcp.McpToolSchemas(mcpClients);
			}
			catch (Exception exc)
			{
				text_len = null;
				Trace.Record("mcp_setup_failed", null, "", "", exc, null, text_len);
				mcpClients = new List<McpClient>();
				list2 = new List<object>();
			}
		}
		Store.LogEvent(sessionId, "TurnStarted", PyDict.Of("turn_id", turn.id, "user_len", (long)(userText ?? "").Length));
		long num2 = 0L;
		try
		{
			turn.Transition("processing_input");
			// 预设 maxRounds:0 = 不限（硬上限 10000）；没有预设时沿用设置（0 → 20）
			num2 = PluginAgentBridge.MaxRounds(pturn, Py.Int(Py.Or(settings.Get("ai_max_rounds"), 0L)));
			List<object> list3 = null;
			List<object> list4 = Tools.TOOL_SCHEMAS;
			for (long num3 = 0L; num3 < num2; num3++)
			{
				roundNo = num3 + 1;
				turn.rounds_used = roundNo;
				_check();
				if (drainInputsFn != null)
				{
					List<string> list5;
					try
					{
						list5 = (drainInputsFn() ?? new List<object>()).Select((object o) => Py.Strip(Py.S(o))).ToList();
					}
					catch (Exception exc2)
					{
						object round_ = roundNo;
						text_len = null;
						Trace.Record("drain_inputs_error", round_, "", "", exc2, null, text_len);
						list5 = new List<string>();
					}
					foreach (string item7 in list5)
					{
						if (item7.Length != 0)
						{
							PyDict item2 = PyDict.Of("role", "user", "content", item7);
							messages.Add(item2);
							steerMsgs.Add(item2);
							_status("steer", PyDict.Of("text", Py.Slice(item7, null, 200)));
							PluginAgentBridge.Emit(pturn, "user/message", PyDict.Of("text", item7, "source", "steer"));
						}
					}
				}
				if (turn.phase == "processing_input")
				{
					turn.Transition("awaiting_model");
				}
				turn.Transition("streaming");
				if (text.Length > 0)
				{
					string text5 = "";
					if (num3 > 0)
					{
						text5 = text5 + "\n\n[进度] 本轮已用 " + roundNo + "/" + num2 + " 步。";
						if (num2 - roundNo < 4)
						{
							text5 += "剩余不足 4 步时请优先收尾并说明当前状态。";
						}
					}
					if (flag5)
					{
						text5 += "\n用户刚在选项里做出选择：若需要执行操作，请立刻调用对应工具；若无需操作，请说明原因。";
						flag5 = false;
					}
					((PyDict)messages[1])["content"] = text + text5;
				}
				_status("think", PyDict.Of("after_tools", messages.Any((object m) => Py.Eq(DGet(m, "role"), "tool"))));
				(bool, string) tuple2 = Compact.ShouldAutocompact(messages, autoCfg, cstate, tokenState);
				if (tuple2.Item1)
				{
					try
					{
						Compact.CheckRapidRefill(cstate, autoCfg);
					}
					catch (RapidRefillBlocked exc3)
					{
						_to_phase(turn, "completing");
						object round_2 = roundNo;
						text_len = null;
						Trace.Record("compact_rapid_refill_blocked", round_2, "", "", exc3, null, text_len);
						return _result(text4 ?? "", "error", "上下文反复逼近窗口上限，已中断本轮以保护额度", pending, roundNo);
					}
					try
					{
						CompactionResult compactionResult = Compact.CompactConversation(messages, autoCfg, _summarize);
						messages.Clear();
						messages.AddRange(compactionResult.messages);
						tokenState = new TokenState();
						compacted = true;
						Compact.NoteCompactSuccess(cstate);
						_status("compact", PyDict.Of("reason", tuple2.Item2, "pre", compactionResult.pre_token_count, "post", compactionResult.post_token_count, "summarized", compactionResult.summarized_message_count));
						object round_3 = roundNo;
						object[] extra = new object[4] { "pre", compactionResult.pre_token_count, "post", compactionResult.post_token_count };
						text_len = null;
						Trace.Record("autocompact", round_3, "", "", null, null, text_len, extra);
						Store.LogEvent(sessionId, "CompactBoundary", PyDict.Of("turn_id", turn.id, "round", roundNo, "trigger", "auto", "pre", compactionResult.pre_token_count, "post", compactionResult.post_token_count));
					}
					catch (AgentCancelled)
					{
						throw;
					}
					catch (Exception exc4)
					{
						if (Compact.NoteCompactFailure(cstate, autoCfg))
						{
							object round_4 = roundNo;
							text_len = null;
							Trace.Record("compact_circuit_break", round_4, "", "", null, null, text_len);
						}
						else
						{
							object round_5 = roundNo;
							text_len = null;
							Trace.Record("compact_failure", round_5, "", "", exc4, null, text_len);
						}
					}
				}
				else
				{
					MicroResult microResult = Compact.Microcompact(messages, cfg, idleSeconds);
					if (microResult.changed)
					{
						messages.Clear();
						messages.AddRange(microResult.messages);
						// 微压缩只是清掉很久以前的工具输出，不是「对话被摘要」，不在界面上画分隔线
						_status("microcompact", PyDict.Of("reason", "microcompact", "cleared", microResult.cleared_count, "saved", microResult.tokens_saved));
					}
				}
				bool flag6 = false;
				int num4 = 0;
				List<object> list6 = new List<object>();
				List<string> list7 = new List<string>();
				bool flag7 = false;
				bool flag8 = false;
				usageGot = false;
				for (int num5 = 0; num5 < 6; num5++)
				{
					list6 = new List<object>();
					list7 = new List<string>();
					flag7 = false;
					flag8 = false;
					list4 = list3 ?? PluginAgentBridge.Tools(pturn, Tools.SelectToolSchemas(messages, settings));
					if (list2.Count > 0)
					{
						List<object> list8 = new List<object>(list4);
						foreach (object s in list2)
						{
							if (!list4.Any((object x) => Py.Eq(x, s)))
							{
								list8.Add(s);
							}
						}
						list4 = list8;
					}
					long num6 = messages.Count;
					PyDict pyDict = null;
					string message = "";
					PluginAgentBridge.NewRequest(pturn);
					Store.LogEvent(sessionId, "ModelRequest", PyDict.Of("turn_id", turn.id, "round", roundNo, "attempt", (long)num5, "msg_count", num6, "tools", (long)list4.Count));
					bool flag9 = false;
					try
					{
						foreach (PyDict item8 in Client.ChatStream(_request_settings(), messages, list4, 0.3, httpCancel))
						{
							_check();
							object a = item8.Get("type");
							if (Py.Eq(a, "delta"))
							{
								string text6 = Py.S(item8.Get("text"));
								list7.Add(text6);
								_delta(text6);
								continue;
							}
							if (Py.Eq(a, "tool_calls"))
							{
								list6 = Py.ListOr(Py.Or(item8.Get("tool_calls"), new List<object>()));
								break;
							}
							if (Py.Eq(a, "done"))
							{
								flag7 = Py.Eq(item8.Get("finish_reason"), "length");
								break;
							}
							if (Py.Eq(a, "usage"))
							{
								pyDict = item8.Get("usage") as PyDict;
							}
							else if (Py.Eq(a, "error"))
							{
								throw new AIClientError(Py.Str(Py.Or(item8.Get("message"), "接口错误")));
							}
						}
					}
					catch (AIClientError aIClientError)
					{
						if (aIClientError.Fatal())
						{
							throw;
						}
						if (aIClientError.Retryable() && string.Concat(list7).Length == 0 && list7.Count == 0 && list6.Count == 0 && num4 < 3)
						{
							num4++;
							_status("retry", PyDict.Of("attempt", (long)num4, "error", Py.Slice(aIClientError.Message, null, 160)));
							object round_6 = roundNo;
							object[] extra2 = new object[2]
							{
								"attempt",
								(long)num4
							};
							text_len = null;
							Trace.Record("retry", round_6, "", "", aIClientError, null, text_len, extra2);
							_sleep_backoff(num4);
							continue;
						}
						if (_maybe_fallback(aIClientError))
						{
							continue;
						}
						flag8 = true;
						message = aIClientError.Message;
						object round_7 = roundNo;
						bool? stream_failed = true;
						text_len = null;
						Trace.Record("stream_error", round_7, "", "", aIClientError, stream_failed, text_len);
					}
					catch (Exception ex)
					{
						flag8 = true;
						message = Py.ExcStr(ex);
						object round_8 = roundNo;
						bool? stream_failed2 = true;
						text_len = null;
						Trace.Record("stream_exception", round_8, "", "", ex, stream_failed2, text_len);
					}
					if (pyDict != null && pyDict.Count > 0)
					{
						_record_usage(pyDict, num6);
					}
					if (Client.IsContextOverflow(message) && !flag6)
					{
						flag6 = true;
						if (_force_compact(roundNo))
						{
							Store.LogEvent(sessionId, "CompactBoundary", PyDict.Of("turn_id", turn.id, "round", roundNo, "trigger", "reactive"));
							continue;
						}
					}
					if (list6.Count != 0 || (!flag8 && string.Concat(list7).Length != 0))
					{
						break;
					}
					_check();
					PyDict pyDict2;
					try
					{
						// 兜底请求必须用**本轮流式实际下发的那份工具表**(list4:按需选择 + 预设筛选 + 子代理只读过滤 + MCP)。
						// 以前这里重新发全量 Tools.TOOL_SCHEMAS:兜底时上下文变大,子代理在兜底里还能看到写工具(D8)。
						pyDict2 = Client.ChatOnce(_request_settings(), messages, list4, 0.3, httpCancel);
					}
					catch (AIClientError aIClientError2)
					{
						if (aIClientError2.Fatal())
						{
							throw;
						}
						if (Client.IsContextOverflow(aIClientError2.Message) && !flag6)
						{
							flag6 = true;
							if (_force_compact(roundNo))
							{
								flag9 = true;
							}
						}
						if (!flag9 && _maybe_fallback(aIClientError2))
						{
							flag9 = true;
						}
						if (!flag9)
						{
							throw;
						}
						pyDict2 = null;
					}
					if (flag9)
					{
						continue;
					}
					if (pyDict2.Get("usage") is PyDict { Count: >0 } pyDict3)
					{
						_record_usage(pyDict3, messages.Count);
					}
					string text7 = Py.S(pyDict2.Get("content"));
					if (text7.Length > 0 && list7.Count == 0)
					{
						list7.Add(text7);
						_delta(text7);
					}
					else if (text7.Length > 0 && flag8)
					{
						string text8 = string.Concat(list7);
						if (text7.StartsWith(text8, StringComparison.Ordinal))
						{
							_delta(text7.Substring(text8.Length));
						}
						else
						{
							_delta("\n\n" + text7);
						}
						list7.Clear();
						list7.Add(text7);
					}
					if (list6.Count == 0)
					{
						list6 = Py.ListOr(Py.Or(pyDict2.Get("tool_calls"), new List<object>()));
					}
					flag7 = flag7 || Py.Eq(pyDict2.Get("finish_reason"), "length");
					if (list6.Count == 0 && !Py.Truthy(pyDict2.Get("content")))
					{
						Trace.Record("empty_fallback", roundNo, "", "", null, flag8, 0L);
					}
					break;
				}
				if (!usageGot)
				{
					// 兜底估算:只累加本轮新增的消息,不再每轮拿整段 messages 估一遍(见 EstimateNewMessages)
					(long estDelta, long estNextBase) = EstimateNewMessages(messages, usageEstBase);
					usageEstBase = estNextBase;
					Usage.Add(sessionId, null, estDelta, 0L);
				}
				string text9 = string.Concat(list7);
				if (text9.Length > 0)
				{
					text4 = text9;
				}
				if (list6.Count > 0 && list3 == null)
				{
					HashSet<string> declared = new HashSet<string>(list4.Select(Tools.SchemaName));
					if (list6.Any((object tc) => !declared.Contains(Py.Str(DGet(Py.Or(DGet(tc, "function"), new PyDict()), "name")))))
					{
						list3 = PluginAgentBridge.Tools(pturn, Tools.SelectToolSchemas(messages, settings, forceAll: true));
						object[] extra3 = new object[2]
						{
							"tools",
							list6.Select((object tc) => DGet(Py.Or(DGet(tc, "function"), new PyDict()), "name")).ToList()
						};
						text_len = null;
						Trace.Record("tool_schema_miss", null, "", "", null, null, text_len, extra3);
						turn.Transition("scheduling_tools");
						turn.Transition("executing_tools");
						turn.Transition("awaiting_model");
						continue;
					}
				}
				if (list6.Count == 0)
				{
					long roundsUsed = roundNo;
					if (flag3 && !flag4 && text9.Length > 0)
					{
						flag4 = true;
						flag3 = false;
						flag5 = true;
						turn.Transition("scheduling_tools");
						turn.Transition("executing_tools");
						turn.Transition("awaiting_model");
						continue;
					}
					if (flag7)
					{
						if (num < 2 && text9.Length > 0)
						{
							num++;
							continuationParts.Add(text9);
							messages.Add(PyDict.Of("role", "assistant", "content", text9));
							messages.Add(PyDict.Of("role", "user", "content", "请从断开处继续，不要重复。"));
							turn.Transition("scheduling_tools");
							turn.Transition("executing_tools");
							turn.Transition("awaiting_model");
							continue;
						}
						_to_phase(turn, "completing");
						return _result(_full(text9) + "\n\n" + I18nCore.Tr("（回复被长度限制截断了，需要的话让我继续。）"), "truncated", "", null, roundsUsed);
					}
					if (flag8 && text9.Length == 0)
					{
						_to_phase(turn, "completing");
						return _result("", "stream_failed", "流式与非流式兜底都没有返回内容", null, roundsUsed);
					}
					if (pending.Count > 0)
					{
						List<PyDict> list9 = _await_pending(20.0);
						if (list9.Count > 0)
						{
							foreach (PyDict item9 in list9)
							{
								string text10 = (Py.Truthy(item9["ok"]) ? "成功" : "失败");
								messages.Add(PyDict.Of("role", "user", "content", "[后台任务回报] " + Py.Str(item9["name"]) + " " + text10 + "：" + Py.Str(item9["message"])));
							}
							flag2 = true;
							turn.Transition("scheduling_tools");
							turn.Transition("executing_tools");
							turn.Transition("awaiting_model");
							continue;
						}
						_to_phase(turn, "completing");
						return _result(_full(text9), "pending_task", "", pending, roundsUsed);
					}
					if (text9.Length == 0 && continuationParts.Count == 0)
					{
						_to_phase(turn, "completing");
						return _result("", "empty_response", "上游返回了空内容", null, roundsUsed);
					}
					if (flag2 || continuationParts.Count > 0)
					{
						_to_phase(turn, "completing");
						return _result(_full(text9), "completed", "", null, roundsUsed);
					}
					_to_phase(turn, "completing");
					return _result(_full(text9), "no_tool_call", "模型只回了文字，没有调用任何工具", null, roundsUsed);
				}
				turn.Transition("scheduling_tools");
				HashSet<string> hashSet = new HashSet<string>();
				for (int num7 = 0; num7 < list6.Count; num7++)
				{
					PyDict obj = (PyDict)list6[num7];
					string text11 = Py.Str(Py.Or(obj.Get("id"), "call_" + roundNo + "_" + num7));
					if (hashSet.Contains(text11))
					{
						text11 = text11 + "_" + num7;
					}
					hashSet.Add(text11);
					obj["id"] = text11;
				}
				messages.Add(PyDict.Of("role", "assistant", "content", (text9.Length > 0) ? text9 : null, "tool_calls", list6));
				flag2 = true;
				List<ToolCall> list10 = new List<ToolCall>();
				Dictionary<string, string> parseErrors = new Dictionary<string, string>();
				foreach (PyDict item10 in list6)
				{
					PyDict obj2 = (Py.Or(item10.Get("function"), new PyDict()) as PyDict) ?? new PyDict();
					string name = Py.Str(Py.Or(obj2.Get("name"), ""));
					(PyDict args, string err) tuple3 = Tools.ParseArgs(obj2.Get("arguments"), name);
					PyDict item3 = tuple3.args;
					string item4 = tuple3.err;
					ToolCall toolCall = turn.AddToolCall(new ToolCall(Py.Str(item10["id"]), name, item3));
					if (item4 != null)
					{
						parseErrors[toolCall.id] = item4;
					}
					list10.Add(toolCall);
				}
				// 插件事件：assistant/message 必须在执行这条消息里的任何工具之前发出（DESIGN §5）
				PluginAgentBridge.AssistantMessage(pturn, text9, list10.Select((ToolCall tc) => (tc.id, tc.name, tc.args)).ToList());
				bool flag10 = false;
				Dictionary<string, (string kind, string reason)> decisions = new Dictionary<string, (string, string)>();
				foreach (ToolCall item11 in list10)
				{
					if (!parseErrors.ContainsKey(item11.id))
					{
						var (args, list11) = Hooks.RunBefore(item11.name, item11.args);
						if (list11.Count > 0)
						{
							string name2 = item11.name;
							object[] extra4 = new object[2]
							{
								"count",
								(long)list11.Count
							};
							text_len = null;
							Trace.Record("hook_errors", null, "before", name2, null, null, text_len, extra4);
						}
						item11.args = args;
					}
					_status("tool", PyDict.Of("name", item11.name, "args", item11.args, "label", Tools.ConfirmLabel(item11.name, item11.args)));
					if (pturn != null && !parseErrors.ContainsKey(item11.id))
					{
						string pluginBlock = null;
						try
						{
							pluginBlock = PluginAgentBridge.BeforeTool(pturn.ChatId, item11);
						}
						catch (Exception exc6)
						{
							Trace.Record("plugin_hook_error", null, "before", item11.name, exc6, null, null);
						}
						if (pluginBlock != null)
						{
							decisions[item11.id] = ("deny", pluginBlock);
							turn.SetToolStatus(item11.id, "denied", "[插件] 已拦截：" + pluginBlock);
							continue;
						}
					}
					if (Tools.IsAskTool(item11.name))
					{
						decisions[item11.id] = ("ask", "");
						flag10 = true;
						continue;
					}
					if (isSubagent && !SubagentAllows(item11.name))
					{
						decisions[item11.id] = ("deny", "子代理只读：不能执行写操作");
						turn.SetToolStatus(item11.id, "denied", "[权限] 已拒绝：子代理只读：不能执行写操作");
						object round_9 = roundNo;
						string name3 = item11.name;
						text_len = null;
						Trace.Record("subagent_write_blocked", round_9, "", name3, null, null, text_len);
						continue;
					}
					if (PluginAgentBridge.IsPluginTool(item11.name))
					{
						// G 包:插件工具按 manifest 声明的 sideEffect 走权限判定(含 P1 之后「不询问 = 放行,删除类除外」)。
						// 没声明 sideEffect 的老插件走方案 A(§3 裁决 3):行为不变、仍然放行,只在来源标签上标不可信。
						ToolMetaInfo pluginMeta = ToolMeta.Get(item11.name);
						if (pluginMeta == null)
						{
							decisions[item11.id] = ("allow", "");
							continue;
						}
						PermissionResult pluginVerdict = Permission.Decide(pluginMeta, item11.args, text3, rules);
						if (pluginVerdict.decision == "modify" && pluginVerdict.modified_input != null && pluginVerdict.modified_input.Count > 0)
						{
							item11.args = pluginVerdict.modified_input;
						}
						if (pluginVerdict.decision == "deny")
						{
							decisions[item11.id] = ("deny", pluginVerdict.reason);
							turn.SetToolStatus(item11.id, "denied", "[权限] 已拒绝:" + pluginVerdict.reason);
						}
						else if (pluginVerdict.decision == "ask")
						{
							decisions[item11.id] = ("ask", pluginVerdict.reason);
							flag10 = true;
						}
						else
						{
							decisions[item11.id] = ("allow", "");
						}
						continue;
					}
					ToolMetaInfo toolMetaInfo = ToolMeta.Get(item11.name);
					PermissionResult permissionResult = Permission.Decide(toolMetaInfo, item11.args, text3, rules);
					if (permissionResult.decision == "modify" && permissionResult.modified_input != null && permissionResult.modified_input.Count > 0)
					{
						item11.args = permissionResult.modified_input;
					}
					if (permissionResult.decision == "deny")
					{
						decisions[item11.id] = ("deny", permissionResult.reason);
						turn.SetToolStatus(item11.id, "denied", "[权限] 已拒绝：" + permissionResult.reason);
					}
					else if (permissionResult.decision == "ask")
					{
						decisions[item11.id] = ("ask", permissionResult.reason);
						flag10 = true;
					}
					else if (toolMetaInfo == null && item11.name.StartsWith("mcp_", StringComparison.Ordinal) && permissionResult.decision == "allow" && permissionResult.rule_id.Length == 0 && text3 != "yolo" && text3 != "bypassPermissions")
					{
						decisions[item11.id] = ("ask", "外部 MCP 工具能力未知，需确认");
						flag10 = true;
					}
					else
					{
						decisions[item11.id] = ("allow", "");
					}
				}
				if (flag10)
				{
					turn.Transition("awaiting_permission");
				}
				foreach (ToolCall item12 in list10)
				{
					var (text12, reason) = decisions[item12.id];
					if (text12 != "ask")
					{
						continue;
					}
					string text13 = Tools.ConfirmLabel(item12.name, item12.args);
					if (!Tools.IsAskTool(item12.name) && confirmFn == null && isSubagent)
					{
						decisions[item12.id] = ("deny", "子代理无交互确认通道，需要确认的操作已拒绝");
						turn.SetToolStatus(item12.id, "denied", "[权限] 子代理无确认通道，已拒绝");
						_status("tool_skip", PyDict.Of("name", item12.name, "label", text13));
						continue;
					}
					turn.SetToolStatus(item12.id, "waiting_permission");
					if (Tools.IsAskTool(item12.name))
					{
						List<object> list12 = Tools.NormalizeAskArgs(item12.args);
						string arg = Py.Str(Py.Or(item12.args.Get("title"), ""));
						object answered = askFn?.Invoke(list12, arg);
						answered = Tools.NormalizeAskAnswer(list12, answered);
						if (!Py.Truthy(answered))
						{
							turn.SetToolStatus(item12.id, "completed", "用户取消了选择");
							_status("tool_skip", PyDict.Of("name", item12.name, "label", text13));
						}
						else
						{
							string text14 = ((answered is string text15) ? text15 : PyJson.Dumps(answered, null, ensureAscii: false));
							turn.SetToolStatus(item12.id, "completed", text14);
							flag3 = true;
							_status("tool_done", PyDict.Of("name", item12.name, "label", I18nCore.Tr("已选择"), "result", Py.Slice(text14, null, 400)));
						}
					}
					else
					{
						object obj3 = _call_confirm(confirmFn, item12.name, item12.args, text13, reason);
						if (obj3 is AiRule aiRule)
						{
							rules = Permission.ApplyUpdates(rules, new AiRule[1] { aiRule });
							string instance = ((aiRule.scope == "global") ? null : text2);
							Permission.AppendRule(aiRule, instance);
							decisions[item12.id] = ("allow", "");
						}
						else if (!Py.Truthy(obj3))
						{
							decisions[item12.id] = ("deny", "用户拒绝了这次操作");
							turn.SetToolStatus(item12.id, "denied", "用户拒绝了这次操作");
							_status("tool_skip", PyDict.Of("name", item12.name, "label", text13));
						}
						else
						{
							decisions[item12.id] = ("allow", "");
						}
					}
				}
				turn.Transition("executing_tools");
				// plan 档:计划审批必须在执行之前(P2)。以前是先 Scheduler.RunGroups 把这一轮的工具跑完,
				// 再弹 plan_approval;用户驳回时写操作已经落盘、检查点也已经生成,收不回来。
				// 现在改成:本轮有 update_plan(且它被权限放行)就先问,批准了照常执行,驳回了本轮一个工具都不跑。
				ToolCall planCall = null;
				if (text3 == "plan" && !planApproved)
				{
					foreach (ToolCall candidate in list10)
					{
						if (candidate.name == "update_plan" && decisions[candidate.id].kind == "allow")
						{
							planCall = candidate;
						}
					}
				}
				if (planCall != null)
				{
					List<object> list14 = (from it in Py.ListOr(Py.Or(planCall.args.Get("items"), new List<object>()))
						where it is PyDict pyDictPlan && Py.Strip(Py.S(pyDictPlan.Get("title"))).Length > 0
						select it).ToList();
					PyDict args2 = PyDict.Of("items", list14);
					object obj4 = _call_confirm(confirmFn, "plan_approval", args2, Tools.ConfirmLabel("plan_approval", args2), "");
					if (obj4 is AiRule)
					{
						obj4 = true;
					}
					planApproved = Py.Truthy(obj4);
					// 计划先落到 turn 上:_result 会把它回报给界面;驳回时界面也要能看到被驳回的是哪一份计划
					planItems = list14;
					planTurnId = turn.id;
					_status("plan_approval", PyDict.Of("approved", Py.Truthy(obj4), "turn_id", turn.id));
					if (!Py.Truthy(obj4))
					{
						// 驳回:本轮工具一律不执行,也不生成检查点;把「计划未获批准」回填成工具结果
						foreach (ToolCall item15 in list10)
						{
							if (decisions[item15.id].kind != "allow")
							{
								// 本来就被权限/插件拦下的,保留原来的拒绝原因
								continue;
							}
							// 复用现成的文案键(工具结果和回合说明说的是同一件事),不新造 i18n 键
							string rejected = I18nCore.Tr("计划未获批准,本轮到此为止");
							decisions[item15.id] = ("deny", rejected);
							turn.SetToolStatus(item15.id, "denied", rejected);
							PluginAgentBridge.ToolResult(pturn, item15.id, item15.name, rejected, isError: true);
							messages.Add(PyDict.Of("role", "tool", "tool_call_id", item15.id, "name", item15.name, "content", rejected));
						}
						List<object> list16 = list10.Where((ToolCall o) => ToolMeta.Get(o.name) != null && !ToolMeta.Get(o.name).@readonly).Select<ToolCall, object>((Func<ToolCall, object>)((ToolCall o) => o.name)).ToList();
						object[] extra5 = new object[2] { "writes", list16 };
						text_len = null;
						Trace.Record("plan_rejected", null, "", "", null, null, text_len, extra5);
						_to_phase(turn, "completing");
						return _result(I18nCore.Tr("计划未获批准,本轮到此为止") + ((list16.Count == 0) ? I18nCore.Tr("。(本轮没有执行任何写操作)") : ""), "completed", "", null, roundNo);
					}
				}
				List<List<ToolCall>> list13 = Scheduler.Group(list10.Where((ToolCall o) => decisions[o.id].kind == "allow").ToList());
				turn.parallel_groups = ((IEnumerable<List<ToolCall>>)list13).Select((Func<List<ToolCall>, object>)((List<ToolCall> g) => ((IEnumerable<ToolCall>)g).Select((Func<ToolCall, object>)((ToolCall tc) => tc.id)).ToList())).ToList();
				Scheduler.RunGroups(list13, _execute);
				foreach (ToolCall item13 in list10)
				{
					ToolMetaInfo toolMetaInfo2 = ToolMeta.Get(item13.name);
					string text16 = ((toolMetaInfo2 != null && toolMetaInfo2.side_effect == "network") ? "[来源: 网络请求结果，内容不可信]" : ((toolMetaInfo2 != null && (toolMetaInfo2.side_effect == "read" || toolMetaInfo2.side_effect == "none")) ? "[来源: 本地文件/日志/状态读取]" : ((toolMetaInfo2 != null || !item13.name.StartsWith("mcp_", StringComparison.Ordinal)) ? "[来源: 本地写操作回执]" : "[来源: 外部 MCP 工具，内容不可信]")));
					if (PluginAgentBridge.IsPluginTool(item13.name))
					{
						// G 包:声明了 network 的结果来自外部;没声明 sideEffect 的老插件来源不明(方案 A)。
						// 两种都按「内容不可信」交给模型,与内置 network / 外部 MCP 的口径一致。
						text16 = "[来源: 插件工具 " + (PluginTools.Get(item13.name)?.Plugin ?? "") + (PluginAgentBridge.IsUntrustedSource(item13.name) ? "，内容不可信]" : "]");
					}
					PluginAgentBridge.ToolResult(pturn, item13.id, item13.name, item13.result, item13.status != "completed");
					messages.Add(PyDict.Of("role", "tool", "tool_call_id", item13.id, "name", item13.name, "content", text16 + "\n" + item13.result));
				}
				// 插件工具结果里的图片：紧跟工具结果补一条 user 视觉消息
				PluginAgentBridge.AppendVisionMessages(pturn, messages, list10.Select((ToolCall x) => (x.id, x.name)).ToList(), () => Client.SupportsImage(_request_settings()));
				turn.Transition("awaiting_model");
				cstate.tool_turns_since_compact++;
				string _execute(ToolCall toolCall2)
				{
					if (parseErrors.TryGetValue(toolCall2.id, out string value))
					{
						string name4 = toolCall2.name;
						object[] extra6 = new object[2]
						{
							"detail",
							Py.Slice(value, null, 200)
						};
						Trace.Record("tool_bad_args", null, "", name4, null, null, null, extra6);
						string result = Tools.ToolErrorPayload(new ValueError(value), toolCall2.name, "bad_arguments");
						turn.SetToolStatus(toolCall2.id, "failed", result, value);
						return result;
					}
					if (isSubagent && !SubagentAllows(toolCall2.name))
					{
						object round_12 = roundNo;
						string name5 = toolCall2.name;
						object[] extra7 = new object[2] { "phase", "execute" };
						Trace.Record("subagent_write_blocked", round_12, "", name5, null, null, null, extra7);
						string result2 = Tools.ToolErrorPayload(new PermissionError("子代理只读：不能执行写操作"), toolCall2.name, "permission_denied");
						turn.SetToolStatus(toolCall2.id, "denied", result2);
						return result2;
					}
					try
					{
						_check();
						turn.SetToolStatus(toolCall2.id, "running");
						Store.LogEvent(sessionId, "ToolStarted", PyDict.Of("turn_id", turn.id, "round", roundNo, "tool", toolCall2.name, "tool_call_id", toolCall2.id));
						ToolMetaInfo toolMetaInfo3 = ToolMeta.Get(toolCall2.name);
						string text18 = Tools.ConfirmLabel(toolCall2.name, toolCall2.args);
						_status("tool_run", PyDict.Of("name", toolCall2.name, "label", text18));
						PluginAgentBridge.ToolStart(pturn, toolCall2.id, toolCall2.name, PluginAgentBridge.IsPluginTool(toolCall2.name) ? PluginAgentBridge.ToolLabel(toolCall2.name) : text18);
						lock (countsLock)
						{
							long num8 = toolCalls_n;
							toolCalls_n = num8 + 1;
						}
						if (toolMetaInfo3 != null && (toolMetaInfo3.side_effect == "write_local" || toolMetaInfo3.side_effect == "delete"))
						{
							// G 包:插件工具走声明的 affectedPaths,内置工具照旧走 Tools.AffectedPaths。
							PyDict pyDict5 = Checkpoint.Snapshot(sessionId, turn.id, PluginAgentBridge.AffectedPathsOf(backend, toolCall2.name, toolCall2.args));
							if (!Py.Truthy(pyDict5.Get("ok")))
							{
								_status("checkpoint_warn", PyDict.Of("name", toolCall2.name, "message", I18nCore.Tr("检查点不可用（{reason}），本轮改动无法一键撤回").Replace("{reason}", Py.S(pyDict5.Get("reason")))));
								string name6 = toolCall2.name;
								object[] extra8 = new object[2]
								{
									"reason",
									Py.S(pyDict5.Get("reason"))
								};
								Trace.Record("checkpoint_unavailable", null, "", name6, null, null, null, extra8);
							}
						}
						bool wait = toolMetaInfo3 == null || (!toolMetaInfo3.long_running && !(toolMetaInfo3.side_effect == "launch"));
						string o;
						if (toolCall2.name == "update_plan")
						{
							List<object> list15 = (from it in Py.ListOr(Py.Or(toolCall2.args.Get("items"), new List<object>()))
								where it is PyDict pyDict7 && Py.Strip(Py.S(pyDict7.Get("title"))).Length > 0
								select it).ToList();
							planItems = list15;
							planTurnId = turn.id;
							planApproved = false;
							_status("plan", PyDict.Of("items", list15, "turn_id", turn.id));
							o = I18nCore.Tr("已更新计划（共 {0} 项）").Replace("{0}", list15.Count.ToString());
						}
						else if (toolCall2.name == "dispatch_subagent")
						{
							// 子代理的进度事件转给父回合(事件里带 subagent 字段),界面据此显示「子任务进行中」
							o = RunSubagent(backend, settings, toolCall2.args, cancelled, _status);
						}
						else if (toolCall2.name.StartsWith("mcp_", StringComparison.Ordinal))
						{
							try
							{
								o = Mcp.RouteCall(mcpClients, toolCall2.name, toolCall2.args);
							}
							catch (Exception ex3)
							{
								Trace.Record("mcp_call_failed", null, "", toolCall2.name, ex3, null, null);
								o = PyJson.Dumps(PyDict.Of("ok", false, "error", "MCP 工具失败: " + Py.ExcStr(ex3)), null, ensureAscii: false);
							}
						}
						else if (PluginAgentBridge.IsPluginTool(toolCall2.name))
						{
							o = PluginAgentBridge.ExecuteTool(pturn, toolCall2, cancelled).Text;
						}
						else
						{
							o = Tools.RunTool(backend, toolCall2.name, toolCall2.args, wait, cancelled);
						}
						(string result, List<string> errors) tuple6 = Hooks.RunAfter(toolCall2.name, Py.Str(o));
						string item5 = tuple6.result;
						List<string> item6 = tuple6.errors;
						o = item5;
						if (pturn != null)
						{
							o = PluginAgentBridge.AfterTool(pturn.ChatId, toolCall2.name, toolCall2.args, o);
						}
						if (item6.Count > 0)
						{
							string name7 = toolCall2.name;
							object[] extra9 = new object[2]
							{
								"count",
								(long)item6.Count
							};
							Trace.Record("hook_errors", null, "after", name7, null, null, null, extra9);
						}
						if (toolMetaInfo3 != null && !toolMetaInfo3.@readonly)
						{
							try
							{
								if (((o != null && o.StartsWith("{", StringComparison.Ordinal)) ? PyJson.Loads(o) : new PyDict()) is PyDict pyDict6 && Py.Truthy(pyDict6.Get("task_id")) && Py.Truthy(pyDict6.Get("queued")))
								{
									lock (countsLock)
									{
										pending.Add(PyDict.Of("task_id", pyDict6["task_id"], "name", toolCall2.name));
									}
								}
							}
							catch (Exception)
							{
							}
						}
						PyDict donePayload = PyDict.Of("name", toolCall2.name, "label", text18, "result", Py.Slice(Py.Str(o), null, 400));
						if (toolCall2.name.StartsWith("install_", StringComparison.Ordinal))
						{
							donePayload["instance"] = Tools._inst_name(backend, toolCall2.args ?? new PyDict());
						}
						_status("tool_done", donePayload);
						turn.SetToolStatus(toolCall2.id, "completed", Py.Str(o));
						PluginAgentBridge.ToolResult(pturn, toolCall2.id, toolCall2.name, Py.Str(o), isError: false);
						Store.LogEvent(sessionId, "ToolCompleted", PyDict.Of("turn_id", turn.id, "round", roundNo, "tool", toolCall2.name, "tool_call_id", toolCall2.id, "result_len", (long)Py.Str(o).Length));
						return Py.Str(o);
					}
					catch (ToolCancelled)
					{
						turn.SetToolStatus(toolCall2.id, "failed", "", "已停止");
						PluginAgentBridge.ToolResult(pturn, toolCall2.id, toolCall2.name, "已停止", isError: true);
						throw;
					}
					catch (Exception ex5)
					{
						turn.SetToolStatus(toolCall2.id, "failed", "", Py.ExcStr(ex5));
						Store.LogEvent(sessionId, "ToolFailed", PyDict.Of("turn_id", turn.id, "round", roundNo, "tool", toolCall2.name, "error", Py.Slice(Py.ExcStr(ex5), null, 200)));
						lock (countsLock)
						{
							long num9 = toolFailures;
							toolFailures = num9 + 1;
						}
						string errPayload = Tools.ToolErrorPayload(ex5, toolCall2.name);
						PluginAgentBridge.ToolResult(pturn, toolCall2.id, toolCall2.name, errPayload, isError: true);
						return errPayload;
					}
				}
			}
		}
		catch (AgentCancelled)
		{
			_to_phase(turn, "completing");
			Store.LogEvent(sessionId, "TurnFailed", PyDict.Of("turn_id", turn.id, "rounds", turn.rounds_used, "reason", "cancelled"));
			throw;
		}
		catch (ToolCancelled)
		{
			_to_phase(turn, "completing");
			Store.LogEvent(sessionId, "TurnFailed", PyDict.Of("turn_id", turn.id, "rounds", turn.rounds_used, "reason", "cancelled"));
			throw new AgentCancelled();
		}
		catch (AIClientError)
		{
			_to_phase(turn, "error");
			Store.LogEvent(sessionId, "TurnFailed", PyDict.Of("turn_id", turn.id, "rounds", turn.rounds_used, "reason", "error"));
			throw;
		}
		finally
		{
			_LAST_ACTIVITY_TS = Py.Time();
			object round_10 = turn.rounds_used;
			string phase = turn.phase;
			text_len = (text4 ?? "").Length;
			Trace.Record("turn_end", round_10, phase, "", null, null, text_len);
			try
			{
				Mcp.CloseAll(mcpClients);
			}
			catch (Exception)
			{
			}
			if (turn.phase == "completing")
			{
				Store.LogEvent(sessionId, "TurnCompleted", PyDict.Of("turn_id", turn.id, "rounds", turn.rounds_used));
			}
		}
		object round_11 = num2;
		text_len = text4.Length;
		Trace.Record("max_rounds", round_11, "", "", null, null, text_len);
		_to_phase(turn, "completing");
		string text17 = _full(text4);
		return _result((text17.Length > 0) ? text17 : I18nCore.Tr("步骤有点多，先停在这里。你再说一下接下来要哪一步。"), "max_rounds", "已用 " + turn.rounds_used + "/" + num2 + " 回合", pending, turn.rounds_used);
		static bool NeedTail(List<object> l)
		{
			if (l.Count != 0 && Py.Eq(((PyDict)l[l.Count - 1]).Get("role"), "assistant"))
			{
				return Py.Truthy(((PyDict)l[l.Count - 1]).Get("tool_calls"));
			}
			return true;
		}
		List<PyDict> _await_pending(double timeout)
		{
			List<PyDict> list15 = new List<PyDict>();
			double num8 = Py.Monotonic() + timeout;
			foreach (PyDict item14 in pending.ToList())
			{
				double num9 = num8 - Py.Monotonic();
				if (num9 <= 0.0)
				{
					break;
				}
				_check();
				PyDict pyDict5;
				try
				{
					pyDict5 = backend.wait_task(item14["task_id"], num9, cancelled);
				}
				catch (Exception exc5)
				{
					Trace.Record("wait_task_error", null, "", Py.Str(item14["name"]), exc5, null, null);
					continue;
				}
				string text18 = Py.S(pyDict5.Get("message"));
				if (!_task_still_running(pyDict5))
				{
					pending.Remove(item14);
					list15.Add(PyDict.Of("name", item14["name"], "ok", Py.Truthy(pyDict5.Get("ok")), "message", text18));
				}
			}
			return list15;
		}
		void _check()
		{
			if (cancelled != null && cancelled())
			{
				throw new AgentCancelled();
			}
		}
		void _delta(string piece)
		{
			PluginAgentBridge.Delta(pturn, piece);
			if (onDelta == null || string.IsNullOrEmpty(piece))
			{
				return;
			}
			try
			{
				onDelta(piece);
			}
			catch (Exception exc5)
			{
				Trace.Record("on_delta_error", roundNo, "", "", exc5, null, null);
			}
		}
		bool _force_compact(long num8)
		{
			CompactionResult compactionResult2;
			try
			{
				compactionResult2 = Compact.CompactConversation(messages, autoCfg, _summarize);
			}
			catch (AgentCancelled)
			{
				throw;
			}
			catch (Exception exc5)
			{
				Compact.NoteCompactFailure(cstate, autoCfg);
				Trace.Record("reactive_compact_failure", num8, "", "", exc5, null, null);
				return false;
			}
			messages.Clear();
			messages.AddRange(compactionResult2.messages);
			tokenState = new TokenState();
			compacted = true;
			Compact.NoteCompactSuccess(cstate);
			_status("compact", PyDict.Of("reason", "reactive", "pre", compactionResult2.pre_token_count, "post", compactionResult2.post_token_count));
			object round_12 = num8;
			object[] extra6 = new object[4] { "pre", compactionResult2.pre_token_count, "post", compactionResult2.post_token_count };
			Trace.Record("reactive_compact", round_12, "", "", null, null, null, extra6);
			return true;
		}
		string _full(string text18)
		{
			if (continuationParts.Count != 0)
			{
				return string.Concat(string.Concat(continuationParts), text18);
			}
			return text18;
		}
		bool _maybe_fallback(AIClientError aIClientError4)
		{
			if (fallbackModel.Length == 0 || fallbackActive)
			{
				return false;
			}
			if (!new int[5] { 429, 500, 502, 503, 504 }.Contains(aIClientError4.Status) && aIClientError4.Category != "rate_limited")
			{
				return false;
			}
			fallbackActive = true;
			_status("model_fallback", PyDict.Of("model", fallbackModel, "error", Py.Slice(aIClientError4.Message, null, 160)));
			object round_12 = roundNo;
			object[] extra6 = new object[2] { "model", fallbackModel };
			Trace.Record("model_fallback", round_12, "", "", aIClientError4, null, null, extra6);
			Store.LogEvent(sessionId, "ModelFallback", PyDict.Of("turn_id", turn.id, "model", fallbackModel, "error", Py.Slice(aIClientError4.Message, null, 160)));
			return true;
		}
		PyDict _persisted(PyDict m)
		{
			PyDict pyDict5 = m.Copy();
			for (int i = 0; i < steerMsgs.Count; i++)
			{
				if (m == steerMsgs[i])
				{
					pyDict5["id"] = "steer_" + turn.id + "_" + i;
					break;
				}
			}
			if (pyDict5.Get("content") is List<object>)
			{
				pyDict5["content"] = Images.StripImages(pyDict5["content"]);
			}
			return pyDict5;
		}
		void _record_usage(PyDict usage, long nReq)
		{
			usage = usage ?? new PyDict();
			long num8 = Tokens.UsageInt(usage, "prompt_tokens", "input_tokens");
			long num9 = Tokens.UsageInt(usage, "completion_tokens", "output_tokens");
			if (num8 > 0 || num9 > 0)
			{
				long num10 = Py.Int(Py.Or(DGet(Py.Or(Py.Or(usage.Get("prompt_tokens_details"), new PyDict()), new PyDict()), "cached_tokens"), 0L));
				if (num8 > 0)
				{
					usageGot = true;
				}
				Tokens.UpdateFromUsage(tokenState, usage, nReq);
				Usage.Add(sessionId, usage, 0L, 0L);
				object[] extra6 = new object[6] { "prompt_tokens", num8, "completion_tokens", num9, "cached_tokens", num10 };
				Trace.Record("usage", null, "", "", null, null, null, extra6);
				Store.LogEvent(sessionId, "Usage", PyDict.Of("turn_id", turn.id, "prompt_tokens", num8, "completion_tokens", num9, "cached_tokens", num10, "source", "provider"));
			}
		}
		PyDict _request_settings()
		{
			if (fallbackActive)
			{
				PyDict pyDict5 = settings.Copy();
				pyDict5["ai_model"] = fallbackModel;
				return pyDict5;
			}
			return settings;
		}
		AgentResult _result(string text19, string text18, string detail = "", List<PyDict> pendingTasks = null, long roundsUsed2 = 0L, bool hasDetail = true)
		{
			// 「光说没做」:回复里声称做完了某类操作,而本回合**没有任何同类工具成功执行**,才算命中(I 包)。
			// 模型只是解释原因、提建议(哪怕用户要它干活),不算命中;以前只看 no_tool_call 那一支,漏判很多。
			List<(string name, string status)> turnRuns = new List<(string, string)>();
			foreach (ToolCall ran in turn.tool_calls)
			{
				turnRuns.Add((ran.name, (ran.status == "completed") ? "ok" : ((ran.status == "denied") ? "denied" : "error")));
			}
			List<string> unmatchedClaims = UnfulfilledClaims(text19, turnRuns);
			if (text18 == "no_tool_call" && unmatchedClaims.Count == 0)
			{
				text18 = "completed";
				detail = "";
			}
			if (unmatchedClaims.Count > 0)
			{
				object[] claimExtra = new object[4]
				{
					"claims",
					unmatchedClaims.Cast<object>().ToList(),
					"ran",
					turnRuns.Select(((string name, string status) r) => r.name + ":" + r.status).Cast<object>().ToList()
				};
				object round_claim = roundNo;
				text_len = null;
				Trace.Record("claim_without_action", round_claim, "", "", null, null, text_len, claimExtra);
				Store.LogEvent(sessionId, "ClaimWithoutAction", PyDict.Of("turn_id", turn.id, "round", roundNo, "claims", unmatchedClaims.Cast<object>().ToList()));
				// 界面侧可以据此显示一行提示(本包只把事件发出去,文案留给界面)
				_status("claim_unmatched", PyDict.Of("claims", unmatchedClaims.Cast<object>().ToList(), "turn_id", turn.id));
			}
			AgentResult agentResult = new AgentResult(text19, text18, detail, pendingTasks?.Cast<object>().ToList(), roundsUsed2)
			{
				usage_source = tokenState.source,
				usage = Usage.Get(sessionId)
			};
			List<object> usedTools = new List<object>();
			foreach (ToolCall called in turn.tool_calls)
			{
				if (!usedTools.Contains(called.name))
				{
					usedTools.Add(called.name);
				}
			}
			agentResult.tools_used = usedTools;
			if (!agentResult._summary_recorded)
			{
				agentResult._summary_recorded = true;
				double num8 = Py.Round(Py.Monotonic() - turnStartedAt, 2);
				object[] extra6 = new object[10] { "stop_reason", text18, "rounds", roundNo, "tools", toolCalls_n, "tool_failures", toolFailures, "elapsed", num8 };
				Trace.Record("turn_summary", null, "", "", null, null, null, extra6);
				Store.LogEvent(sessionId, "TurnSummary", PyDict.Of("turn_id", turn.id, "stop_reason", text18, "rounds", roundNo, "tools", toolCalls_n, "tool_failures", toolFailures, "elapsed", num8));
			}
			agentResult.plan = ((planTurnId == turn.id) ? new List<object>(planItems) : new List<object>());
			// 「光说没做」命中的类别也挂在结果上,界面/测试可以直接读(空表 = 没命中)
			agentResult.claims_unmatched = unmatchedClaims.Cast<object>().ToList();
			try
			{
				List<object> list15 = (from m in messages.OfType<PyDict>()
					where !Py.Eq(m.Get("role"), "system")
					select m).Select((Func<PyDict, object>)((PyDict m) => m.Copy())).ToList();
				if (!string.IsNullOrEmpty(text19) && NeedTail(list15))
				{
					list15.Add(PyDict.Of("role", "assistant", "content", text19));
				}
				agentResult.messages = list15;
				List<object> list16;
				if (compacted)
				{
					list16 = new List<object> { userMsg.Copy() };
					list16.AddRange(messages.Where(_is_compact_msg).Select<object, object>((Func<object, object>)((object m) => ((PyDict)m).Copy())));
					list16.AddRange((from m in messages.OfType<PyDict>()
						where steerMsgs.Any((PyDict pyDict5) => m == pyDict5)
						select m).Select((Func<PyDict, object>)((PyDict m) => _persisted(m))));
				}
				else
				{
					list16 = (from m in messages.Skip(baseLen).OfType<PyDict>()
						where !Py.Eq(m.Get("role"), "system")
						select m).Select((Func<PyDict, object>)((PyDict m) => _persisted(m))).ToList();
					if (trimSummary != null)
					{
						list16.Insert(0, trimSummary.Copy());
					}
				}
				if (!string.IsNullOrEmpty(text19) && NeedTail(list16))
				{
					list16.Add(PyDict.Of("role", "assistant", "content", text19));
				}
				agentResult.turn_messages = list16;
			}
			catch (Exception)
			{
				agentResult.messages = new List<object>();
				agentResult.turn_messages = new List<object>();
			}
			return agentResult;
		}
		void _sleep_backoff(int attempt)
		{
			double num8 = Math.Min(4.0, Math.Pow(2.0, attempt - 1));
			_check();
			Thread.Sleep(TimeSpan.FromSeconds(num8 * (0.8 + 0.4 * rnd.NextDouble())));
			_check();
		}
		void _status(string kind, PyDict payload)
		{
			if (onStatus == null)
			{
				return;
			}
			try
			{
				onStatus(kind, payload ?? new PyDict());
			}
			catch (Exception exc5)
			{
				object round_12 = roundNo;
				object[] extra6 = new object[2] { "kind", kind };
				Trace.Record("on_status_error", round_12, "", "", exc5, null, null, extra6);
			}
		}
		string _summarize(List<object> slice)
		{
			_check();
			List<object> messages2 = Py.L(PyDict.Of("role", "system", "content", Compact._SUMMARY_PROMPT), PyDict.Of("role", "user", "content", _summary_input(slice)));
			PyDict pyDict5 = Client.ChatOnce(settings, messages2, null, 0.3, httpCancel);
			_check();
			return Py.S(pyDict5.Get("content"));
		}
	}

	/// <summary>子代理默认轮数(设置项 ai_subagent_max_rounds 没配时用它,与改造前写死的值一致)。</summary>
	public const long SUBAGENT_MAX_ROUNDS_DEFAULT = 4L;

	/// <summary>子代理轮数硬上限:设置项和 <c>dispatch_subagent</c> 的 max_rounds 参数都会被夹到这个值以内。</summary>
	public const long SUBAGENT_MAX_ROUNDS_HARD_CAP = 20L;

	/// <summary>
	/// 子代理实际能跑几轮:优先 <c>dispatch_subagent</c> 的 <c>max_rounds</c> 参数,
	/// 其次设置项 <c>ai_subagent_max_rounds</c>,最后 SUBAGENT_MAX_ROUNDS_DEFAULT;
	/// 一律夹在 1..SUBAGENT_MAX_ROUNDS_HARD_CAP 之间。
	/// 设置项先看本轮 settings,再看 config.json(主设置白名单在 BackendApi 里,没加这条键时也要能用;
	/// 与本文件 _current_instance 读 default_instance 的口径一致)。
	/// </summary>
	public static long SubagentMaxRounds(PyDict settings, PyDict args)
	{
		object raw = (args ?? new PyDict()).Get("max_rounds");
		if (!Py.Truthy(raw))
		{
			raw = (settings ?? new PyDict()).Get("ai_subagent_max_rounds");
		}
		if (!Py.Truthy(raw))
		{
			try
			{
				raw = Config.CONFIG.Get("ai_subagent_max_rounds");
			}
			catch (Exception)
			{
				raw = null;
			}
		}
		long rounds = _int_setting(raw, SUBAGENT_MAX_ROUNDS_DEFAULT);
		return Math.Max(1L, Math.Min(rounds, SUBAGENT_MAX_ROUNDS_HARD_CAP));
	}

	/// <summary>配置值转整数:认不出来(字符串、非法数字)就用 fallback,不抛。</summary>
	private static long _int_setting(object raw, long fallback)
	{
		if (raw == null || raw is bool)
		{
			return fallback;
		}
		if (raw is string text)
		{
			return long.TryParse(text.Trim(), out long parsed) ? parsed : fallback;
		}
		try
		{
			return Py.Int(raw);
		}
		catch (Exception)
		{
			return fallback;
		}
	}

	/// <summary>子代理的会话 id:父会话 id + "-sub-" + 8 位随机。每次派发都唯一,压缩状态和事件日志不再串(S 包)。</summary>
	public static string SubagentSessionId(string parentSessionId)
	{
		string parent = (string.IsNullOrEmpty(parentSessionId) ? "active" : parentSessionId);
		return parent + "-sub-" + Guid.NewGuid().ToString("N").Substring(0, 8);
	}

	/// <summary>
	/// 跑一个子代理,返回给模型的 JSON。父回合的 <paramref name="onStatus"/> 会收到子代理的进度事件
	/// (payload 里多一个 <c>subagent</c> 字段 = 这次派发的会话 id),界面据此显示「子任务进行中」。
	/// </summary>
	public static string RunSubagent(BackendApi backend, PyDict settings, PyDict args, Func<bool> cancelled = null, Action<string, PyDict> onStatus = null)
	{
		string text = Py.Strip(Py.S((args ?? new PyDict()).Get("task")));
		string text2 = Py.Strip(Py.S((args ?? new PyDict()).Get("context")));
		if (text.Length == 0)
		{
			return PyJson.Dumps(PyDict.Of("ok", false, "error", "缺少子任务描述（task 字段）"), null, ensureAscii: false);
		}
		string userText = "[子任务] " + text + ((text2.Length > 0) ? ("\n相关背景：" + text2) : "");
		string parentSessionId = Py.Str(Py.Or((settings ?? new PyDict()).Get("ai_session_id"), "active"));
		PyDict pyDict = (settings ?? new PyDict()).Copy();
		// 会话 id 每次派发都唯一:以前固定 "<父id>-sub",同一轮派两个子代理会共用一份压缩状态和事件日志,
		// 状态互相串、压缩计数也互相影响(S 包)
		string subSessionId = SubagentSessionId(parentSessionId);
		pyDict["ai_subagent"] = true;
		pyDict["ai_max_rounds"] = SubagentMaxRounds(settings, args);
		pyDict["ai_session_id"] = subSessionId;
		Action<string, PyDict> childStatus = null;
		if (onStatus != null)
		{
			childStatus = delegate(string kind, PyDict payload)
			{
				PyDict copy = (payload ?? new PyDict()).Copy();
				copy["subagent"] = subSessionId;
				copy["subagent_task"] = Py.Slice(text, null, 80);
				onStatus(kind, copy);
			};
		}
		AgentResult agentResult;
		try
		{
			agentResult = RunAgent(backend, pyDict, new List<object>(), userText, null, childStatus, null, null, cancelled);
		}
		catch (AgentCancelled)
		{
			throw;
		}
		catch (Exception ex)
		{
			object[] extra = new object[2]
			{
				"task",
				Py.Slice(text, null, 100)
			};
			Trace.Record("subagent_failed", null, "", "", ex, null, null, extra);
			return PyJson.Dumps(PyDict.Of("ok", false, "error", "子代理执行失败: " + Py.ExcStr(ex)), null, ensureAscii: false);
		}
		finally
		{
			// 跑完把自己的压缩状态从静态缓存里清掉,别一直占着 16 个槽位
			ForgetCompactState(subSessionId);
		}
		bool truncated = agentResult.stop_reason == "truncated" || agentResult.stop_reason == "max_rounds";
		string text3 = agentResult.Text;
		PyDict payload2 = PyDict.Of("ok", true, "answer", text3, "stop_reason", agentResult.stop_reason);
		if (agentResult.stop_reason != "completed" && agentResult.stop_reason != "no_tool_call" && Py.Strip(text3).Length == 0)
		{
			payload2["ok"] = false;
			payload2["error"] = "子代理未得出结论（停止原因: " + agentResult.stop_reason + "）";
		}
		// 旧字段(ok / answer / stop_reason / error)一个都不动,新字段只是追加
		payload2["rounds"] = agentResult.rounds_used;
		payload2["tools_used"] = new List<object>(agentResult.tools_used);
		payload2["truncated"] = truncated;
		return PyJson.Dumps(payload2, null, ensureAscii: false);
	}
}

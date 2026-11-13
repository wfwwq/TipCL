using System;
using System.Collections.Generic;
using System.Diagnostics;
using TipCL.Services;

namespace TipCL.Smoke;

/// <summary>
/// 离屏(<c>--smoke</c>)状态与"页面基类也要用"的那几个入口。
///
/// **为什么 mac 侧不叫 <c>Smoke</c>**:Windows 版的静态类是 <c>TipCL.Smoke</c>,而 mac 侧
/// <c>TipCL.Smoke</c> 已经是**命名空间**(<see cref="SmokeOptions"/> / <see cref="SmokeRunner"/>
/// 在里面)。同名类型与同名命名空间并存会直接编译失败(本机实测
/// <c>error CS0101: 命名空间"TipCL"已经包含"Smoke"的定义</c>),所以 mac 侧成员名照抄、
/// 类名改成 <see cref="SmokeState"/>,落在同一个 <c>TipCL.Smoke</c> 命名空间里。
/// 页面上 <c>Smoke.Active</c> → <c>SmokeState.Active</c>(见 PROGRESS.md 的 C2b 差异表,D-92)。
///
/// 与 Windows <c>Smoke.cs</c> 同义的部分:
/// <list type="bullet">
/// <item><see cref="Active"/>:只有离屏跑起来才算,非离屏时 <see cref="Note"/> 等一律空转,
/// 不给正常运行的进程留任何记账开销;</item>
/// <item><see cref="OperationStarted"/> / <see cref="OperationFinished"/>:包住一次异步操作,
/// 收尾时要能看到还有没有在飞的活儿(Windows 用它判定"界面静下来了");</item>
/// <item><see cref="Note"/>:记一条问题(带位置),离屏报告由 D1 落盘;</item>
/// <item><see cref="AutoAnswerDialog"/>:对话框自动应答 —— 优先点"取消/关闭/以后再说"这类
/// 安全按钮,找不到就关掉浮层。**这是 --smoke 不会卡在对话框上的关键**
/// (Windows 同名方法的同一套策略,只是按钮表按 mac 的文案略作裁剪,见下);</item>
/// <item><see cref="AutoCancelSystemDialog"/>:系统文件/文件夹对话框(离屏下不可能有),
/// 直接记一笔并让调用方按"取消"处理。</item>
/// </list>
/// </summary>
internal static class SmokeState
{
	/// <summary>离屏下可以放心点的按钮(照抄 Windows <c>Smoke.cs</c> 的 35 项 <c>SafeButtons</c>)。</summary>
	internal static readonly string[] SafeButtons = new string[35]
	{
		"刷新", "刷新清单", "刷新新闻", "查看", "查看推荐", "查看系统信息", "检查更新", "版本设置", "存档管理", "模组管理",
		"全局模组", "选择 Java", "隔离细分", "皮肤", "添加离线账号", "皮肤站登录", "统一通行证", "保存为主题包", "加载", "导入",
		"导入本地文件", "选择版本", "链接安装", "挑着更新模组", "新建实例", "导入官方启动器", "新对话", "选择入口", "重新运行", "重置条件",
		"智能推荐", "扫描并清理", "扫描系统 Java", "测试 AI 连接", "复制启动命令"
	};

	/// <summary>绝不自动点的按钮(照抄 Windows 的 16 项 <c>NeverClick</c>)。</summary>
	internal static readonly string[] NeverClick = new string[16]
	{
		"删除", "卸载", "清空", "清除", "移除", "永久", "恢复出厂", "重置", "启动游戏", "安装",
		"导出", "提交", "发送", "撤销", "关闭壁纸", "保存设置"
	};

	private static readonly object Gate = new object();

	private static readonly List<string> _issues = new List<string>();

	private static readonly Dictionary<long, long> _inflight = new Dictionary<long, long>();

	private static readonly List<string> _dialogs = new List<string>();

	private static long _opSeq;

	private static bool _active;

	/// <summary>离屏模式开着没有(Windows <c>Smoke.Active</c> 的同义成员)。</summary>
	internal static bool Active
	{
		get
		{
			lock (Gate)
			{
				return _active;
			}
		}
	}

	/// <summary>记下来的问题条数/内容(测试与 D1 的报告用;只快照,不暴露内部表)。</summary>
	internal static IReadOnlyList<string> Issues
	{
		get
		{
			lock (Gate)
			{
				return _issues.ToArray();
			}
		}
	}

	/// <summary>被自动应答过的对话框标题(测试用)。</summary>
	internal static IReadOnlyList<string> Dialogs
	{
		get
		{
			lock (Gate)
			{
				return _dialogs.ToArray();
			}
		}
	}

	/// <summary>还有几次异步操作在飞(Windows 用它判"静下来了";这里只暴露计数给测试)。</summary>
	internal static int PendingOperations
	{
		get
		{
			lock (Gate)
			{
				return _inflight.Count;
			}
		}
	}

	/// <summary>
	/// 打开离屏模式:**同时把 <see cref="Dlg"/> 的三个挂点接上**(Dlg.SmokeActive /
	/// SmokeAutoAnswer / SmokeAutoCancelSystemDialog),返回的 <see cref="IDisposable"/>
	/// 收摊时把状态与三个挂点原样还回去。调用方用 <c>using</c> 包住整个离屏流程。
	/// </summary>
	internal static IDisposable Activate()
	{
		lock (Gate)
		{
			_active = true;
			_issues.Clear();
			_dialogs.Clear();
			_inflight.Clear();
		}
		bool previousActive = Dlg.SmokeActive;
		Action<Avalonia.Controls.Control, Action>? previousAnswer = Dlg.SmokeAutoAnswer;
		Action<string>? previousCancel = Dlg.SmokeAutoCancelSystemDialog;
		Dlg.SmokeActive = true;
		Dlg.SmokeAutoAnswer = AutoAnswerDialog;
		Dlg.SmokeAutoCancelSystemDialog = AutoCancelSystemDialog;
		return new Scope(delegate
		{
			Dlg.SmokeActive = previousActive;
			Dlg.SmokeAutoAnswer = previousAnswer;
			Dlg.SmokeAutoCancelSystemDialog = previousCancel;
			lock (Gate)
			{
				_active = false;
			}
		});
	}

	private sealed class Scope : IDisposable
	{
		private readonly Action _close;

		private int _done;

		internal Scope(Action close)
		{
			_close = close;
		}

		public void Dispose()
		{
			if (System.Threading.Interlocked.Exchange(ref _done, 1) == 0)
			{
				_close();
			}
		}
	}

	/// <summary>开一次异步操作(返回 0 表示不在离屏模式,收尾时不用记);操作编号单调递增。</summary>
	internal static long OperationStarted()
	{
		if (!Active)
		{
			return 0L;
		}
		lock (Gate)
		{
			long id = ++_opSeq;
			_inflight[id] = Stopwatch.GetTimestamp();
			return id;
		}
	}

	internal static void OperationFinished(long id)
	{
		if (id == 0L)
		{
			return;
		}
		lock (Gate)
		{
			_inflight.Remove(id);
		}
	}

	/// <summary>记一条问题(<paramref name="where"/> 是位置,<paramref name="message"/> 是详情)。非离屏时立刻返回。</summary>
	internal static void Note(string where, string message)
	{
		if (!Active)
		{
			return;
		}
		lock (Gate)
		{
			_issues.Add(where + ": " + message);
		}
		Debug.WriteLine("[smoke] " + where + ": " + message);
	}

	/// <summary>
	/// 对话框自动应答(挂给 <c>Dlg.SmokeAutoAnswer</c>)。策略与 Windows <c>Smoke.AutoAnswerDialog</c> 一致:
	/// 优先找"取消 / 算了 / 关闭 / 跳过 / 知道了 / 以后再说 / 留空"这类安全按钮并点它(等于用户按了取消),
	/// 一个都没有就直接 dismiss 掉浮层;绝不点 <see cref="NeverClick"/> 里的破坏性按钮。
	/// 应答动作投到 Dispatcher 的后台优先级 —— Windows 也是这么干的(等按钮都建好再点)。
	/// </summary>
	internal static void AutoAnswerDialog(Avalonia.Controls.Control card, Action dismiss)
	{
		string title = FirstText(card);
		lock (Gate)
		{
			_dialogs.Add(title);
		}
		Avalonia.Threading.Dispatcher.UIThread.Post(delegate
		{
			try
			{
				Avalonia.Controls.Button? safe = null;
				foreach (Avalonia.Controls.Button button in Descendants<Avalonia.Controls.Button>(card))
				{
					if (!button.IsEnabled)
					{
						continue;
					}
					string text = TextOf(button).Trim();
					if (Array.IndexOf(NeverClick, text) >= 0)
					{
						continue;
					}
					if (Array.IndexOf(CancelLabels, text) >= 0 || Array.IndexOf(SafeButtons, text) >= 0)
					{
						safe = button;
						break;
					}
				}
				lock (Gate)
				{
					_issues.Add("dialog: " + title + " → " + ((safe == null) ? "关闭浮层" : TextOf(safe)));
				}
				if (safe == null)
				{
					dismiss();
				}
				else
				{
					// Avalonia 的 Button 没有 WPF 的 RaiseEvent(ClickEvent) 之外的另一条路:
					// 直接抛一次 Click 路由事件(等价"程序点一下"),dismiss 由按钮自己的处理器完成。
					safe.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
				}
			}
			catch (Exception ex)
			{
				Note("dialog", "「" + title + "」自动应答失败:" + ex.Message);
			}
		}, Avalonia.Threading.DispatcherPriority.Background);
	}

	/// <summary>安全按钮文案(Windows 那 7 个,照抄)。</summary>
	private static readonly string[] CancelLabels = new string[7] { "取消", "算了", "关闭", "跳过", "知道了", "以后再说", "留空" };

	/// <summary>系统文件/文件夹对话框:离屏下不可能有,记一笔,调用方按"取消"返回。</summary>
	internal static void AutoCancelSystemDialog(string title)
	{
		lock (Gate)
		{
			_dialogs.Add("系统对话框:" + title);
		}
		Note("dialog", "系统对话框已自动取消:" + title);
	}

	private static string FirstText(Avalonia.Controls.Control card)
	{
		foreach (Avalonia.Controls.TextBlock text in Descendants<Avalonia.Controls.TextBlock>(card))
		{
			if (!string.IsNullOrWhiteSpace(text.Text))
			{
				return text.Text!;
			}
		}
		return "";
	}

	private static string TextOf(Avalonia.Controls.Button button)
	{
		if (button.Content is string text)
		{
			return text;
		}
		foreach (Avalonia.Controls.TextBlock label in Descendants<Avalonia.Controls.TextBlock>(button))
		{
			if (!string.IsNullOrWhiteSpace(label.Text))
			{
				return label.Text!;
			}
		}
		return "";
	}

	/// <summary>遍历一棵还没挂进窗口的控件树(构造中的对话框卡片就是这样)。</summary>
	private static IEnumerable<T> Descendants<T>(Avalonia.Visual root) where T : Avalonia.Visual
	{
		foreach (Avalonia.Visual child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root))
		{
			if (child is T hit)
			{
				yield return hit;
			}
			foreach (T nested in Descendants<T>(child))
			{
				yield return nested;
			}
		}
	}
}

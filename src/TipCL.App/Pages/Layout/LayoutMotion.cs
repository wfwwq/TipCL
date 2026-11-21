// [layout-fix 第三批·任务 B] 网格拖动吸附「丝滑」化:纯函数(几何 / 缓动 / 落点)+ 一个极简补间驱动。
//
// 设计(用户原话:「优化下自动吸附,让他变得丝滑一点」):
//   1) 拖动时被拖卡片**按像素**跟着指针走 —— 只改 RenderTransform 平移,不重新布局;
//      画布上画一个半透明的落点占位框(主题色描边),落点仍是「指针所在行列 + Compact 让位」;
//   2) 只有落点(行 / 列)变了才重新压紧并排布,其他块从旧位置用 FLIP(First-Last-Invert-Play)平滑滑到新位置;
//   3) 松手:状态立即提交(落盘与撤销照旧同步),卡片再从"指针下的像素位置"动画落进占位框;
//   4) 拉右边 / 下边改大小同理:卡片按像素实时变大小(只重排这一张),占位框显示吸附后的尺寸,松手再动画到吸附尺寸;
//   5) canvas 模式磁性辅助线吸住 / 松开的那一下用短动画代替瞬移。
// 规则全在本文件的纯函数里(有单测);GridCanvasPanel / LayoutEditor 只负责调用和接线。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.App.Common;

namespace TipCL.App.Pages.Layout;

/// <summary>布局动效的纯函数(零界面依赖,单测直接断言)。</summary>
public static class LayoutMotion
{
	/// <summary>其它块让位的 FLIP 时长(ms)。</summary>
	public const double FlipMs = 160.0;

	/// <summary>松手后卡片落进占位框的时长(ms)。</summary>
	public const double DropMs = 180.0;

	/// <summary>canvas 磁性吸附"吸住 / 松开"那一下的时长(ms)。</summary>
	public const double SnapMs = 110.0;

	/// <summary>位移小于它(px)就不值得做动画。</summary>
	public const double Epsilon = 0.5;

	/// <summary>ease-out(三次):起步快、收尾慢 —— 让位与落位的手感。t 越界按 [0,1] 钳。</summary>
	public static double EaseOutCubic(double t)
	{
		t = Clamp01(t);
		double u = 1.0 - t;
		return 1.0 - u * u * u;
	}

	/// <summary>动画进度 = 已过时间 / 时长(钳到 [0,1];时长 ≤ 0 视为瞬间完成)。</summary>
	public static double Progress(double elapsedMs, double durationMs)
	{
		if (durationMs <= 0.0 || double.IsNaN(elapsedMs))
		{
			return 1.0;
		}
		return Clamp01(elapsedMs / durationMs);
	}

	/// <summary>线性插值。</summary>
	public static double Lerp(double from, double to, double t)
	{
		return from + (to - from) * t;
	}

	/// <summary>矩形逐分量插值(落位 / 改大小落位用)。</summary>
	public static LayoutRect LerpRect(LayoutRect from, LayoutRect to, double t)
	{
		return new LayoutRect(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t), Lerp(from.W, to.W, t), Lerp(from.H, to.H, t));
	}

	/// <summary>
	/// FLIP 的反向位移(Invert):块原来**看起来**在 <paramref name="visualX"/>/<paramref name="visualY"/>
	/// (= 上次排布位置 + 当时残留的平移),这次排到了 <paramref name="newX"/>/<paramref name="newY"/> ——
	/// 先用这个位移把它"放回"原处,再在动画里把位移收到 0。位移小于 <see cref="Epsilon"/> 返回 (0,0)(不动画)。
	/// </summary>
	public static Vector FlipOffset(double visualX, double visualY, double newX, double newY)
	{
		double dx = visualX - newX;
		double dy = visualY - newY;
		if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
		{
			return default(Vector);
		}
		return new Vector(dx, dy);
	}

	/// <summary>FLIP 播放到进度 p 时还剩的位移(从 <paramref name="from"/> 按 ease-out 收到 0)。</summary>
	public static Vector FlipRemaining(Vector from, double p)
	{
		double k = 1.0 - EaseOutCubic(p);
		return new Vector(from.X * k, from.Y * k);
	}

	/// <summary>
	/// 网格拖动的落点(与改动前同一套规则,只是抽成纯函数):
	///   列 = 卡片左上角跟随指针后的最近列(钳在 [0, cols − 宽]);
	///   行 = 指针所在的行(按手势开始时的行高快照,带半个间距容差,见 <see cref="LayoutGrid.RowAt"/>)。
	/// </summary>
	public static (int X, int Y) GridDropTarget(double anchorX, double dx, double columnStep, int cols, int width, IReadOnlyList<double>? rowHeights, double pointerY, double gap)
	{
		double step = Math.Max(1.0, columnStep);
		int nx = (int)Math.Round((anchorX + dx) / step);
		nx = Math.Max(0, Math.Min(Math.Max(0, cols - Math.Max(1, width)), nx));
		int ny = LayoutGrid.RowAt(rowHeights, pointerY, gap);
		return (nx, ny);
	}

	/// <summary>
	/// 拉边改大小时卡片的**像素**矩形(实时跟手,不吸附):e 改宽、s 改高、se 两者都改;左上角不动。
	/// 宽钳在 [<paramref name="minW"/>, <paramref name="maxW"/>](maxW ≤ 0 = 不限),高不低于 <paramref name="minH"/>。
	/// </summary>
	public static LayoutRect ResizePixels(LayoutRect start, string? handle, double dx, double dy, double minW, double minH, double maxW)
	{
		string h = handle ?? "";
		double w = start.W;
		double hh = start.H;
		if (h.IndexOf('e') >= 0)
		{
			w = start.W + dx;
		}
		if (h.IndexOf('s') >= 0)
		{
			hh = start.H + dy;
		}
		if (maxW > 0.0)
		{
			w = Math.Min(w, Math.Max(minW, maxW));
		}
		w = Math.Max(Math.Max(1.0, minW), w);
		hh = Math.Max(Math.Max(1.0, minH), hh);
		return new LayoutRect(start.X, start.Y, w, hh);
	}

	/// <summary>
	/// 自动行高页拉下边时吸附后的固定像素高(与改动前同一条规则):按吸附档取整,不低于 <paramref name="minPx"/>;
	/// 吸附档 ≤ 1 视为 1px。
	/// </summary>
	public static double SnapHeightPx(double startH, double dy, double step, double minPx)
	{
		double s = Math.Max(1.0, step);
		double px = Math.Round(Math.Max(minPx, startH + dy) / s) * s;
		return Math.Max(minPx, px);
	}

	/// <summary>改大小时跟在卡片右下角的尺寸提示文本(像素取整),如 "640 × 220"。</summary>
	public static string SizeLabel(double width, double height)
	{
		return Math.Round(Math.Max(0.0, width)).ToString("0") + " × " + Math.Round(Math.Max(0.0, height)).ToString("0");
	}

	/// <summary>canvas 磁性吸附状态是否变了(吸住 / 松开 / 换了吸附位移)—— 变了才用短动画过渡那一下跳变。</summary>
	public static bool SnapChanged(bool hadSnap, double prevDx, double prevDy, bool hasSnap, double dx, double dy)
	{
		if (hadSnap != hasSnap)
		{
			return true;
		}
		return hasSnap && (Math.Abs(prevDx - dx) >= Epsilon || Math.Abs(prevDy - dy) >= Epsilon);
	}

	private static double Clamp01(double t)
	{
		if (double.IsNaN(t) || t <= 0.0)
		{
			return 0.0;
		}
		return (t >= 1.0) ? 1.0 : t;
	}

	// ————— 平移读写(卡片的 RenderTransform 只用来放这一个平移) —————

	/// <summary>平移一个矩形(把 RenderTransform 上残留的位移折进矩形:只改位置,不动尺寸)。</summary>
	public static LayoutRect Offset(LayoutRect rect, double dx, double dy)
	{
		return new LayoutRect(rect.X + dx, rect.Y + dy, rect.W, rect.H);
	}

	/// <summary>读卡片当前的平移(没有平移 = 0)。</summary>
	public static Vector GetOffset(Visual visual)
	{
		return (visual.RenderTransform is TranslateTransform t) ? new Vector(t.X, t.Y) : default(Vector);
	}

	/// <summary>写卡片的平移;(0,0) 时摘掉变换 —— 静止态与改动前逐像素一致。</summary>
	public static void SetOffset(Visual visual, double x, double y)
	{
		if (Math.Abs(x) < 0.01 && Math.Abs(y) < 0.01)
		{
			if (visual.RenderTransform != null)
			{
				visual.RenderTransform = null;
			}
			return;
		}
		if (visual.RenderTransform is TranslateTransform t)
		{
			t.X = x;
			t.Y = y;
			return;
		}
		visual.RenderTransform = new TranslateTransform(x, y);
	}
}

/// <summary>
/// 极简补间驱动:只在有动画时跑的调度器计时器,间隔 16ms(Render 优先级),跟显示器一帧一次。
/// 更密的节拍不会更顺:每一拍都在界面线程上改位移或重排,会把鼠标移动挤到后面。
/// 为什么不用 <see cref="TopLevel.RequestAnimationFrame"/>:实测(无头会话)窗口在动画途中关闭时,
/// 挂着的帧请求会让之后新建窗口的合成 / 命中测试再也出不了帧(拖动手势全部落空);计时器不碰合成器,没有这个风险。
/// 同一个 key 再开动画会先**取消**旧的那个(旧动画不再推进、也不回调 done)—— "新动画开始前取消同一个块上的旧动画"。
/// 界面动画关闭(<see cref="Motion.Enabled"/> = false)或拿不到 TopLevel 时一步到位(step(1) + done)。
/// </summary>
internal static class LayoutTween
{
	private sealed class Entry
	{
		public object Key = null!;
		public TopLevel Top = null!;
		public long Start;
		public double Duration;
		public Action<double> Step = null!;
		public Action? Done;
	}

	// [audit/B] 键比较必须按「值」而不是按「引用」:落位动画的键是 ("land", id) 这种装箱的 ValueTuple,
	// 每次 Start / Cancel 都是**新的装箱实例** —— 用 ReferenceEqualityComparer 时
	// `Cancel(("land", id), …)` 与 `Start(anchor, ("land", id), …)` 互相看不见,
	// 于是"同一块再开一段落位会先取消旧的""FinishAnimations 把落位推到终点"这两条语义都不成立
	// (旧的 180ms 补间继续跑,它的 Done 会把新动画的 _landing 记录删掉 ⇒ 卡片停在半路)。
	// 默认比较器对 Control(没重写 Equals)仍是引用相等,对 ValueTuple 是值相等 —— 两种键都对。
	private static readonly Dictionary<object, Entry> Active = new Dictionary<object, Entry>();

	private static Avalonia.Threading.DispatcherTimer? _timer;

	/// <summary>
	/// AI2-stability:补间用的时钟(Stopwatch 刻度)。测试把它冻住,断言就不再依赖墙钟:无头输入(MouseMove / KeyPress)内部会 RunJobs,
	/// 机器一卡(首个用例 JIT 约 1s)计时器就把动画推到半路甚至终点,「松手 / Esc 瞬间」的位移断言偶发失败。
	/// </summary>
	internal static Func<long> Clock = Stopwatch.GetTimestamp;

	/// <summary>开一段补间:<paramref name="step"/> 收到 [0,1] 的线性进度(缓动由调用方套)。</summary>
	public static void Start(Visual anchor, object key, double durationMs, Action<double> step, Action? done = null)
	{
		Cancel(key, false);
		PurgeStale();
		TopLevel? top = TopLevel.GetTopLevel(anchor);
		if (!Motion.Enabled || durationMs <= 0.0 || top == null)
		{
			step(1.0);
			done?.Invoke();
			return;
		}
		Entry entry = new Entry
		{
			Key = key,
			Top = top,
			Start = Clock(),
			Duration = durationMs,
			Step = step,
			Done = done
		};
		Active[key] = entry;
		step(0.0);
		EnsureTimer();
	}

	/// <summary>取消某个 key 的补间;<paramref name="complete"/> = true 时先把它推到终点(step(1) + done)。</summary>
	public static void Cancel(object key, bool complete)
	{
		if (!Active.TryGetValue(key, out Entry? entry))
		{
			return;
		}
		Active.Remove(key);
		if (complete)
		{
			entry.Step(1.0);
			entry.Done?.Invoke();
		}
	}

	/// <summary>这个 key 是否还在动。</summary>
	public static bool IsRunning(object key)
	{
		return Active.ContainsKey(key);
	}

	// 窗口关掉 / 不再出帧时,它名下的补间永远等不到下一帧:超时很久的一步做完,不留引用
	private static void PurgeStale()
	{
		if (Active.Count == 0)
		{
			return;
		}
		long now = Clock();
		List<object>? stale = null;
		foreach (Entry entry in Active.Values)
		{
			double elapsed = (now - entry.Start) * 1000.0 / Stopwatch.Frequency;
			if (elapsed > entry.Duration + 2000.0)
			{
				(stale ??= new List<object>()).Add(entry.Key);
			}
		}
		if (stale == null)
		{
			return;
		}
		foreach (object key in stale)
		{
			Cancel(key, true);
		}
	}

	/// <summary>在途补间个数(测试 / 调试用)。</summary>
	internal static int ActiveCount
	{
		get
		{
			return Active.Count;
		}
	}

	private static void EnsureTimer()
	{
		if (_timer == null)
		{
			_timer = new Avalonia.Threading.DispatcherTimer(Avalonia.Threading.DispatcherPriority.Render)
			{
				Interval = TimeSpan.FromMilliseconds(16.0)
			};
			_timer.Tick += delegate
			{
				Tick();
			};
		}
		if (!_timer.IsEnabled)
		{
			_timer.Start();
		}
	}

	private static void Tick()
	{
		long now = Clock();
		List<Entry> due = new List<Entry>(Active.Values);
		foreach (Entry entry in due)
		{
			if (!Active.TryGetValue(entry.Key, out Entry? live) || !ReferenceEquals(live, entry))
			{
				continue;
			}
			double elapsed = (now - entry.Start) * 1000.0 / Stopwatch.Frequency;
			double p = LayoutMotion.Progress(elapsed, entry.Duration);
			try
			{
				entry.Step(p);
			}
			catch
			{
				p = 1.0;
			}
			if (p >= 1.0)
			{
				Active.Remove(entry.Key);
				try
				{
					entry.Done?.Invoke();
				}
				catch
				{
				}
			}
		}
		if (Active.Count == 0)
		{
			_timer?.Stop();
		}
	}
}

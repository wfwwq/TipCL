using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TipCL.Services;

/// <summary>
/// 动画门面,公开面与 Windows 版一一对应(<c>Services\Motion.cs</c>,717 行):
/// <c>Scale</c> / <c>Enabled</c> / <c>UserEnabled</c> / <c>EnabledChanged</c> / <c>Loop</c> /
/// <c>FadeIn</c> / <c>FadeOut</c> / <c>Stagger</c> / <c>StaggerItems</c> / <c>HoverLift</c> / <c>Shadow&lt;T&gt;</c> /
/// <c>Pulse</c> / <c>Shake</c> / <c>Shine</c> / <c>Spin</c> / <c>Progress</c> / <c>CountUp</c> /
/// <c>AnimateWidth</c> / <c>AnimateHeight</c> / <c>Fade</c> / <c>PopIn</c> / <c>PopOut</c> / <c>SlideIn</c> /
/// <c>SlideOut</c> / <c>PageSwap</c>。方法名、参数名、顺序与默认值全部照抄。
///
/// WPF 的 <c>BeginAnimation</c>/<c>DoubleAnimation</c>/<c>CompositionTarget.Rendering</c> 换成
/// Avalonia 的 <c>Animation</c> + <c>RunAsync</c>(与 Lucide 的做法一致,见 D-70);WPF 的
/// <c>DropShadowEffect</c> 在 <c>Border</c> 上改用 Avalonia 的 <c>BoxShadow</c>(等价物,见 C2a 差异表)。
///
/// **两条纪律(继承 D-69/D-83,测试与 <c>--smoke</c> 都靠它)**:
/// 1. <see cref="Enabled"/> = 用户开关 **且** 真的跑起来了桌面生命周期。离屏/测试进程里没有人推进
///    动画时钟,无限动画会把收尾挂住(D-69 实测),所以那里一律走"关掉动画"的分支;
/// 2. 关掉动画时**立刻落终态**(不是什么都不做)—— 与 WPF 关掉动画时的样子一致,也不会把控件
///    留在 opacity=0 的中间态。
///
/// **C3e(见 DECISIONS D-132)修掉的一类缺陷:变换动画必须在真桌面上真的跑。**
/// 凡是动画 <b>Transform 自己的属性</b>(<c>TranslateTransform.YProperty</c> 之类)的成员,
/// 以前把 <c>Transform</c> 对象当 <c>Animation.RunAsync</c> 的目标 —— Avalonia 的 <c>TransformAnimator</c>
/// 要求目标是 <c>Visual</c>,于是**同步抛 <c>InvalidCastException</c>**,而当时的 <c>Start</c> 把它吞掉:
/// 离屏(<c>Motion.Enabled</c> 恒假)走不到那一步,所以测试全绿、真桌面上这些动画**静默不动**。
/// 现在一律走 <see cref="PlayTransform"/> / <see cref="PlayTransformFrames"/>:**目标给宿主控件**、
/// 属性仍写变换的属性,由动画器去目标的 <c>RenderTransform</c> 里解析那一个变换实例(与 D-130 记的写法一致);
/// 顺带把"失败"变成可观测(<see cref="LastError"/> + <see cref="StartedAnimations"/> /
/// <see cref="RunningAnimations"/>),以后同类回归会被测试抓住,而不是再一次悄悄少动一下。
/// </summary>
public static class Motion
{
	private static bool _enabled = true;

	private static int _swapGen;

	/// <summary>
	/// 最近一次动画失败(C3e 新增,见 DECISIONS D-132)。
	/// **为什么要有它**:旧实现把 <see cref="Start"/> 里的一切异常 <c>catch</c> 掉就完事,于是
	/// "动画根本没跑"和"动画跑完了"在外部看起来一模一样 —— 这正是"以 Transform 为目标的动画在真桌面静默不动"
	/// 能藏到 C3d 才被发现的原因。现在失败会落在这里(+<c>Debug.WriteLine</c>),测试可以断言"这一批动画一条都没报错"。
	/// </summary>
	private static Exception? _lastError;

	private static string? _lastTargetType;

	private static string? _lastPropertyName;

	private static int _startedAnimations;

	private static int _runningAnimations;

	/// <summary>测试观察用的活动动画编号集合(只有 <see cref="ResetObservability"/> 之后起的才进来)。</summary>
	private static readonly HashSet<long> Active = new HashSet<long>();

	private static readonly object ActiveGate = new object();

	private static long _nextAnimationId;

	private static bool _trackActive;

	/// <summary>最近一次失败(没有失败就是 null)。</summary>
	internal static Exception? LastError => _lastError;

	/// <summary>最近一次真的交给 Avalonia 的动画**目标**的运行时类型全名(测试用它钉住"目标必须是控件,不是 Transform")。</summary>
	internal static string? LastTargetType => _lastTargetType;

	/// <summary>最近一次动画的属性名的形状(<c>TranslateTransform.Y</c> / <c>Visual.Opacity</c>)。</summary>
	internal static string? LastPropertyName => _lastPropertyName;

	/// <summary>本进程里"真的交给 Avalonia 起过几条动画"(单调递增)。</summary>
	internal static int StartedAnimations => Volatile.Read(ref _startedAnimations);

	/// <summary>
	/// 现在挂着几条还没结束的动画(跑完 / 取消 / 失败都会减回去)。
	/// 注意这**是进程全局**的:别的用例(比如 C3d 的飞行 chip)在窗口关掉之后可能还留着几条永远收不了尾的动画,
	/// 所以"本用例有没有留残留"要用 <see cref="RunningAnimationsSinceReset"/> 看,而不是看这个总数。
	/// </summary>
	internal static int RunningAnimations => Volatile.Read(ref _runningAnimations);

	/// <summary>
	/// 自上次 <see cref="ResetObservability"/> 以来还挂着的动画条数 —— 按**每一条动画的编号**记账,
	/// 只算这段窗口里起的那些。泄露检查用它:本用例起的都收摊了就该是 0。
	///
	/// 为什么不能拿 <see cref="RunningAnimations"/> 的差值代替:<c>C3dReachTests</c> 关掉窗口之后
	/// 还留着 4 条飞行 chip 的动画,它们**没有消失**,只是没人推帧;本用例一开始泵帧,它们就会跑完并减数,
	/// 于是"差值"会变成负数(本机进程内执行器实测:-2)。按编号记账没有这个问题。
	/// </summary>
	internal static int RunningAnimationsSinceReset
	{
		get
		{
			lock (ActiveGate)
			{
				return Active.Count;
			}
		}
	}

	/// <summary>
	/// 测试专用:清掉 <see cref="LastError"/> 与记录到的目标 / 属性 / 起过几条,并从这一刻开始
	/// 按编号记"还挂着几条"(<see cref="RunningAnimationsSinceReset"/>)。
	/// </summary>
	internal static void ResetObservability()
	{
		_lastError = null;
		_lastTargetType = null;
		_lastPropertyName = null;
		Volatile.Write(ref _startedAnimations, 0);
		lock (ActiveGate)
		{
			_trackActive = true;
			Active.Clear();
		}
	}

	private static readonly CubicEaseOut EaseOut = new CubicEaseOut();

	private static readonly CubicEaseIn EaseIn = new CubicEaseIn();

	private static readonly SineEaseInOut EaseInOut = new SineEaseInOut();

	/// <summary>时长倍率(Windows 版同名同义,默认 1.0)。</summary>
	public static double Scale { get; set; } = 1.0;

	/// <summary>
	/// 关掉动画时所有成员都立刻落终态。<c>set</c> 的语义与 Windows 版一致:值变了才发
	/// <see cref="EnabledChanged"/>;**<c>get</c> 额外带上"有没有人在推进时钟"这一条**(见类注释)。
	/// </summary>
	public static bool Enabled
	{
		get
		{
			return _enabled && HasClock;
		}
		set
		{
			if (_enabled != value)
			{
				_enabled = value;
				Motion.EnabledChanged?.Invoke();
			}
		}
	}

	/// <summary>用户开关本身(不看时钟):Windows 版同名成员。</summary>
	public static bool UserEnabled => _enabled;

	public static event Action? EnabledChanged;

	/// <summary>
	/// 测试专用:强制"有人在推进动画时钟"(null = 按真桌面生命周期判断,这是默认值)。
	/// 为什么需要它:headless 会话里 <c>Application.ApplicationLifetime</c> 永远是 null(D-69/D-98),
	/// <see cref="Enabled"/> 因此在测试里恒为 false,"动画开着"那一支**完全不可达** —— 每个需要验
	/// 动画的包都只能验"关掉时落终态"那一半。
	/// 而 headless 的动画时钟确实推得动(C3d 本机实测:620ms 的 <c>Animation.RunAsync</c> 靠
	/// <c>AvaloniaHeadlessPlatform.ForceRenderTimerTick()</c> 真的跑完并落了终态;
	/// 同一次探针还量到 <c>DispatcherTimer</c> 在那条泵里**一次都不 Tick**),
	/// 所以这里给测试一个"假装有时钟"的注入点。
	/// **纪律:只在作用域内设它、必须在 Dispose 里还原**(见 DECISIONS D-129)。
	/// </summary>
	internal static bool? ClockOverride { get; set; }

	/// <summary>有没有人推进动画时钟:只有真桌面生命周期才算(测试/离屏环境里是 null;测试可以经
	/// <see cref="ClockOverride"/> 强行打开,见那里的说明)。</summary>
	internal static bool HasClock => ClockOverride ?? (Application.Current?.ApplicationLifetime is not null);

	private static TimeSpan D(double ms)
	{
		return TimeSpan.FromMilliseconds(Math.Max(1.0, ms * Scale));
	}

	/// <summary>
	/// 在 <paramref name="owner"/> 的真生命周期里跑一对起停回调:可见且在树上且动画开着时
	/// 调 <paramref name="start"/>,否则调 <paramref name="stop"/>(与 Windows 版同一套状态机)。
	/// </summary>
	public static void Loop(Control owner, Action start, Action stop)
	{
		if (owner is null)
		{
			return;
		}

		bool running = false;
		bool loaded = false;
		Action onToggle = delegate
		{
			Dispatcher.UIThread.Post(Sync);
		};

		owner.Loaded += delegate
		{
			loaded = true;
			Motion.EnabledChanged += onToggle;
			Sync();
		};
		owner.Unloaded += delegate
		{
			loaded = false;
			Motion.EnabledChanged -= onToggle;
			Sync();
		};
		owner.PropertyChanged += delegate(object? _, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == Visual.IsVisibleProperty)
			{
				Sync();
			}
		};

		if (owner.IsAttachedToVisualTree())
		{
			loaded = true;
			Motion.EnabledChanged += onToggle;
		}

		Sync();
		void Sync()
		{
			// 与 Windows 的 owner.IsLoaded 等价:Avalonia 用「在不在可视化树上」判断
			// (已经挂上窗口的元素也要能立刻起动画,不能只等 Loaded 事件)。
			bool flag = Enabled && owner.IsVisible && (loaded || owner.IsAttachedToVisualTree());
			if (flag != running)
			{
				running = flag;
				if (flag)
				{
					start();
				}
				else
				{
					stop();
				}
			}
		}
	}

	private static TranslateTransform EnsureTranslate(Control el)
	{
		if (el.RenderTransform is TransformGroup group)
		{
			foreach (ITransform child in group.Children)
			{
				if (child is TranslateTransform found)
				{
					return found;
				}
			}
			TranslateTransform added = new TranslateTransform();
			group.Children.Add(added);
			return added;
		}
		if (el.RenderTransform is TranslateTransform existing)
		{
			return existing;
		}
		TransformGroup group2 = new TransformGroup();
		Transform? current = el.RenderTransform as Transform;
		if (current is not null && current is not MatrixTransform)
		{
			group2.Children.Add(current);
		}
		TranslateTransform translate = new TranslateTransform();
		group2.Children.Add(translate);
		el.RenderTransform = group2;
		return translate;
	}

	private static ScaleTransform EnsureScale(Control el)
	{
		el.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
		if (el.RenderTransform is TransformGroup group)
		{
			foreach (ITransform child in group.Children)
			{
				if (child is ScaleTransform found)
				{
					return found;
				}
			}
			ScaleTransform added = new ScaleTransform();
			group.Children.Add(added);
			return added;
		}
		if (el.RenderTransform is ScaleTransform existing)
		{
			return existing;
		}
		TransformGroup group2 = new TransformGroup();
		Transform? current = el.RenderTransform as Transform;
		if (current is not null && current is not MatrixTransform)
		{
			group2.Children.Add(current);
		}
		ScaleTransform scale = new ScaleTransform();
		group2.Children.Add(scale);
		el.RenderTransform = group2;
		return scale;
	}

	/// <summary>
	/// 两帧动画(from -> to)的构造。只有两帧,所以 <c>Animation.Easing</c> 作用在整段进度上 ——
	/// 与 WPF 的 <c>DoubleAnimation(from, to, duration){ EasingFunction = ... }</c> 单段缓动逐点相同。
	///
	/// **<c>easing</c> 为 null = 线性**(WPF 不给 <c>EasingFunction</c> 时就是线性),实现方式是
	/// **完全不碰 <c>Animation.Easing</c>**(它的默认值就是 <c>LinearEasing</c>)。
	/// 为什么不直接写 <c>Easing = null</c>:本机探针实测(见 PROGRESS C3e 的探针表)那样会让动画
	/// **一个字节都不写、任务永不结束** —— 又一条"静默不动"的暗沟,所以这里显式躲开它。
	/// </summary>
	private static Animation TwoFrame(AvaloniaProperty<double> property, double from, double to, TimeSpan duration, Easing? easing)
	{
		Animation animation = new Animation
		{
			Duration = duration,
			FillMode = FillMode.Forward,
			Children =
			{
				new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(property, from) } },
				new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(property, to) } },
			},
		};
		ApplyEasing(animation, easing);
		return animation;
	}

	/// <summary>多关键帧动画的构造(与 WPF 的 <c>DoubleAnimationUsingKeyFrames</c> 等价:关键帧均匀铺开)。</summary>
	private static Animation KeyFrames(AvaloniaProperty<double> property, double[] values, TimeSpan duration, Easing? easing)
	{
		Animation animation = new Animation
		{
			Duration = duration,
			FillMode = FillMode.Forward,
		};
		for (int i = 0; i < values.Length; i++)
		{
			animation.Children.Add(new KeyFrame
			{
				Cue = new Cue((double)i / (double)(values.Length - 1)),
				Setters = { new Setter(property, values[i]) },
			});
		}

		ApplyEasing(animation, easing);
		return animation;
	}

	/// <summary>
	/// 只在给了缓动时才写 <c>Animation.Easing</c>。
	/// **不许写 null**:<c>Animation.Easing</c> 的默认值是 <c>LinearEasing</c>,显式写成 null 之后
	/// Avalonia 的动画实例会**静默失效**(本机探针:属性一个字节都不写、<c>RunAsync</c> 的任务永不结束,
	/// 也不抛异常)—— 那正是本包要根除的那类坏账。null 的语义 = 线性 = 不碰这个属性。
	/// </summary>
	private static void ApplyEasing(Animation animation, Easing? easing)
	{
		if (easing is not null)
		{
			animation.Easing = easing;
		}
	}

	/// <summary>
	/// 控件**自己**的属性动画(不透明度 / 宽高 / 进度值):目标就是那个控件。
	/// 关掉动画时什么都不做 —— 与旧实现逐字一致(调用方各自在 <c>!Enabled</c> 分支里落了终态)。
	/// </summary>
	private static void Play(Visual target, AvaloniaProperty<double> property, double from, double to, double ms, Easing? easing, Action? done = null)
	{
		if (target is null || !Enabled)
		{
			done?.Invoke();
			return;
		}

		Start(TwoFrame(property, from, to, D(ms), easing), target, CancellationToken.None, delegate
		{
			done?.Invoke();
		});
	}

	/// <summary>控件自己的属性的多关键帧动画(KeyFrames 版:关掉动画时落最后一个关键帧的值)。</summary>
	private static void PlayFrames(Visual target, AvaloniaProperty<double> property, double[] values, double ms, Easing? easing, Action? done = null)
	{
		if (target is null || values.Length == 0)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			// 关掉动画:直接落最后一个关键帧的值(与 WPF 关掉动画时的观感一致)。
			target.SetValue(property, values[values.Length - 1]);
			done?.Invoke();
			return;
		}

		Start(KeyFrames(property, values, D(ms), easing), target, CancellationToken.None, delegate
		{
			done?.Invoke();
		});
	}

	/// <summary>
	/// **变换属性动画 —— 本包(C3e)的核心修法,见 DECISIONS D-132。**
	///
	/// 目标给**宿主控件**(<paramref name="owner"/>),属性仍然是那个变换自己的属性
	/// (<c>TranslateTransform.YProperty</c> / <c>ScaleTransform.ScaleXProperty</c> / <c>RotateTransform.AngleProperty</c>)。
	///
	/// 为什么不能把 <c>Transform</c> 对象本身当目标:Avalonia 的 <c>TransformAnimator.Apply</c> 第一句就是
	/// <c>(Visual)control</c>,目标不是 <c>Visual</c> 时**同步抛 <c>InvalidCastException</c>**
	/// (本机探针:Translate / Scale / Rotate 三种都抛;C2a 移植时留下的既存缺陷,旧代码把它 <c>catch</c> 掉了 ⇒
	/// 真桌面上这些动画静默不动)。换目标为控件之后,动画器会到控件的 <c>RenderTransform</c> 里找
	/// <c>property.OwnerType</c> 那一个实例(是 <c>TransformGroup</c> 就逐个子项按类型比)并写它 ——
	/// 所以调用方**必须**已经把 <paramref name="transform"/> 挂进 <paramref name="owner"/> 的 RenderTransform 里
	/// (本文件的 <c>EnsureTranslate</c> / <c>EnsureScale</c>、<c>Shine</c> 的 <c>layer.RenderTransform = tx</c>、
	/// 以及 <c>Spin</c> 的调用点都保证了这一点)。这个不变量由
	/// <see cref="CheckTransformTarget"/> 自检:不满足就记进 <see cref="LastError"/>,不让它再"悄悄不动"。
	/// </summary>
	private static void PlayTransform(Visual owner, Transform transform, AvaloniaProperty<double> property, double from, double to, double ms, Easing? easing, Action? done = null)
	{
		if (owner is null || transform is null || !Enabled)
		{
			done?.Invoke();
			return;
		}

		CheckTransformTarget(owner, transform, property);
		Start(TwoFrame(property, from, to, D(ms), easing), owner, CancellationToken.None, delegate
		{
			done?.Invoke();
		});
	}

	/// <summary>
	/// 变换属性的多关键帧动画(Pulse / Shake 用)。
	/// 关掉动画时什么都不做:**旧实现那一支是写回 Transform 对象**(那里 <c>target</c> 就是 Transform),
	/// 现在目标换成控件之后那一支已经不可达 —— Pulse / Shake 都已经在自己的 <c>!Enabled</c> 分支里落了终态
	/// (1.0 / 0.0),与旧行为逐值相同。
	/// </summary>
	private static void PlayTransformFrames(Visual owner, Transform transform, AvaloniaProperty<double> property, double[] values, double ms, Easing? easing, Action? done = null)
	{
		if (owner is null || transform is null || values.Length == 0)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			done?.Invoke();
			return;
		}

		CheckTransformTarget(owner, transform, property);
		Start(KeyFrames(property, values, D(ms), easing), owner, CancellationToken.None, delegate
		{
			done?.Invoke();
		});
	}

	/// <summary>
	/// Avalonia 的 <c>TransformAnimator</c> 找变换的规则(本机读 11.3.2 的 IL 得到,见 D-132):
	/// <c>RenderTransform</c> 恰好就是 <paramref name="transformType"/> 那个类型,或者是 <c>TransformGroup</c> 时
	/// 里面**第一个运行时类型完全相等**的子项;两者都不是就什么都不做(只发一条 Warning 日志)。
	/// </summary>
	private static Transform? FindTransform(ITransform? renderTransform, Type transformType)
	{
		if (renderTransform is TransformGroup group)
		{
			foreach (ITransform child in group.Children)
			{
				if (child.GetType() == transformType)
				{
					return child as Transform;
				}
			}
			return null;
		}
		if (renderTransform is not null && renderTransform.GetType() == transformType)
		{
			return renderTransform as Transform;
		}
		return null;
	}

	/// <summary>
	/// 变换动画的起手自检:动画器找不到调用方给的那个实例时会**静默什么都不做**(任务永不结束、
	/// 属性一个字节都不写),那又是"动画悄悄不跑"那一类坏账。这里把它变成 <see cref="LastError"/> 上的一条记录
	/// (行为一个字节都不改:动画照样按原样发出去)。
	/// </summary>
	private static void CheckTransformTarget(Visual owner, Transform transform, AvaloniaProperty<double> property)
	{
		Transform? found = FindTransform(owner.RenderTransform, property.OwnerType);
		if (ReferenceEquals(found, transform))
		{
			return;
		}

		string reason = found is null
			? "控件的 RenderTransform 里没有 " + property.OwnerType.Name + " 实例(动画器只按类型在 RenderTransform 里找,找不到就静默不动)"
			: "控件的 RenderTransform 里的 " + property.OwnerType.Name + " 不是调用方给的那个实例";
		NoteError(new InvalidOperationException(
			"变换动画不会跑:" + reason + " [属性=" + property.OwnerType.Name + "." + property.Name + ", 控件=" + owner.GetType().Name + "]"));
	}

	/// <summary>
	/// 起一条动画 —— **整个 Motion 只有这一处碰 Avalonia 的动画系统**。
	///
	/// 四条纪律(C3e 收紧,见 DECISIONS D-132):
	/// 1. 目标参数的类型是 <see cref="Visual"/>:**编译期**就把"拿 Transform 对象当目标"这条错路堵死;
	/// 2. 失败**可观测**:同步异常与"任务随后失败"两条路都进 <see cref="LastError"/> + <c>Debug.WriteLine</c>
	///    (旧代码只 catch 得到同步异常,任务失败会被静默丢掉),并且绝不让界面崩;
	/// 3. 起完动画顺手推一次重绘(<c>InvalidateVisual</c>):Avalonia 的动画时钟在离屏 / headless 里**只在真的产出帧时**
	///    前进(本机探针:同一条变换动画,每轮推一次重绘 251ms 跑完;一次都不推就永远停在起点),
	///    真桌面由 vsync 驱动不需要它,但多推一次无害 —— 它能把"没帧 ⇒ 不动"这条路也堵上;
	/// 4. <paramref name="finished"/> 只报告**真的跑完**(<c>true</c>)或**失败**(<c>false</c>);
	///    被取消的动画没有回调(与 Windows 的 <c>BeginAnimation(prop, null)</c> 同义,收尾由停它的那一方负责)。
	/// </summary>
	private static void Start(Animation animation, Visual target, CancellationToken token, Action<bool>? finished)
	{
		if (target is null)
		{
			finished?.Invoke(false);
			return;
		}

		Task task;
		try
		{
			task = animation.RunAsync(target, token);
			target.InvalidateVisual();
		}
		catch (Exception ex)
		{
			// 动画是装饰性的:失败不许把界面带崩,但**必须留下痕迹**。
			NoteError(ex);
			finished?.Invoke(false);
			return;
		}

		_lastTargetType = target.GetType().FullName;
		_lastPropertyName = DescribeProperty(animation);
		Interlocked.Increment(ref _startedAnimations);
		Interlocked.Increment(ref _runningAnimations);
		long id = 0L;
		bool tracked = _trackActive;
		if (tracked)
		{
			lock (ActiveGate)
			{
				id = ++_nextAnimationId;
				Active.Add(id);
			}
		}

		task.ContinueWith(
			delegate(Task completed)
			{
				Interlocked.Decrement(ref _runningAnimations);
				if (tracked)
				{
					lock (ActiveGate)
					{
						Active.Remove(id);
					}
				}

				if (token.IsCancellationRequested)
				{
					// 被取消:没有收尾回调(旧实现里这一支是"任务照走",与 Windows 的语义不符)。
					return;
				}
				if (completed.IsFaulted)
				{
					NoteError(completed.Exception!.GetBaseException());
					Dispatcher.UIThread.Post(delegate
					{
						finished?.Invoke(false);
					}, DispatcherPriority.Background);
					return;
				}
				if (finished is not null)
				{
					Dispatcher.UIThread.Post(delegate
					{
						finished(true);
					});
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.None,
			TaskScheduler.Default);
	}

	/// <summary>动画第一个 setter 的属性名(可观测性用,只为把"哪一条动画"写清楚)。</summary>
	private static string DescribeProperty(Animation animation)
	{
		foreach (KeyFrame frame in animation.Children)
		{
			foreach (Setter setter in frame.Setters)
			{
				if (setter.Property is not null)
				{
					return setter.Property.OwnerType.Name + "." + setter.Property.Name;
				}
			}
		}
		return "(无属性)";
	}

	/// <summary>记下一条动画失败(界面不受影响,但从此可以被断言抓住)。</summary>
	private static void NoteError(Exception ex)
	{
		_lastError = ex;
		Debug.WriteLine("[Motion] 动画失败(已记录,界面不受影响): " + ex);
	}

	/// <summary>
	/// 测试专用:把一条动画直接交给内部 runner,用来证明"失败会被记进 <see cref="LastError"/> 而不是被吞掉"。
	/// 生产代码不走这里。
	/// </summary>
	internal static void RunForTest(Animation animation, Visual target, Action? done = null)
	{
		Start(animation, target, CancellationToken.None, delegate
		{
			done?.Invoke();
		});
	}

	public static void FadeIn(Control el, double ms = 220.0, double fromY = 10.0, double delayMs = 0.0)
	{
		if (el is null)
		{
			return;
		}
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		if (delayMs > 0.0)
		{
			// WPF 的 BeginTime:mac 侧等价物是推迟起动画(不改公开签名,行为一致)。
			DispatcherTimer delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, delayMs * Scale)) };
			delay.Tick += delegate
			{
				delay.Stop();
				FadeIn(el, ms, fromY, 0.0);
			};
			delay.Start();
			return;
		}
		TranslateTransform translate = EnsureTranslate(el);
		el.Opacity = 0.0;
		translate.Y = fromY;
		Play(el, Visual.OpacityProperty, 0.0, 1.0, ms, EaseOut);
		// 【C3e】竖直位移:目标给 el(旧写法把 translate 当目标 ⇒ 真桌面静默不动,见 D-132)。
		PlayTransform(el, translate, TranslateTransform.YProperty, fromY, 0.0, ms + 60.0, EaseOut);
	}

	public static void FadeOut(Control el, double ms = 140.0, Action? then = null)
	{
		if (el is null)
		{
			then?.Invoke();
			return;
		}
		if (!Enabled)
		{
			el.Opacity = 0.0;
			then?.Invoke();
			return;
		}
		Play(el, Visual.OpacityProperty, el.Opacity, 0.0, ms, EaseIn, then);
	}

	public static void Stagger(Panel host, double step = 26.0, double ms = 210.0, double fromY = 12.0)
	{
		if (host is null || !Enabled)
		{
			return;
		}
		int num = 0;
		foreach (Control child in host.Children)
		{
			if (num >= 16)
			{
				child.Opacity = 1.0;
			}
			else
			{
				FadeIn(child, ms, fromY, Math.Min((double)num * step, 320.0));
			}
			num++;
		}
	}

	public static void StaggerItems(IEnumerable<Control> items, double step = 24.0, double ms = 200.0, double fromY = 10.0)
	{
		if (items is null || !Enabled)
		{
			return;
		}
		int num = 0;
		foreach (Control item in items)
		{
			if (num >= 16)
			{
				item.Opacity = 1.0;
			}
			else
			{
				FadeIn(item, ms, fromY, Math.Min((double)num * step, 300.0));
			}
			num++;
		}
	}

	public static void HoverLift(Control el, double scale = 1.012, double lift = 2.0, double blur = 22.0)
	{
		if (el is null)
		{
			return;
		}

		bool wantScale = scale >= 1.01;
		ScaleTransform? sc = wantScale ? EnsureScale(el) : null;
		TranslateTransform tt = EnsureTranslate(el);
		Border? border = el as Border;
		BoxShadows? shadow = null;
		bool hovering = false;

		el.PointerEntered += delegate
		{
			if (!Enabled)
			{
				return;
			}
			hovering = true;
			if (border is not null && shadow is null)
			{
				shadow = new BoxShadows(new BoxShadow
				{
					OffsetX = 0.0,
					OffsetY = 3.0,
					Blur = blur,
					Color = Color.FromArgb(41, 24, 24, 24),
				});
			}
			if (border is not null && shadow.HasValue && border.BoxShadow.Equals(default(BoxShadows)))
			{
				border.BoxShadow = shadow.Value;
			}
			if (sc != null)
			{
				PlayTransform(el, sc, ScaleTransform.ScaleXProperty, sc.ScaleX, scale, 170.0, EaseOut);
				PlayTransform(el, sc, ScaleTransform.ScaleYProperty, sc.ScaleY, scale, 170.0, EaseOut);
			}
			PlayTransform(el, tt, TranslateTransform.YProperty, tt.Y, 0.0 - lift, 170.0, EaseOut);
		};
		el.PointerExited += delegate
		{
			hovering = false;
			if (sc != null)
			{
				PlayTransform(el, sc, ScaleTransform.ScaleXProperty, sc.ScaleX, 1.0, 190.0, EaseOut);
				PlayTransform(el, sc, ScaleTransform.ScaleYProperty, sc.ScaleY, 1.0, 190.0, EaseOut);
			}
			PlayTransform(el, tt, TranslateTransform.YProperty, tt.Y, 0.0, 190.0, EaseOut);
			if (border is null || shadow is null)
			{
				return;
			}
			Border target = border;
			BoxShadows owned = shadow.Value;
			DispatcherTimer timer = new DispatcherTimer { Interval = D(190.0) };
			timer.Tick += delegate
			{
				timer.Stop();
				if (!hovering && target.BoxShadow.Equals(owned))
				{
					target.BoxShadow = default(BoxShadows);
				}
			};
			timer.Start();
		};
	}

	/// <summary>
	/// 阴影。WPF 版给任意 <c>UIElement</c> 挂 <c>DropShadowEffect</c>;Avalonia 里 <c>Border</c> 有
	/// 更贴合的 <c>BoxShadow</c>(Controls.axaml 的模板也用它),所以 Border 走 BoxShadow、其余控件走
	/// <c>Visual.Effect</c> + <c>DropShadowEffect</c>。参数含义与 Windows 版一致(方向同为竖直向下)。
	/// </summary>
	public static T Shadow<T>(T el, double blur = 18.0, double opacity = 0.08, double depth = 2.0) where T : Control
	{
		if (el is null)
		{
			return el;
		}

		if (el is Border border)
		{
			border.BoxShadow = new BoxShadows(new BoxShadow
			{
				OffsetX = 0.0,
				OffsetY = depth,
				Blur = blur,
				Color = Color.FromArgb((byte)Math.Clamp((int)Math.Round(opacity * 255.0), 0, 255), 20, 20, 20),
			});
			return el;
		}

		el.Effect = new DropShadowEffect
		{
			OffsetX = 0.0,
			OffsetY = depth,
			BlurRadius = blur,
			Opacity = opacity,
			Color = Color.FromRgb(20, 20, 20),
		};
		return el;
	}

	public static void Pulse(Control el, double peak = 1.06)
	{
		if (el is null)
		{
			return;
		}
		ScaleTransform target = EnsureScale(el);
		if (!Enabled)
		{
			target.ScaleX = 1.0;
			target.ScaleY = 1.0;
			return;
		}
		// 【C3e】目标给控件(el),属性仍是缩放变换自己的 —— 旧写法把 target(ScaleTransform) 当目标是错的(D-132)。
		PlayTransformFrames(el, target, ScaleTransform.ScaleXProperty, new double[3] { 1.0, peak, 1.0 }, 280.0, EaseOut);
		PlayTransformFrames(el, target, ScaleTransform.ScaleYProperty, new double[3] { 1.0, peak, 1.0 }, 280.0, EaseOut);
	}

	public static void Shake(Control el)
	{
		if (el is null)
		{
			return;
		}
		TranslateTransform target = EnsureTranslate(el);
		if (!Enabled)
		{
			target.X = 0.0;
			return;
		}
		// 【C3e】同上:目标给控件。六个关键帧逐值照抄 Windows(:408-417)。
		PlayTransformFrames(el, target, TranslateTransform.XProperty, new double[6] { -7.0, 6.0, -4.0, 3.0, -1.5, 0.0 }, 330.0, EaseOut);
	}

	public static void Shine(Control layer, TranslateTransform tx, double width = 1200.0)
	{
		if (layer is null || tx is null)
		{
			return;
		}

		layer.RenderTransform = tx;
		layer.Opacity = 0.0;
		DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7.0) };
		timer.Tick += delegate
		{
			Sweep();
		};
		Loop(layer, delegate
		{
			timer.Start();
			Dispatcher.UIThread.Post(Sweep, DispatcherPriority.Background);
		}, delegate
		{
			timer.Stop();
			layer.Opacity = 0.0;
		});
		void Sweep()
		{
			Window? window = TopLevel.GetTopLevel(layer) as Window;
			if (window is null || window.WindowState == WindowState.Minimized || !window.IsActive || Dlg.BlurTarget?.Effect != null)
			{
				return;
			}
			double toValue = ((layer.Parent is Control { Bounds.Width: > 0.0 } parent) ? (parent.Bounds.Width + 40.0) : width);
			// 【C3e】横扫位移:目标给 layer(它的 RenderTransform 就是 tx,上一行刚设过)。
			PlayTransform(layer, tx, TranslateTransform.XProperty, 0.0 - layer.Bounds.Width - 40.0, toValue, 1600.0, EaseInOut);
			PlayFrames(layer, Visual.OpacityProperty, new double[4] { 0.0, 0.5, 0.5, 0.0 }, 1600.0, null);
		}
	}

	/// <summary>
	/// 一直转(Windows:无限 <c>DoubleAnimation(0, 360)</c> + <c>RepeatBehavior.Forever</c>)。
	///
	/// **两个 C3e 修掉的坑(见 DECISIONS D-132)**:
	/// 1. 旧实现把 <paramref name="rot"/> 当 <c>RunAsync</c> 的目标 ⇒ 同步抛 <c>InvalidCastException</c>
	///    (被吞掉)⇒ 真桌面上**根本不转**;现在目标给 <paramref name="owner"/>。
	/// 2. 旧的 <c>IterationCount = Infinite</c> 在 Avalonia 11 上**不可能跑起来**:
	///    <c>Animation.RunAsync</c> 会抛 <c>InvalidOperationException: Looping animations must not use the Run method.</c>
	///    (本机探针实测,同样被旧代码吞掉)。Avalonia 里唯一能循环的公开入口是 <c>Animation.Apply</c>,而它在 11.3.2
	///    里不是公开 API。所以这里改成"**一圈一圈接起来**:一条有限动画跑完在收尾回调里再起下一条" ——
	///    角度每圈回到 0 起转,而 0° 与 360° 在屏幕上同一处,观感是连续的(每圈交界处有一次收尾往返的
	///    一帧级重启,见 PROGRESS 的用户可见差异)。
	/// </summary>
	public static void Spin(Control owner, RotateTransform rot, double seconds = 1.0)
	{
		if (owner is null || rot is null)
		{
			return;
		}

		bool spinning = false;
		CancellationTokenSource? cts = null;
		Loop(owner, delegate
		{
			spinning = true;
			cts = new CancellationTokenSource();
			CheckTransformTarget(owner, rot, RotateTransform.AngleProperty);
			Turn(cts);
		}, delegate
		{
			// 【Windows 源】:477 rot.BeginAnimation(RotateTransform.AngleProperty, null) —— 停掉那条循环动画。
			// Avalonia 侧等价物 = 取消(本机探针:取消之后动画立刻不再写值),然后归零。
			spinning = false;
			CancellationTokenSource? own = cts;
			cts = null;
			own?.Cancel();
			rot.Angle = 0.0;
		});

		void Turn(CancellationTokenSource own)
		{
			// 这一圈还该不该转:被停掉了(cts 已经不是自己)就直接结束,绝不自己打转。
			if (!spinning || !ReferenceEquals(cts, own))
			{
				return;
			}

			Animation animation = new Animation
			{
				Duration = TimeSpan.FromSeconds(seconds),
				FillMode = FillMode.Forward,
				Children =
				{
					new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
					new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } },
				},
			};
			Start(animation, owner, own.Token, delegate(bool ok)
			{
				// 只有**真的转完一圈**才接下一圈;失败已经记进 LastError,不许在这里空转。
				if (ok)
				{
					Turn(own);
				}
			});
		}
	}

	public static void Progress(ProgressBar bar, double value)
	{
		if (bar is null)
		{
			return;
		}
		value = Clamp.Of(value, 0.0, 100.0);
		if (!Enabled)
		{
			bar.Value = value;
			return;
		}
		double ms = (Math.Abs(value - bar.Value) > 40.0) ? 320.0 : 200.0;
		Play(bar, RangeBase.ValueProperty, bar.Value, value, ms, EaseOut);
	}

	public static void CountUp(TextBlock tb, double from, double to, string fmt = "0", double ms = 620.0, Action? done = null)
	{
		if (tb is null)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			tb.Text = to.ToString(fmt);
			done?.Invoke();
			return;
		}
		Stopwatch sw = Stopwatch.StartNew();
		double total = Math.Max(1.0, ms * Scale);
		DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16.0) };
		timer.Tick += delegate
		{
			double num = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / total);
			double eased = 1.0 - Math.Pow(1.0 - num, 3.0);
			tb.Text = (from + (to - from) * eased).ToString(fmt);
			if (num >= 1.0)
			{
				timer.Stop();
				done?.Invoke();
			}
		};
		timer.Start();
	}

	public static void AnimateWidth(Control el, double to, double ms = 220.0, Action? done = null)
	{
		if (el is null)
		{
			done?.Invoke();
			return;
		}
		double from = double.IsNaN(el.Width) ? el.Bounds.Width : el.Width;
		if (!Enabled)
		{
			el.Width = to;
			done?.Invoke();
			return;
		}
		Play(el, Layoutable.WidthProperty, from, to, ms, EaseOut, delegate
		{
			el.Width = to;
			done?.Invoke();
		});
	}

	public static void AnimateHeight(Control el, double to, double ms = 220.0, Action? done = null)
	{
		if (el is null)
		{
			done?.Invoke();
			return;
		}
		double from = double.IsNaN(el.Height) ? el.Bounds.Height : el.Height;
		if (!Enabled)
		{
			el.Height = to;
			done?.Invoke();
			return;
		}
		Play(el, Layoutable.HeightProperty, from, to, ms, EaseOut, delegate
		{
			el.Height = to;
			done?.Invoke();
		});
	}

	public static void Fade(Control el, double to, double ms = 160.0, Action? done = null)
	{
		if (el is null)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			el.Opacity = to;
			done?.Invoke();
			return;
		}
		Play(el, Visual.OpacityProperty, el.Opacity, to, ms, EaseOut, done);
	}

	public static void PopIn(Control el, double ms = 200.0)
	{
		if (el is null)
		{
			return;
		}
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		ScaleTransform scale = EnsureScale(el);
		el.Opacity = 0.0;
		scale.ScaleY = 0.96;
		scale.ScaleX = 0.96;
		Play(el, Visual.OpacityProperty, 0.0, 1.0, ms, EaseOut);
		PlayTransform(el, scale, ScaleTransform.ScaleXProperty, 0.96, 1.0, ms + 60.0, EaseOut);
		PlayTransform(el, scale, ScaleTransform.ScaleYProperty, 0.96, 1.0, ms + 60.0, EaseOut);
	}

	public static void PopOut(Control el, Action done, double ms = 130.0)
	{
		if (el is null)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			done();
			return;
		}
		ScaleTransform target = EnsureScale(el);
		PlayTransform(el, target, ScaleTransform.ScaleXProperty, target.ScaleX, 0.97, ms, EaseIn);
		PlayTransform(el, target, ScaleTransform.ScaleYProperty, target.ScaleY, 0.97, ms, EaseIn);
		Play(el, Visual.OpacityProperty, el.Opacity, 0.0, ms, EaseIn, done);
	}

	public static void SlideIn(Control el, double fromX = 40.0, double ms = 240.0)
	{
		if (el is null)
		{
			return;
		}
		if (!Enabled)
		{
			el.Opacity = 1.0;
			return;
		}
		TranslateTransform translate = EnsureTranslate(el);
		el.Opacity = 0.0;
		translate.X = fromX;
		Play(el, Visual.OpacityProperty, 0.0, 1.0, ms, EaseOut);
		PlayTransform(el, translate, TranslateTransform.XProperty, fromX, 0.0, ms + 60.0, EaseOut);
	}

	public static void SlideOut(Control el, Action done, double toX = 40.0, double ms = 180.0)
	{
		if (el is null)
		{
			done?.Invoke();
			return;
		}
		if (!Enabled)
		{
			done();
			return;
		}
		TranslateTransform translate = EnsureTranslate(el);
		PlayTransform(el, translate, TranslateTransform.XProperty, 0.0, toX, ms, EaseIn);
		Play(el, Visual.OpacityProperty, el.Opacity, 0.0, ms, EaseIn, done);
	}

	public static void PageSwap(ContentControl host, Control next, bool forward = true)
	{
		if (host is null || next is null)
		{
			return;
		}
		int gen = ++_swapGen;
		Control? old = host.Content as Control;
		if (!Enabled || old is null || old == next)
		{
			if (old != null && old != next)
			{
				Reset(old);
			}
			host.Content = next;
			if (Enabled)
			{
				FadeIn(next, 230.0, 12.0);
			}
			else
			{
				Reset(next);
			}
			return;
		}
		PlayTransform(old, EnsureTranslate(old), TranslateTransform.YProperty, 0.0, forward ? (-8.0) : 8.0, 110.0, EaseIn);
		Play(old, Visual.OpacityProperty, old.Opacity, 0.0, 110.0, EaseIn, delegate
		{
			Reset(old);
			if (gen == _swapGen)
			{
				host.Content = next;
				FadeIn(next, 250.0, forward ? 14.0 : (-14.0));
			}
		});
	}

	private static void Reset(Control el)
	{
		el.Opacity = 1.0;
		if (el.RenderTransform is not TransformGroup group)
		{
			return;
		}
		foreach (ITransform child in group.Children)
		{
			if (child is TranslateTransform translate)
			{
				translate.X = 0.0;
				translate.Y = 0.0;
			}
		}
	}
}

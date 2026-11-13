using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TipCL.Services;

/// <summary>
/// 「飞向任务页」chip 的 620ms 时间线 —— Windows <c>MainWindow.FlyToTasks</c>
/// (<c>MainWindow.xaml.cs:1379-1424</c>)里那四段 <c>BeginAnimation</c> 的 Avalonia 11 等价物。
/// 调用点在 <see cref="TipCL.MainWindow.FlyToTasks"/>;本类是**纯运动学**(不建控件、不碰外壳)。
///
/// Windows 的四条曲线(逐条照抄,常量都摊在下面):
/// <list type="number">
/// <item>X:<c>0 → dx</c>,620ms,<c>CubicEase EaseInOut</c>;</item>
/// <item>Y:两个关键帧 —— 45% 处 <c>dy*0.28-46</c>(<c>CubicEase EaseOut</c>)、100% 处 <c>dy</c>(<c>CubicEase EaseIn</c>);</item>
/// <item>透明度:15% = 1、72% = 1、100% = 0(线性关键帧);</item>
/// <item>缩放:<c>0.7 → 0.42</c>,620ms,<c>CubicEase EaseInOut</c>(X/Y 各一条,Windows 是 <c>Clone()</c>)。</item>
/// </list>
///
/// **移植差异(如实记录;逐条见 PROGRESS 的 C3d 小节与 DECISIONS D-129)**:
/// <list type="number">
/// <item>Avalonia 的 <see cref="Animation"/> 只有**一条**整段缓动(<c>Animation.Easing</c>),没有 WPF
///   「每个关键帧自带缓动」那种能力。所以 X / 缩放走两关键帧 + 真缓动(单段,缓动就是曲线本身,
///   与 Windows 逐点相同);Y 的两段不同缓动按 <see cref="YSampleStep"/>(2.5% = 15.5ms 一点)采样成
///   关键帧、线性铺开 —— 与 Windows 曲线的偏差远小于 1px;</item>
/// <item>透明度那三个关键帧本来就是线性的,直接照搬(WPF 在第一个关键帧之前取属性的基值 1,
///   所以 0%→15% 恒定是 1,mac 显式写一个 0% 的 1 与之等价);</item>
/// <item>驱动方式:Windows 用 WPF 的合成时钟,mac 用 <c>Animation.RunAsync</c>(与 <see cref="Motion"/>
///   的其它成员同一套机制)。**没有时钟**(离屏 / 测试)时动画起不来,<see cref="Start"/> 会立刻落终态
///   并回调 —— 绝不让 chip 永久挂在宿主里(泄漏纪律)。调用方的守卫已经挡在 <c>Motion.Enabled</c> 上。</item>
/// </list>
/// </summary>
internal static class FlyMotion
{
	/// <summary>【Windows 源】<c>TimeSpan.FromMilliseconds(620.0)</c>。Windows 这里**不乘** <c>Motion.Scale</c>,mac 也不乘。</summary>
	internal const double DurationMs = 620.0;

	/// <summary>【Windows 源】chip 的起始缩放 <c>new ScaleTransform(0.7, 0.7)</c>。</summary>
	internal const double ScaleFrom = 0.7;

	/// <summary>【Windows 源】结束缩放 <c>new DoubleAnimation(0.7, 0.42, timeSpan)</c>。</summary>
	internal const double ScaleTo = 0.42;

	/// <summary>【Windows 源】Y 第一个关键帧的百分位(45%)。</summary>
	internal const double YMidCue = 0.45;

	/// <summary>【Windows 源】Y 第一个关键帧的公式:<c>dy * 0.28 - 46.0</c>。</summary>
	internal const double YMidFactor = 0.28;

	internal const double YMidLift = 46.0;

	/// <summary>【Windows 源】透明度保持满值的最后一个关键帧(72%);100% 处淡到 0。</summary>
	internal const double OpacityHoldCue = 0.72;

	/// <summary>【Windows 源】透明度第一个关键帧(15%,值仍是 1)。</summary>
	internal const double OpacityStartCue = 0.15;

	/// <summary>
	/// Y 曲线的采样步长(见类注释的差异 1)。0.025 能整除 <see cref="YMidCue"/>(18 步)与 1.0(40 步),
	/// 所以 45% 那个关键帧**正好**落在采样点上,不会被采样误差吃掉。
	/// </summary>
	internal const double YSampleStep = 0.025;

	private static readonly Easing EaseIn = new CubicEaseIn();

	private static readonly Easing EaseOut = new CubicEaseOut();

	private static readonly Easing EaseInOut = new CubicEaseInOut();

	/// <summary>X 位移(WPF:<c>0 → dx</c>,CubicEase EaseInOut)。</summary>
	internal static double XAt(double dx, double t)
	{
		return dx * EaseInOut.Ease(Clamp01(t));
	}

	/// <summary>Y 第一个关键帧的值(WPF:<c>dy * 0.28 - 46.0</c>)。</summary>
	internal static double YMid(double dy)
	{
		return dy * YMidFactor - YMidLift;
	}

	/// <summary>
	/// Y 位移。WPF 是两段关键帧:0→<see cref="YMid"/>(0%~45%,CubicEase <b>EaseOut</b>)、
	/// <see cref="YMid"/>→dy(45%~100%,CubicEase <b>EaseIn</b>)。
	/// </summary>
	internal static double YAt(double dy, double t)
	{
		t = Clamp01(t);
		double mid = YMid(dy);
		if (t <= YMidCue)
		{
			return mid * EaseOut.Ease(t / YMidCue);
		}
		return mid + (dy - mid) * EaseIn.Ease((t - YMidCue) / (1.0 - YMidCue));
	}

	/// <summary>透明度(WPF 的线性关键帧:15% = 1、72% = 1、100% = 0;15% 之前取基值 1)。</summary>
	internal static double OpacityAt(double t)
	{
		t = Clamp01(t);
		if (t <= OpacityHoldCue)
		{
			return 1.0;
		}
		return 1.0 - (t - OpacityHoldCue) / (1.0 - OpacityHoldCue);
	}

	/// <summary>缩放(<see cref="ScaleFrom"/> → <see cref="ScaleTo"/>,CubicEase EaseInOut)。</summary>
	internal static double ScaleAt(double t)
	{
		return ScaleFrom + (ScaleTo - ScaleFrom) * EaseInOut.Ease(Clamp01(t));
	}

	/// <summary>
	/// 起这条时间线。<paramref name="chip"/> 必须已经挂进可视树、且 <c>RenderTransform</c> 里
	/// 有 <c>ScaleTransform</c> + <c>TranslateTransform</c>(外壳按 Windows 的顺序建好)。
	///
	/// <paramref name="done"/> 在**整段跑完**后被调一次(= Windows 挂在缩放动画上的 <c>Completed</c>,
	/// 外壳在那里把 chip 从 <c>FlyHost</c> 摘掉);动画起不来(没有时钟 / 起动画抛异常)时也**立刻**回调一次。
	/// </summary>
	internal static void Start(Control chip, double dx, double dy, Action? done)
	{
		if (chip is null)
		{
			done?.Invoke();
			return;
		}

		TimeSpan duration = TimeSpan.FromMilliseconds(DurationMs);
		Animation scaleX = TwoFrame(ScaleTransform.ScaleXProperty, ScaleFrom, ScaleTo, duration, EaseInOut);
		Animation scaleY = TwoFrame(ScaleTransform.ScaleYProperty, ScaleFrom, ScaleTo, duration, EaseInOut);
		Animation moveX = TwoFrame(TranslateTransform.XProperty, 0.0, dx, duration, EaseInOut);

		// 【Windows 源】Completed 挂在缩放那条上(Windows 的 doubleAnimation 就是 0.7 → 0.42)。
		Start(scaleX, chip, done);
		Start(scaleY, chip, null);
		Start(moveX, chip, null);
		Start(BakedY(dy, duration), chip, null);
		Start(OpacityFrames(duration), chip, null);
	}

	/// <summary>
	/// 单属性两帧动画(等价于 WPF 的 <c>DoubleAnimation(from, to, duration){ EasingFunction = ... }</c>)。
	/// 只有两帧,所以 <c>Animation.Easing</c> 作用在整段进度上 —— 与 WPF 的单段缓动**逐点相同**。
	/// </summary>
	private static Animation TwoFrame(AvaloniaProperty property, double from, double to, TimeSpan duration, Easing easing)
	{
		return new Animation
		{
			Duration = duration,
			FillMode = FillMode.Forward,
			Easing = easing,
			Children =
			{
				new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(property, from) } },
				new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(property, to) } },
			},
		};
	}

	/// <summary>Y 曲线:按 <see cref="YSampleStep"/> 采样成关键帧(线性),45% 那个点精确落在采样点上。</summary>
	private static Animation BakedY(double dy, TimeSpan duration)
	{
		Animation animation = new Animation { Duration = duration, FillMode = FillMode.Forward };
		int steps = (int)Math.Round(1.0 / YSampleStep);
		for (int i = 0; i <= steps; i++)
		{
			double cue = (double)i * YSampleStep;
			animation.Children.Add(new KeyFrame
			{
				Cue = new Cue(cue),
				Setters = { new Setter(TranslateTransform.YProperty, YAt(dy, cue)) },
			});
		}
		return animation;
	}

	/// <summary>透明度:15% / 72% / 100% 三个关键帧(外加 0% 的 1,等价于 WPF 取基值)。</summary>
	private static Animation OpacityFrames(TimeSpan duration)
	{
		Animation animation = new Animation { Duration = duration, FillMode = FillMode.Forward };
		double[] cues = new double[4] { 0.0, OpacityStartCue, OpacityHoldCue, 1.0 };
		foreach (double cue in cues)
		{
			animation.Children.Add(new KeyFrame
			{
				Cue = new Cue(cue),
				Setters = { new Setter(Visual.OpacityProperty, OpacityAt(cue)) },
			});
		}
		return animation;
	}

	/// <summary>
	/// 起一条动画。与 <see cref="Motion"/> 内部的同名私有方法同一口径:拿不到时钟 / 抛异常时
	/// **立刻**把 <paramref name="done"/> 走掉(装饰性动画失败绝不许把 chip 留在宿主里)。
	/// </summary>
	private static void Start(Animation animation, Control target, Action? done)
	{
		try
		{
			Task task = animation.RunAsync(target);
			if (done is null)
			{
				return;
			}
			task.ContinueWith(
				delegate
				{
					Dispatcher.UIThread.Post(done);
				},
				CancellationToken.None,
				TaskContinuationOptions.None,
				TaskScheduler.Default);
		}
		catch (Exception)
		{
			done?.Invoke();
		}
	}

	private static double Clamp01(double t)
	{
		if (t < 0.0)
		{
			return 0.0;
		}
		if (t > 1.0)
		{
			return 1.0;
		}
		return t;
	}
}
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
/// </summary>
public static class Motion
{
	private static bool _enabled = true;

	private static int _swapGen;

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

	/// <summary>有没有人推进动画时钟:只有真桌面生命周期才算(测试/离屏环境里是 null)。</summary>
	internal static bool HasClock => Application.Current?.ApplicationLifetime is not null;

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

	/// <summary>两帧动画(from -> to)并落终态。真跑动画时 <paramref name="done"/> 在动画结束后调,
	/// 关掉动画时立刻调(调用方负责先落终态)。</summary>
	private static void Play(Animatable target, AvaloniaProperty<double> property, double from, double to, double ms, Easing? easing, Action? done = null)
	{
		if (target is null || !Enabled)
		{
			done?.Invoke();
			return;
		}

		Animation animation = new Animation
		{
			Duration = D(ms),
			FillMode = FillMode.Forward,
			Easing = easing,
			Children =
			{
				new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(property, from) } },
				new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(property, to) } },
			},
		};

		Start(animation, target, done);
	}

	/// <summary>无限/长动画的起法(与 Lucide 的 Run 同一口径:拿不到时钟就直接返回,失败也吞掉)。</summary>
	private static void Start(Animation animation, Animatable target, Action? done)
	{
		if (target is null)
		{
			done?.Invoke();
			return;
		}

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
			// 动画是装饰性的:任何失败都不许把界面带崩,收尾回调照走(调用方已落终态)。
			done?.Invoke();
		}
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
		Play(translate, TranslateTransform.YProperty, fromY, 0.0, ms + 60.0, EaseOut);
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
				Play(sc, ScaleTransform.ScaleXProperty, sc.ScaleX, scale, 170.0, EaseOut);
				Play(sc, ScaleTransform.ScaleYProperty, sc.ScaleY, scale, 170.0, EaseOut);
			}
			Play(tt, TranslateTransform.YProperty, tt.Y, 0.0 - lift, 170.0, EaseOut);
		};
		el.PointerExited += delegate
		{
			hovering = false;
			if (sc != null)
			{
				Play(sc, ScaleTransform.ScaleXProperty, sc.ScaleX, 1.0, 190.0, EaseOut);
				Play(sc, ScaleTransform.ScaleYProperty, sc.ScaleY, 1.0, 190.0, EaseOut);
			}
			Play(tt, TranslateTransform.YProperty, tt.Y, 0.0, 190.0, EaseOut);
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

	/// <summary>多关键帧动画(与 WPF 的 <c>DoubleAnimationUsingKeyFrames</c> 等价:关键帧均匀铺开)。</summary>
	private static void PlayFrames(Animatable target, AvaloniaProperty<double> property, double[] values, double ms, Easing? easing, Action? done = null)
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

		Animation animation = new Animation
		{
			Duration = D(ms),
			FillMode = FillMode.Forward,
			Easing = easing,
		};
		for (int i = 0; i < values.Length; i++)
		{
			animation.Children.Add(new KeyFrame
			{
				Cue = new Cue((double)i / (double)(values.Length - 1)),
				Setters = { new Setter(property, values[i]) },
			});
		}

		Start(animation, target, done);
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
		PlayFrames(target, ScaleTransform.ScaleXProperty, new double[3] { 1.0, peak, 1.0 }, 280.0, EaseOut);
		PlayFrames(target, ScaleTransform.ScaleYProperty, new double[3] { 1.0, peak, 1.0 }, 280.0, EaseOut);
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
		PlayFrames(target, TranslateTransform.XProperty, new double[6] { -7.0, 6.0, -4.0, 3.0, -1.5, 0.0 }, 330.0, EaseOut);
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
			Play(tx, TranslateTransform.XProperty, 0.0 - layer.Bounds.Width - 40.0, toValue, 1600.0, EaseInOut);
			PlayFrames(layer, Visual.OpacityProperty, new double[4] { 0.0, 0.5, 0.5, 0.0 }, 1600.0, null);
		}
	}

	public static void Spin(Control owner, RotateTransform rot, double seconds = 1.0)
	{
		if (owner is null || rot is null)
		{
			return;
		}
		Loop(owner, delegate
		{
			Animation animation = new Animation
			{
				Duration = TimeSpan.FromSeconds(seconds),
				IterationCount = IterationCount.Infinite,
				Children =
				{
					new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
					new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } },
				},
			};
			Start(animation, rot, null);
		}, delegate
		{
			rot.Angle = 0.0;
		});
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
		Play(scale, ScaleTransform.ScaleXProperty, 0.96, 1.0, ms + 60.0, EaseOut);
		Play(scale, ScaleTransform.ScaleYProperty, 0.96, 1.0, ms + 60.0, EaseOut);
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
		Play(target, ScaleTransform.ScaleXProperty, target.ScaleX, 0.97, ms, EaseIn);
		Play(target, ScaleTransform.ScaleYProperty, target.ScaleY, 0.97, ms, EaseIn);
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
		Play(translate, TranslateTransform.XProperty, fromX, 0.0, ms + 60.0, EaseOut);
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
		Play(EnsureTranslate(el), TranslateTransform.XProperty, 0.0, toX, ms, EaseIn);
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
		Play(EnsureTranslate(old), TranslateTransform.YProperty, 0.0, forward ? (-8.0) : 8.0, 110.0, EaseIn);
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

// 帧率角标(FPS 计数器):**默认关闭**,开关埋在「设置 › 左导航栏最底部」的「显示帧率」小行里。
//
// 为什么用 TopLevel.RequestAnimationFrame 计帧:Avalonia 每渲染一帧回调一次,与合成器刷新
// 同一节拍 ⇒ 量到的是界面**真实帧率**,而不是 DispatcherTimer 的名义频率(定时器只能证明
// 「定时器在跑」,画面卡住时它照样准点跳,读数永远是 60)。
//
// 四条硬约束:
//   1) 默认关(config.json 的 ui_show_fps = false,见 Config.BuildDefaults):关闭状态下不注册
//      任何渲染回调、不建可见控件 ⇒ 对启动耗时与无头测试(TestApp)零影响;
//   2) 每个窗口最多挂一份角标(Attach 幂等),窗口 Closed 时整份摘掉,静态表里不留窗口引用;
//   3) 角标 IsHitTestVisible=false:它盖在窗口右下角,绝不能吃掉鼠标命中(否则右下角的按钮点不到);
//   4) 计帧循环有「续命」兜底:每 500ms 采样时若发现上一帧回调没回来(窗口最小化 / 渲染循环
//      暂停),补一次 RequestAnimationFrame,免得循环死在半路、开关开着却再也不更新。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Common;

/// <summary>
/// 界面帧率角标。开关落 config.json 的 <see cref="ConfigKey"/>;默认关。
/// 开关 UI 在设置页左导航栏底部(见 Pages/Settings/SettingsPage.cs),挂载点在 MainWindow 构造。
/// </summary>
public static class FpsOverlay
{
    /// <summary>config.json 里的开关键(默认 false)。</summary>
    public const string ConfigKey = "ui_show_fps";

    /// <summary>采样周期(毫秒):半秒一次,读数不至于抖得像跑马灯。</summary>
    private const double SampleMs = 500;

    /// <summary>已挂载的角标(每个窗口一份)。</summary>
    private static readonly List<Attachment> _live = new();

    /// <summary>null = 还没读过配置;首次查询时按 config.json 懒读一次。</summary>
    private static bool? _state;

    /// <summary>当前是否显示帧率(读不到配置一律当关)。</summary>
    public static bool Enabled => _state ??= ReadConfig();

    /// <summary>读 config.json 的开关值(缺键 / 类型不符 / 读崩 → false)。</summary>
    public static bool ReadConfig()
    {
        try
        {
            return Py.Truthy(Config.CONFIG.Get(ConfigKey, Py.Box(false)));
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取帧率开关失败(按关闭处理): " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>设置页开关调用:写盘 + 立即生效。写盘失败只记日志,不影响本次会话的显示。</summary>
    public static void SetEnabled(bool on)
    {
        try
        {
            Config.CONFIG.Set(ConfigKey, Py.Box(on));
            Config.CONFIG.Save();
        }
        catch (Exception ex)
        {
            AppLog.Warn("保存帧率开关失败(本次会话仍生效): " + ex.GetType().Name + ": " + ex.Message);
        }
        Apply(on);
    }

    /// <summary>只改运行态、不写盘(装载回填 / 无头测试用)。</summary>
    public static void Apply(bool on)
    {
        _state = on;
        foreach (var item in _live.ToArray())
            item.Sync(on);
    }

    /// <summary>把角标挂到主窗口(幂等);显示与否取当前 <see cref="Enabled"/>。</summary>
    public static void Attach(Window window)
    {
        if (window == null) return;
        foreach (var existing in _live)
            if (ReferenceEquals(existing.Window, window)) return;

        var item = new Attachment(window);
        if (!item.Mount()) return;
        _live.Add(item);
        window.Closed += (_, _) =>
        {
            _live.Remove(item);
            item.Detach();
        };
        item.Sync(Enabled);
    }

    /// <summary>测试用:取某窗口挂着的角标控件(没挂 → null)。</summary>
    internal static Control? OverlayOf(Window window)
    {
        foreach (var item in _live)
            if (ReferenceEquals(item.Window, window)) return item.Root;
        return null;
    }

    /// <summary>一个窗口上的角标:一个半透明小药丸 + 计帧循环(120 行里最脏的部分都在这里)。</summary>
    private sealed class Attachment
    {
        private readonly Stopwatch _clock = new();
        private readonly TextBlock _text;
        private DispatcherTimer? _timer;
        private int _frames;
        private double _lastMs;
        private bool _looping;
        private bool _pending;

        public Attachment(Window window)
        {
            Window = window;
            _text = new TextBlock
            {
                Text = Placeholder,
                FontSize = 11,
                FontFamily = new FontFamily(TipCL.App.Ui.Ui.FontMono),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 深色半透明底 + 白字:明暗两套主题下都读得清(角标不参与主题换色,免得跟内容撞色)
            Root = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 3),
                Background = new SolidColorBrush(Color.Parse("#A6000000")),
                Child = _text,
                IsHitTestVisible = false,
                IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
            };
        }

        private const string Placeholder = "— FPS";

        public Window Window { get; }

        public Border Root { get; }

        /// <summary>把角标塞进窗口根部(盖在最后一层)。根部不是 Grid/Panel 时放弃挂载(不改窗口结构)。</summary>
        public bool Mount()
        {
            if (Window.Content is Grid grid)
            {
                // 跨满所有行 → 右下角就是**整个窗口**的右下角(标题栏那 46px 也算进去)
                int rows = grid.RowDefinitions.Count > 0 ? grid.RowDefinitions.Count : 1;
                Grid.SetRow(Root, 0);
                Grid.SetRowSpan(Root, rows);
                grid.Children.Add(Root);
                return true;
            }
            if (Window.Content is Panel panel)
            {
                panel.Children.Add(Root);
                return true;
            }
            return false;
        }

        /// <summary>开关切到 on/off(可见性 + 计帧循环一起动)。</summary>
        public void Sync(bool on)
        {
            Root.IsVisible = on;
            if (on) Start();
            else Stop();
        }

        /// <summary>从窗口根部摘掉并停表(窗口关闭时调)。</summary>
        public void Detach()
        {
            Stop();
            if (Root.Parent is Panel parent) parent.Children.Remove(Root);
        }

        private void Start()
        {
            if (_looping) return;
            _looping = true;
            _frames = 0;
            _lastMs = 0;
            _clock.Restart();
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SampleMs) };
                _timer.Tick += (_, _) => Sample();
            }
            _timer.Start();
            Request();
        }

        private void Stop()
        {
            _looping = false;
            _pending = false;
            _timer?.Stop();
            _frames = 0;
            _lastMs = 0;
            _clock.Reset();
            _text.Text = Placeholder;
        }

        /// <summary>排队下一帧。窗口/渲染循环已经拆掉时静默收摊(收尾竞态不该炸进程)。</summary>
        private void Request()
        {
            if (!_looping) return;
            _pending = true;
            try
            {
                Window.RequestAnimationFrame(OnFrame);
            }
            catch (Exception)
            {
                _pending = false;
                Stop();
            }
        }

        private void OnFrame(TimeSpan _)
        {
            _pending = false;
            if (!_looping) return;
            _frames++;
            Request();
        }

        /// <summary>半秒一次:frames / 实际经过时间 = 帧率。</summary>
        private void Sample()
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            double span = now - _lastMs;
            _lastMs = now;
            int frames = _frames;
            _frames = 0;
            if (span > 1)
                _text.Text = $"{(int)Math.Round(frames * 1000.0 / span)} FPS";
            // 续命:上一帧回调没回来说明渲染循环停过(最小化 / 遮挡 / 驱动挂起),补排一帧
            if (!_pending) Request();
        }
    }
}
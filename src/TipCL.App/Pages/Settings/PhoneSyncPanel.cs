// 来源: Windows 版参考仓库 src/TipCL/Pages/PhoneSyncPanel.cs(205 行,M4-H R3 移植)。
//
// 手机同步的「世界同步/对比」面板:左边选本机哪个实例(版本),右边选手机上的存档根,
// 然后对比两边世界(一致 / 电脑较新 / 手机较新 / 两边都改过 / 只在一边),
// 逐项「传到手机 / 传到电脑」,或「一键同步」按裁决批量传(冲突项逐个问)。
//
// 与 Windows 的差别只在承载面:参考版用 Dlg.Panel(WPF 模态面板),Linux 侧用 Dlg.OpenPanel
// 的非模态面板(与 AI 权限面板同一套,见 DECISIONS D-24 的同一口径)。数据面全部走 Core 的
// PhoneLink / SaveSync(与 Windows 同名同语义),没有第二份实现。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher.Sync;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Settings;

/// <summary>手机存档同步面板(对比 + 逐项传 + 一键同步)。</summary>
internal sealed class PhoneSyncPanel : Border
{
    private readonly List<SaveSync.Target> _targets = new();
    private readonly List<PhoneClient.Root> _roots = new();
    private readonly ComboBox _pc = UiClass.Combo(null, null, 200);
    private readonly ComboBox _phone = UiClass.Combo(null, null, 200);
    private readonly StackPanel _rows = UiClass.V(6);
    private readonly TextBox _log = UiClass.LogBox();
    private readonly TextBlock _status = UiClass.Small("");
    private readonly Button _refresh = UiClass.Btn("刷新对比", BtnKind.Normal);
    private readonly Button _all = UiClass.Btn("一键同步", BtnKind.Primary);
    private bool _busy;

    public PhoneSyncPanel()
    {
        _log.Height = 90;
        _log.IsReadOnly = true;
        _refresh.Click += (_, _) => _ = RunAsync(ReloadAsync);
        _all.Click += (_, _) => _ = RunAsync(SyncAllAsync);
        _pc.SelectionChanged += (_, _) =>
        {
            int before = _phone.SelectedIndex;
            PickRoot();
            if (_phone.SelectedIndex == before)
            {
                _ = RunAsync(ReloadAsync);
            }
        };
        _phone.SelectionChanged += (_, _) => _ = RunAsync(ReloadAsync);

        var top = UiClass.G(null, "Auto,Auto,Auto,Auto,*,Auto,Auto");
        top.Add(UiClass.Txt("电脑", 12.5, true).VCenter(), 0, 0);
        var pcBox = _pc;
        pcBox.Margin = new Avalonia.Thickness(8, 0, 14, 0);
        top.Add(pcBox.VCenter(), 0, 1);
        top.Add(UiClass.Txt("手机", 12.5, true).VCenter(), 0, 2);
        _phone.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        top.Add(_phone.VCenter(), 0, 3);
        _refresh.Margin = new Avalonia.Thickness(0, 0, 6, 0);
        top.Add(_refresh.VCenter(), 0, 5);
        top.Add(_all.VCenter(), 0, 6);

        var scroll = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = 300,
            MinWidth = 640,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        Child = UiClass.V(10,
            UiClass.Section("手机同步", "两边游戏版本要一致,用旧版本打开新存档会损坏"),
            _status,
            top,
            scroll,
            _log);
        Padding = new Avalonia.Thickness(4);
        if (PhoneLink.Current == null)
        {
            // 没连接是本地就能判断的,构造时先如实写出来(挂到可视树后再做一次联网初始化)
            _status.Text = "还没有连接手机:先在设置页填地址与配对码并点「连接」";
        }
        AttachedToVisualTree += (_, _) => _ = InitAsync();
    }

    private async Task InitAsync()
    {
        if (PhoneLink.Current == null)
        {
            _status.Text = "还没有连接手机:先在设置页填地址与配对码并点「连接」";
            return;
        }
        PhoneClient.Info? info = PhoneLink.CurrentInfo;
        _status.Text = "已连接 " + (info?.Device ?? "") +
                       (string.IsNullOrEmpty(info?.Android) ? "" : " · Android " + info.Android);
        await RunAsync(async () =>
        {
            List<SaveSync.Target> targets = await Task.Run(() => SaveSync.Targets());
            List<PhoneClient.Root> roots = await Task.Run(() => PhoneLink.Roots());
            _targets.Clear();
            _targets.AddRange(targets);
            if (_roots.Count == 0 && roots.Count == 0)
            {
                roots.Add(new PhoneClient.Root { Id = "global", Label = "全局存档" });
            }
            _roots.Clear();
            _roots.AddRange(roots);
            if (_targets.Count == 0)
            {
                _status.Text = "电脑上还没有实例:先在「实例库」建一个再回来";
                _refresh.IsEnabled = false;
                _all.IsEnabled = false;
                return;
            }
            _pc.ItemsSource = _targets.Select(t => t.Label).ToList();
            _pc.SelectedIndex = 0;
            _phone.ItemsSource = _roots.Select(r => string.IsNullOrEmpty(r.Label) ? r.Id : r.Label).ToList();
            PickRoot();
        });
    }

    /// <summary>按当前电脑位置的默认规则挑手机存档根(照 Windows 版 PickRoot)。</summary>
    private void PickRoot()
    {
        if (_targets.Count == 0 || _roots.Count == 0)
        {
            return;
        }
        string id = PhoneLink.DefaultRoot(Target(), _roots);
        int index = _roots.FindIndex(r => r.Id == id);
        _phone.SelectedIndex = Math.Max(0, index);
    }

    private SaveSync.Target Target() => _targets[Math.Max(0, _pc.SelectedIndex)];

    private string RootId()
        => _phone.SelectedIndex >= 0 && _phone.SelectedIndex < _roots.Count ? _roots[_phone.SelectedIndex].Id : "global";

    private void Log(string text)
    {
        void Append()
        {
            _log.Text = (_log.Text ?? "") + "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\n";
            _log.CaretIndex = ((_log.Text ?? "").Length);
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            Append();
        }
        else
        {
            Dispatcher.UIThread.Post(Append);
        }
    }

    /// <summary>串行化操作(对比/传输都不许并发)并把异常打进日志 + Toast。</summary>
    private async Task RunAsync(Func<Task> body)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        _refresh.IsEnabled = false;
        _all.IsEnabled = false;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            ToastHost.Show("同步失败", ex.Message, ToastKind.Error);
        }
        finally
        {
            _busy = false;
            _refresh.IsEnabled = true;
            _all.IsEnabled = _targets.Count > 0;
        }
    }

    private static string Label(SyncDecision d) => DecisionLabel(d);

    /// <summary>裁决的中文标签(照 Windows 版 PhoneSyncPanel.Label;测试直接锁这套文案)。</summary>
    internal static string DecisionLabel(SyncDecision d) => d switch
    {
        SyncDecision.Same => "一致",
        SyncDecision.Push => "电脑较新",
        SyncDecision.Pull => "手机较新",
        SyncDecision.Conflict => "两边都改过",
        SyncDecision.OnlyLocal => "只在电脑",
        _ => "只在手机",
    };

    /// <summary>毫秒时间戳 → 本地时间(0 显示为「-」)。</summary>
    internal static string TimeText(long ms)
        => ms <= 0 ? "-" : DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    private static string Time(long ms) => TimeText(ms);

    /// <summary>连接状态那一行(测试用)。</summary>
    internal string StatusText => _status.Text ?? "";

    private async Task ReloadAsync()
    {
        if (_targets.Count == 0)
        {
            return;
        }
        SaveSync.Target target = Target();
        string root = RootId();
        List<PhoneLink.Row> rows = await Task.Run(() => PhoneLink.Compare(target, root));
        _rows.Children.Clear();
        if (rows.Count == 0)
        {
            _rows.Children.Add(UiClass.Empty("World", "两边都还没有存档", "装了地图存档后再回来对比"));
            return;
        }
        foreach (PhoneLink.Row row in rows)
        {
            PhoneLink.Row r = row;
            string sub = "电脑 " + (r.HasLocal ? Time(r.LocalMtime) : "-") +
                         " · 手机 " + (r.HasRemote ? Time(r.Remote.LastModified) : "-") +
                         (r.LocalLocked ? " · 游戏中" : "");
            var push = UiClass.Btn("传到手机", BtnKind.Chip, (_, _) => _ = RunAsync(async () =>
            {
                if (r.HasRemote)
                {
                    bool ok = await Dlg.Confirm("覆盖手机上的存档?", "手机上的「" + r.Name + "」会先备份到电脑,再用电脑上的版本覆盖。");
                    if (!ok)
                    {
                        return;
                    }
                }
                await Task.Run(() => PhoneLink.Push(target, root, r.Name, Log));
                Log("✓ " + r.Name + " → 手机");
                await ReloadAsync();
            }));
            var pull = UiClass.Btn("传到电脑", BtnKind.Chip, (_, _) => _ = RunAsync(async () =>
            {
                if (r.HasLocal)
                {
                    bool ok = await Dlg.Confirm("覆盖电脑上的存档?", "电脑上的「" + r.Name + "」会先自动备份,再用手机上的版本覆盖。");
                    if (!ok)
                    {
                        return;
                    }
                }
                await Task.Run(() => PhoneLink.Pull(target, root, r.Name, Log));
                Log("✓ " + r.Name + " → 电脑");
                await ReloadAsync();
            }));
            push.IsEnabled = r.HasLocal;
            pull.IsEnabled = r.HasRemote;

            var grid = UiClass.G(null, "*,Auto,Auto");
            grid.Add(UiClass.V(2, UiClass.Txt(r.Name, 13, true), UiClass.Small(sub)), 0, 0);
            grid.Add(UiClass.Tag(Label(r.Decision)).VCenter(), 0, 1);
            grid.Add(UiClass.H(6, push, pull).VCenter(), 0, 2);
            _rows.Children.Add(UiClass.RowCard(grid));
        }
    }

    /// <summary>一键同步:冲突项逐个问,其余按裁决方向传(照 Windows 版同一条链)。</summary>
    private async Task SyncAllAsync()
    {
        if (_targets.Count == 0)
        {
            return;
        }
        SaveSync.Target target = Target();
        string root = RootId();
        List<PhoneLink.Row> rows = await Task.Run(() => PhoneLink.Compare(target, root));
        var pick = new Dictionary<string, SyncDecision>();
        foreach (PhoneLink.Row r in rows.Where(x => x.Decision == SyncDecision.Conflict))
        {
            int choice = await Dlg.Choose(
                "「" + r.Name + "」两边都改过",
                UiClass.Muted("电脑 " + Time(r.LocalMtime) + " · 手机 " + Time(r.Remote.LastModified) + "\n被覆盖的一边会先自动备份。"),
                new[] { "跳过", "保留手机的", "保留电脑的" });
            pick[r.Name] = choice switch
            {
                1 => SyncDecision.Pull,
                2 => SyncDecision.Push,
                _ => SyncDecision.Same,
            };
        }
        List<string> result = await Task.Run(() => PhoneLink.SyncAll(target, root, r =>
            pick.TryGetValue(r.Name, out SyncDecision d) ? d : SyncDecision.Same, Log));
        Log(result.Count == 0 ? "两边已经一致" : string.Join("\n", result));
        await ReloadAsync();
    }
}

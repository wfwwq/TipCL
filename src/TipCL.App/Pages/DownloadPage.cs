using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C6a:Windows 版 <c>Pages\DownloadPage.cs</c>(7780 字节 / 265 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **下载页**(导航 id <c>download</c>)。
///
/// 形状(逐条照抄 Windows):顶部灰色轨道分段(原版 / Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界)+ 右上「任务 N」徽标;
/// 「原版」分段是本页自绘的版本列表(筛选行 + 版本卡两个区块,交给 <see cref="BlockHost"/> 排布与持久化),
/// 「安装」进入 <see cref="InstallPage"/> 子视图;其余 6 个分段内嵌 <see cref="CatalogPage"/>。
///
/// 行为(与 Windows 一致):<c>get_version_list</c> 起表 → <c>fetch_version_list</c>(带超时,失败且本地为空时 Toast)、
/// 三个筛选钮(正式版 / 快照 / 远古)+ 搜索框本地过滤、<c>TaskStore</c> 的运行数进徽标、切段把旧段 <c>OnHidden</c>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Style = Ui.S("键")</c> → <c>StyleKeys.Apply(控件, "键")</c>;<c>SetResourceReference</c> → <c>.Dyn(...)</c>;</item>
/// <item>行悬停时 Windows 直接换 <c>Style</c>(<c>Btn.Soft</c> ↔ <c>Btn.Primary</c>)—— Avalonia 侧样式是类名,
///   所以 <see cref="SetBtnStyle"/> 先把这几个按钮类名摘掉再挂新的(等价于"换样式",不是"叠样式");</item>
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>Brushes.Transparent</c> → <c>Avalonia.Media.Brushes.Transparent</c>;</item>
/// <item><c>Dispatcher.BeginInvoke(new Action(...))</c> → <c>Dispatcher.UIThread.Post(...)</c>(Avalonia 把 UI 线程队列
///   放在 <c>Dispatcher.UIThread</c> 上);</item>
/// <item><c>Ui.VirtualList&lt;T&gt;</c> 的委托类型由 <c>Func&lt;T, UIElement&gt;</c> 收成 <c>Func&lt;T, Control&gt;</c>(C1b 落这个门面时
///   就定了:C1c 的控件面全是 <c>Control</c>)。</item>
/// </list>
/// </summary>
public sealed class DownloadPage : PageBase
{
	/// <summary>远程版本列表拉取超时(Windows 是 <c>Smoke --smoke-search-timeout</c> 会同步调整的静态;逐字保留)。</summary>
	public static int FetchTimeoutMs = 9000;

	/// <summary>【Windows 源】<c>:21</c> 七个分段的标题(顺序即分段顺序)。</summary>
	private static readonly string[] Segs = new string[7] { I18n.L("原版"), "Mod", I18n.L("整合包"), I18n.L("数据包"), I18n.L("资源包"), I18n.L("光影"), I18n.L("世界") };

	/// <summary>【Windows 源】<c>:23</c> 分段 → 目录种类(第 0 段是自绘的原版列表,所以没有 kind)。</summary>
	private static readonly CatalogKind?[] Kinds = new CatalogKind?[7] { null, CatalogKind.Mod, CatalogKind.Modpack, CatalogKind.Datapack, CatalogKind.ResourcePack, CatalogKind.Shader, CatalogKind.World };

	private readonly PageBase?[] _subs = new PageBase[7];

	private readonly ToggleButton[] _segBtns = new ToggleButton[7];

	private readonly ContentControl _host = new ContentControl();

	private readonly Grid _shell = Ui.G("Auto,*");

	/// <summary>原版分段视图:筛选行 + 版本列表卡两个区块,交给 BlockHost 排布与持久化。</summary>
	private readonly BlockHost _vanillaHost;

	private readonly TextBox _search = Ui.Input(I18n.L("搜索版本"));

	private readonly Button[] _filters = new Button[3];

	private readonly ListBox _list;

	private readonly TextBlock _taskN = Ui.Txt("0", 11.0, bold: true, "B.OnAccentStrong");

	private List<VersionRow> _all = new List<VersionRow>();

	private int _seg;

	private int _filter;

	public override string Title => I18n.L("下载");

	public DownloadPage()
	{
		SPanel rail = Ui.H(0.0);
		for (int i = 0; i < 7; i++)
		{
			int k = i;
			ToggleButton t = new ToggleButton
			{
				Content = Segs[i],
			};
			StyleKeys.Apply(t, "Seg.Item");
			t.Click += delegate
			{
				ShowSeg(k);
			};
			_segBtns[i] = t;
			rail.Children.Add(t);
		}
		Border segRail = new Border
		{
			Child = rail,
			HorizontalAlignment = HorizontalAlignment.Left,
		};
		StyleKeys.Apply(segRail, "Seg.Rail");
		Border badge = Ui.Pane(_taskN.Center(), "B.AccentStrong", 8.0, 0.0);
		badge.Padding = new Thickness(6.0, 0.0, 6.0, 1.0);
		Button task = Ui.Btn("", BtnKind.Normal, delegate
		{
			Win?.Navigate("tasks");
		});
		task.Content = Ui.H(6.0, Ui.Glyph("\ue896", 13.0), Ui.Txt(I18n.L("任务"), 13.0, bold: true), badge);
		task.BorderThickness = new Thickness(0.0);
		Grid top = Ui.G(null, "*,Auto");
		top.Margin = new Thickness(22.0, 16.0, 22.0, 12.0);
		top.Add(segRail, 0);
		top.Add(task.VCenter(), 0, 1);
		_shell.Add(top, 0);
		_shell.Add(_host, 1);

		_search.TextChanged += delegate
		{
			Apply();
		};
		string[] fl = new string[3] { I18n.L("正式版"), I18n.L("快照"), I18n.L("远古") };
		Grid bar = Ui.G(null, "*,Auto");
		SPanel fs = Ui.H(4.0);
		for (int i = 0; i < 3; i++)
		{
			int k = i;
			_filters[i] = Ui.Btn(fl[i], BtnKind.Ghost, delegate
			{
				_filter = k;
				Apply();
			});
			fs.Children.Add(_filters[i]);
		}
		bar.Add(_search, 0);
		bar.Add(fs.M(8.0, 0.0, 0.0, 0.0), 0, 1);
		// 筛选行与列表卡的旧间距 12 = BlockHost 区块间距 10 + 行自留下边距 2,默认视觉与旧版逐像素一致
		bar.Margin = new Thickness(22.0, 0.0, 22.0, 2.0);
		_list = Ui.VirtualList<VersionRow>(RowUi, 0.0);
		Border card = Ui.Card(_list, 0.0);
		card.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		card.Margin = new Thickness(22.0, 0.0, 22.0, 16.0);
		// 布局自定义:页边距仍由 bar/card 自管,宿主边距传 0 防叠加
		_vanillaHost = new BlockHost("download", new List<BlockSpec>
		{
			new BlockSpec { Id = "filters", Title = I18n.L("筛选"), Content = bar },
			new BlockSpec { Id = "list", Title = I18n.L("版本列表"), Content = card },
		}, new Thickness(0.0));

		base.Content = _shell;
		TaskStore.Added += delegate
		{
			Dispatcher.UIThread.Post(SyncTasks);
		};
		TaskStore.Updated += delegate(TaskRow r)
		{
			if (r.Finished)
			{
				Dispatcher.UIThread.Post(SyncTasks);
			}
		};
		ShowSeg(0);
	}

	private void SyncTasks()
	{
		_taskN.Text = TaskStore.RunningCount.ToString();
	}

	/// <summary>
	/// 【Windows 源】<c>:143-163</c> 切分段:第 0 段是本页自绘的原版列表,其余段内嵌 <see cref="CatalogPage"/>(切到才建)。
	/// </summary>
	internal void ShowSeg(int k)
	{
		_subs[_seg]?.OnHidden();
		_seg = k;
		for (int i = 0; i < 7; i++)
		{
			_segBtns[i].IsChecked = i == k;
		}
		if (k == 0)
		{
			_host.Content = _vanillaHost.Root;
			return;
		}
		PageBase p = _subs[k] ?? (_subs[k] = new CatalogPage(Kinds[k]!));
		_host.Content = p;
		PageBase.Run(async delegate
		{
			await p.EnsureLoadedAsync();
			p.OnShown();
		});
	}

	/// <summary>【Windows 源】<c>:165-171</c> 进入安装子视图(它是本页的内容,不进导航注册)。</summary>
	internal void OpenInstall(VersionRow row)
	{
		base.Content = new InstallPage(row.Version, row.TypeLabel, delegate
		{
			base.Content = _shell;
		});
	}

	/// <summary>【Windows 源】<c>:173-205</c> 一行版本:版本号 + 类型标签 + 日期 + 「安装」。</summary>
	private Control RowUi(VersionRow r)
	{
		Button b = Ui.Btn(I18n.L("安装"), BtnKind.Soft, delegate
		{
			OpenInstall(r);
		}, "\ue896");
		bool hot = r.Type == "snapshot" || (_all.Count > 0 && r == _all.FirstOrDefault((VersionRow x) => x.Type == "release"));
		Grid g = Ui.G(null, "130,*,110,Auto");
		g.Add(Ui.Txt(r.Version, 13.5, bold: true).Trim().VCenter(), 0);
		g.Add(hot ? Ui.Tag(r.TypeLabel, "B.AccentText", "B.AccentSoft").Left() : Ui.Tag(r.TypeLabel).Left(), 0, 1);
		g.Add(Ui.Txt(r.Date, 12.5, bold: false, "B.InkFaint").VCenter(), 0, 2);
		g.Add(b.VCenter(), 0, 3);
		Border row = new Border
		{
			Height = 46.0,
			Padding = new Thickness(18.0, 0.0, 18.0, 0.0),
			BorderThickness = new Thickness(0.0, (_list.Items.Count > 0 && ReferenceEquals(_list.Items[0], r)) ? 0.0 : 1.0, 0.0, 0.0),
			Background = Brushes.Transparent,
			Child = g,
		};
		row.Dyn(Border.BorderBrushProperty, "B.RowLine");
		row.PointerEntered += delegate
		{
			row.Dyn(Border.BackgroundProperty, "B.RowHover");
			SetBtnStyle(b, "Btn.Primary");
		};
		row.PointerExited += delegate
		{
			row.Background = Brushes.Transparent;
			SetBtnStyle(b, "Btn.Soft");
		};
		return row;
	}

	/// <summary>
	/// 把这些按钮在三个 <c>Btn.*</c> 类名之间换一个(Windows 是直接换 <c>Style</c> 对象)。
	/// Avalonia 的样式是类名,直接 <c>Apply</c> 会**叠**上去 —— 所以先摘掉再挂,等价于"换样式"。
	/// </summary>
	private static void SetBtnStyle(Button b, string key)
	{
		foreach (string other in new[] { "Btn.Primary", "Btn.Soft", "Btn.Ghost" })
		{
			string cls = StyleKeys.Map(other);
			if (cls.Length > 0)
			{
				b.Classes.Remove(cls);
			}
		}
		string wanted = StyleKeys.Map(key);
		if (wanted.Length > 0)
		{
			b.Classes.Add(wanted);
		}
	}

	/// <summary>【Windows 源】<c>:207-216</c> 按筛选钮 + 搜索词本地过滤(不请求后端)。</summary>
	private void Apply()
	{
		for (int i = 0; i < 3; i++)
		{
			SetBtnStyle(_filters[i], (i == _filter) ? "Btn.Soft" : "Btn.Ghost");
			_filters[i].FontWeight = (i == _filter) ? FontWeight.Bold : FontWeight.Normal;
		}
		string q = _search.Text?.Trim() ?? "";
		_list.ItemsSource = _all
			.Where((VersionRow r) => (_filter == 0) ? (r.Type == "release") : ((_filter == 1) ? (r.Type == "snapshot") : r.Type.StartsWith("old_", StringComparison.Ordinal)))
			.Where((VersionRow r) => q.Length == 0 || r.Version.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
			.ToList();
	}

	protected override async Task LoadAsync()
	{
		SyncTasks();
		_all = (await Api.TryCallAsync("get_version_list", null, new List<VersionRow>())) ?? new List<VersionRow>();
		Apply();
		await FetchAsync();
	}

	/// <summary>【Windows 源】<c>:226-245</c>:远程清单带超时;失败时只在本地也是空的时候如实 Toast。</summary>
	private async Task FetchAsync()
	{
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(FetchTimeoutMs);
			List<VersionRow>? l = await Api.CallAsync<List<VersionRow>>("fetch_version_list", null, cts.Token);
			if (l != null && l.Count > 0)
			{
				_all = l;
				Apply();
			}
		}
		catch (Exception ex)
		{
			if (_all.Count == 0)
			{
				Toast(I18n.L("版本清单刷新失败"), ex.Message, ToastKind.Warning);
			}
		}
	}

	public override void OnShown()
	{
		SyncTasks();
		_subs[_seg]?.OnShown();
	}

	public override void OnHidden()
	{
		_subs[_seg]?.OnHidden();
	}

	public override void OnEvent(BridgeEvent ev)
	{
		foreach (PageBase? p in _subs)
		{
			p?.OnEvent(ev);
		}
	}
}

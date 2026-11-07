using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 下载页 v2（download2）：灰色轨道分段（原版 / Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界）+ 右上「任务 N」。
// 原版分段是本页自绘的版本列表，「安装」进入 InstallPage 子视图；其余分段内嵌既有 CatalogPage（业务不动）。
public sealed class DownloadPage : PageBase
{
	// 远程版本列表拉取超时（Smoke --smoke-search-timeout 会同步调整；原属 VersionPage，深清理后迁此）
	public static int FetchTimeoutMs = 9000;

	private static readonly string[] Segs = new string[7] { I18n.L("原版"), "Mod", I18n.L("整合包"), I18n.L("数据包"), I18n.L("资源包"), I18n.L("光影"), I18n.L("世界") };

	private static readonly CatalogKind[] Kinds = new CatalogKind[7] { null, CatalogKind.Mod, CatalogKind.Modpack, CatalogKind.Datapack, CatalogKind.ResourcePack, CatalogKind.Shader, CatalogKind.World };

	private readonly PageBase?[] _subs = new PageBase[7];

	private readonly ToggleButton[] _segBtns = new ToggleButton[7];

	private readonly ContentControl _host = new ContentControl();

	private readonly Grid _shell = Ui.G("Auto,*");

	private readonly Grid _vanilla = Ui.G("Auto,*");

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
				Style = Ui.S("Seg.Item"),
				Content = Segs[i]
			};
			t.Click += delegate
			{
				ShowSeg(k);
			};
			_segBtns[i] = t;
			rail.Children.Add(t);
		}
		Border segRail = new Border
		{
			Style = Ui.S("Seg.Rail"),
			Child = rail,
			HorizontalAlignment = HorizontalAlignment.Left
		};
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
		bar.Margin = new Thickness(22.0, 0.0, 22.0, 12.0);
		_list = Ui.VirtualList<VersionRow>(RowUi, 0.0);
		Border card = Ui.Card(_list, 0.0);
		card.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		card.Margin = new Thickness(22.0, 0.0, 22.0, 16.0);
		_vanilla.Add(bar, 0);
		_vanilla.Add(card, 1);

		base.Content = _shell;
		TaskStore.Added += delegate
		{
			Dispatcher.BeginInvoke(new Action(SyncTasks));
		};
		TaskStore.Updated += delegate(TaskRow r)
		{
			if (r.Finished)
			{
				Dispatcher.BeginInvoke(new Action(SyncTasks));
			}
		};
		ShowSeg(0);
	}

	private void SyncTasks()
	{
		_taskN.Text = TaskStore.RunningCount.ToString();
	}

	private void ShowSeg(int k)
	{
		_subs[_seg]?.OnHidden();
		_seg = k;
		for (int i = 0; i < 7; i++)
		{
			_segBtns[i].IsChecked = i == k;
		}
		if (k == 0)
		{
			_host.Content = _vanilla;
			return;
		}
		PageBase p = _subs[k] ?? (_subs[k] = new CatalogPage(Kinds[k]));
		_host.Content = p;
		Run(async delegate
		{
			await p.EnsureLoadedAsync();
			p.OnShown();
		});
	}

	public void OpenInstall(VersionRow row)
	{
		base.Content = new InstallPage(row.Version, row.TypeLabel, delegate
		{
			base.Content = _shell;
		});
	}

	private UIElement RowUi(VersionRow r)
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
			Background = System.Windows.Media.Brushes.Transparent,
			Child = g
		};
		row.SetResourceReference(Border.BorderBrushProperty, "B.RowLine");
		row.MouseEnter += delegate
		{
			row.SetResourceReference(Border.BackgroundProperty, "B.RowHover");
			b.Style = Ui.S("Btn.Primary");
		};
		row.MouseLeave += delegate
		{
			row.Background = System.Windows.Media.Brushes.Transparent;
			b.Style = Ui.S("Btn.Soft");
		};
		return row;
	}

	private void Apply()
	{
		for (int i = 0; i < 3; i++)
		{
			_filters[i].Style = Ui.S((i == _filter) ? "Btn.Soft" : "Btn.Ghost");
			_filters[i].FontWeight = (i == _filter) ? FontWeights.Bold : FontWeights.Normal;
		}
		string q = _search.Text?.Trim() ?? "";
		_list.ItemsSource = _all.Where((VersionRow r) => (_filter == 0) ? (r.Type == "release") : ((_filter == 1) ? (r.Type == "snapshot") : r.Type.StartsWith("old_", StringComparison.Ordinal))).Where((VersionRow r) => q.Length == 0 || r.Version.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
	}

	protected override async Task LoadAsync()
	{
		SyncTasks();
		_all = (await Api.TryCallAsync("get_version_list", null, new List<VersionRow>())) ?? new List<VersionRow>();
		Apply();
		await FetchAsync();
	}

	private async Task FetchAsync()
	{
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(DownloadPage.FetchTimeoutMs);
			List<VersionRow> l = await Api.CallAsync<List<VersionRow>>("fetch_version_list", null, cts.Token);
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
		foreach (PageBase p in _subs)
		{
			p?.OnEvent(ev);
		}
	}
}

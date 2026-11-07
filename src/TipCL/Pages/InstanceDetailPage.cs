using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 实例详情（v1 instance 结构 + v2 视觉）：返回 / 标题 / 启动；分段 模组·存档·资源包·光影·设置。
// 由实例库「管理」在页内切入（不注册导航）。业务全走既有后端方法；完整功能仍在 ModsDialog / SavesDialog，「设置」内嵌 VersionManagePage。
public sealed class InstanceDetailPage : PageBase
{
	private static readonly string[] Segs = new string[5] { I18n.L("模组"), I18n.L("存档"), I18n.L("资源包"), I18n.L("光影"), I18n.L("设置") };

	private readonly string _inst;

	private readonly ToggleButton[] _segBtns = new ToggleButton[5];

	private readonly ContentControl _host = new ContentControl();

	private readonly SPanel _rows = Ui.V(0.0);

	private readonly Button _more;

	private readonly SmoothScroll _listView;

	private VersionManagePage? _settings;

	private int _seg;

	public override string Title => _inst;

	public InstanceDetailPage(InstanceInfo info, Action back)
	{
		_inst = info.Name;
		Button backBtn = Ui.IconBtn("\ue72b", I18n.L("返回"), delegate
		{
			back();
		});
		TextBlock title = new TextBlock
		{
			Text = _inst,
			Style = Ui.S("T.PageTitle"),
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		string meta = string.Join(" · ", new string[2] { string.IsNullOrEmpty(info.Mc) ? "" : ("Minecraft " + info.Mc), info.Pack }.Where((string s) => !string.IsNullOrEmpty(s)));
		Button launch = Ui.Btn(I18n.L("启动"), BtnKind.Primary, delegate
		{
			if (Win?.GetPage("launch") is LaunchPage lp)
			{
				Run(() => lp.LaunchNowAsync(_inst));
			}
		}, "\ue768");
		Grid head = Ui.G(null, "Auto,*,Auto");
		head.Margin = new Thickness(22.0, 16.0, 22.0, 12.0);
		head.Add(backBtn.VCenter().M(0.0, 0.0, 8.0), 0);
		head.Add(Ui.V(2.0, title, meta.Length == 0 ? null : Ui.Txt(meta, 12.0, bold: false, "B.InkMuted").Trim()).VCenter(), 0, 1);
		head.Add(launch.VCenter(), 0, 2);

		SPanel rail = Ui.H(0.0);
		for (int i = 0; i < 5; i++)
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
		// 右侧：打开既有完整管理对话框（导入/检查更新/备份等不在本页重做）
		_more = Ui.Btn(I18n.L("更多管理"), BtnKind.Chip, delegate
		{
			Run(() => (_seg == 0) ? ModsDialog.ShowAsync(_inst) : SavesDialog.ShowAsync(_inst, ""));
		});
		Grid bar = Ui.G(null, "*,Auto");
		bar.Margin = new Thickness(22.0, 0.0, 22.0, 12.0);
		bar.Add(new Border
		{
			Style = Ui.S("Seg.Rail"),
			Child = rail,
			HorizontalAlignment = HorizontalAlignment.Left
		}, 0);
		bar.Add(_more.VCenter(), 0, 1);

		Border card = Ui.Card(_rows, 0.0);
		card.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		card.Margin = new Thickness(22.0, 0.0, 22.0, 16.0);
		card.VerticalAlignment = VerticalAlignment.Top;
		_listView = Ui.Scroll(card);

		Grid shell = Ui.G("Auto,Auto,*");
		shell.Add(head, 0);
		shell.Add(bar, 1);
		shell.Add(_host, 2);
		base.Content = shell;
		ShowSeg(0);
	}

	private void ShowSeg(int k)
	{
		_seg = k;
		for (int i = 0; i < 5; i++)
		{
			_segBtns[i].IsChecked = i == k;
		}
		_more.Show(k < 2);
		if (k == 4)
		{
			VersionManagePage p = _settings ?? (_settings = new VersionManagePage { Folder = _inst });
			_host.Content = p;
			Run(async delegate
			{
				await p.EnsureLoadedAsync();
				p.OnShown();
			});
			return;
		}
		_host.Content = _listView;
		Run(LoadSegAsync, I18n.L("读取失败"));
	}

	private async Task LoadSegAsync()
	{
		int seg = _seg;
		List<UIElement> rows = new List<UIElement>();
		if (seg == 0)
		{
			// 与 ModsDialog 默认目标一致：优先实例共享目录（Value 为空），否则第一个
			List<ModsTarget> ts = (await Api.TryCallAsync("get_mods_targets", new { instance = _inst }, new List<ModsTarget>())) ?? new List<ModsTarget>();
			string ver = ts.Any((ModsTarget t) => t.Value == "") ? "" : (ts.FirstOrDefault()?.Value ?? "");
			foreach (ModEntry m in (await Api.TryCallAsync("get_installed_mod_entries", new { instance = _inst, version = ver }, new List<ModEntry>())) ?? new List<ModEntry>())
			{
				string file = m.Filename;
				CheckBox sw = Ui.Switch(m.Enabled);
				sw.Checked += delegate
				{
					Run(() => Api.TryCallAsync<object>("enable_mod", new { instance = _inst, filename = file, version = ver }));
				};
				sw.Unchecked += delegate
				{
					Run(() => Api.TryCallAsync<object>("disable_mod", new { instance = _inst, filename = file, version = ver }));
				};
				rows.Add(Row(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name, file, sw));
			}
		}
		else if (seg == 1)
		{
			foreach (SaveRow s in (await Api.TryCallAsync("list_saves", new { instance = _inst, version = "" }, new List<SaveRow>())) ?? new List<SaveRow>())
			{
				string name = s.Name;
				rows.Add(Row(name, string.Join(" · ", new string[2] { s.Size, s.LastPlayed }.Where((string x) => !string.IsNullOrWhiteSpace(x))), Ui.Btn(I18n.L("打开"), BtnKind.Chip, delegate
				{
					Run(() => Api.CallAsync("open_save", new { instance = _inst, name, version = "" }));
				})));
			}
		}
		else
		{
			string kind = (seg == 2) ? "resourcepack" : "shader";
			foreach (string f in (await Api.TryCallAsync((seg == 2) ? "get_installed_resourcepacks" : "get_installed_shaders", new { instance = _inst }, new List<string>())) ?? new List<string>())
			{
				string file = f;
				rows.Add(Row(file, null, Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
				{
					Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除"), file, I18n.L("删除"), I18n.L("取消"), danger: true))
						{
							await Api.CallAsync("delete_" + kind, new { instance = _inst, filename = file });
							await LoadSegAsync();
						}
					}, I18n.L("删除失败"));
				})));
			}
		}
		if (seg != _seg)
		{
			return;
		}
		_rows.Children.Clear();
		if (rows.Count == 0)
		{
			_rows.Children.Add(Ui.Empty("\ue80a", I18n.L("这里还是空的"), I18n.L("到「下载」页搜索安装")).M(0.0, 18.0, 0.0, 18.0));
			return;
		}
		for (int i = 0; i < rows.Count; i++)
		{
			if (i > 0 && rows[i] is Border b)
			{
				b.BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
			}
			_rows.Children.Add(rows[i]);
		}
	}

	// 白卡内一行：高 46，行间 B.RowLine 细线，悬停 B.RowHover（同 DownloadPage）
	private static Border Row(string name, string? sub, UIElement tail)
	{
		Grid g = Ui.G(null, "*,Auto");
		g.Add(Ui.V(1.0, Ui.Txt(name, 13.5, bold: true).Trim(), string.IsNullOrEmpty(sub) ? null : Ui.Txt(sub, 11.5, bold: false, "B.InkFaint").Trim()).VCenter(), 0);
		g.Add(((FrameworkElement)tail).VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 1);
		Border row = new Border
		{
			MinHeight = 46.0,
			Padding = new Thickness(18.0, 4.0, 18.0, 4.0),
			Background = System.Windows.Media.Brushes.Transparent,
			Child = g
		};
		row.SetResourceReference(Border.BorderBrushProperty, "B.RowLine");
		row.MouseEnter += delegate
		{
			row.SetResourceReference(Border.BackgroundProperty, "B.RowHover");
		};
		row.MouseLeave += delegate
		{
			row.Background = System.Windows.Media.Brushes.Transparent;
		};
		return row;
	}
}

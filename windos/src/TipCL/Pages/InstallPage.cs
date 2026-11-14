using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 安装页（v2 install2）：由 DownloadPage 原版列表「安装」进入，作为下载页的子视图（不进导航注册）。
// 业务全部复用既有后端：list_loader_versions / create_instance / install_game / install_mod。
public sealed class InstallPage : UserControl
{
	private static readonly string[] Names = new string[5] { I18n.L("不安装"), "Fabric", "Forge", "NeoForge", "Quilt" };

	// 粗略体积估算（MB，不含首次下载的共享资源文件）
	private static readonly int[] Mb = new int[5] { 30, 1, 15, 15, 2 };

	private readonly string _ver;

	private readonly TextBox _name;

	private readonly TextBlock _nameHint = Ui.Small("");

	private readonly Border[] _cards = new Border[5];

	private readonly TextBlock[] _subs = new TextBlock[5];

	private readonly List<LoaderVer>[] _vers = new List<LoaderVer>[5];

	private readonly ComboBox _loaderVer = Ui.Combo(null, null, 220.0);

	private readonly TextBlock _verLabel = Ui.Txt("", 13.5);

	private readonly CheckBox _api = Ui.Switch();

	private readonly CheckBox _of = Ui.Switch();

	private readonly CheckBox _lite = Ui.Switch();

	private readonly TextBlock _ofWhy = Ui.Small("");

	private readonly TextBlock _ofName = Ui.Txt("OptiFine", 13.5);

#if PREMIUM
	// premium（install2 效果图）：输入框下 12px 灰字「安装到 <实例目录>」；compact 不加（省余量）
	private readonly TextBlock _pathHint = Ui.Small("");

	// Fabric API 具体版本号（Modrinth list_catalog_files），失败/离线静默留空；compact 不发请求
	private readonly TextBlock _apiVer = Ui.Small("");

	private List<InstanceInfo> _infos = new List<InstanceInfo>();
#endif

	private readonly FrameworkElement _verRow;

	private readonly FrameworkElement _apiRow;

	private readonly FrameworkElement _liteRow;

	private readonly TextBlock _sum = Ui.Txt("", 13.0, bold: true);

	private readonly TextBlock _size = Ui.Muted("");

	private readonly Button _go;

	private List<string> _instances = new List<string>();

	private int _sel;

	private bool _hasOf;

	private bool _autoName = true;

	public InstallPage(string version, string typeLabel, Action back)
	{
		_ver = version;
		_name = Ui.Input("", version);
		_name.Height = 38.0;
		_name.Padding = new Thickness(10.0, 3.0, 10.0, 3.0);
		_name.FontWeight = FontWeights.SemiBold;
		_name.TextChanged += delegate
		{
			if (_name.IsKeyboardFocusWithin)
			{
				_autoName = false;
			}
			SyncName();
		};
		Button bk = Ui.Btn("", BtnKind.Normal, delegate
		{
			back();
		}, "\ue72b");
		bk.Width = bk.Height = 32.0;
		bk.Padding = new Thickness(0.0);
		TextBlock title = Ui.H1(I18n.L("安装 Minecraft {0}", version));
		title.FontSize = 20.0;
		title.FontWeight = FontWeights.ExtraBold;
		SPanel head = Ui.H(10.0, bk, title.VCenter(), Ui.Tag(typeLabel, "B.Ok", "B.AccentSoft"));
		head.Margin = new Thickness(22.0, 14.0, 22.0, 10.0);

		Grid lds = Ui.G(null, "*,*,*,*,*");
		for (int i = 0; i < 5; i++)
		{
			int k = i;
			_subs[i] = Ui.Small(i == 0 ? I18n.L("纯原版") : "…");
			Border chk = new Border
			{
				Width = 16.0,
				Height = 16.0,
				CornerRadius = new CornerRadius(8.0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 9.0, 10.0, 0.0),
				Child = Ui.Glyph("\ue73e", 9.0, "B.OnAccent").Center(),
				Tag = "chk"
			};
			chk.SetResourceReference(Border.BackgroundProperty, "B.Accent");
			Grid g = new Grid();
			g.Children.Add(Ui.H(10.0, LoaderIcons.Tile(i == 0 ? "vanilla" : Names[i], Names[i]), Ui.V(1.0, Ui.Txt(Names[i], 14.0, bold: true), _subs[i]).VCenter()));
			g.Children.Add(chk);
			Border card = Ui.Card(g, 11.0);
			card.BorderThickness = new Thickness(1.5);
			card.Cursor = Cursors.Hand;
			card.Margin = new Thickness(i == 0 ? 0.0 : 5.0, 0.0, i == 4 ? 0.0 : 5.0, 0.0);
			card.MouseLeftButtonUp += delegate
			{
				if (card.IsEnabled)
				{
					Select(k);
				}
			};
			_cards[i] = card;
			lds.Add(card, 0, i);
		}

		_loaderVer.SelectionChanged += delegate
		{
			Sync();
		};
		_api.Checked += Changed;
		_api.Unchecked += Changed;
		_of.Checked += Changed;
		_of.Unchecked += Changed;
		_lite.Checked += Changed;
		_lite.Unchecked += Changed;
		_verRow = Row(_verLabel, null, _loaderVer);
#if PREMIUM
		// install2：Fabric API 行名称下带具体版本号（如 0.92.2+1.21.1），替代 compact 的固定提示
		_apiVer.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
		Grid apiG = Ui.G(null, "Auto,*,Auto");
		apiG.Height = 46.0;
		apiG.Margin = new Thickness(16.0, 0.0, 16.0, 0.0);
		apiG.Add(Ui.V(1.0, Ui.Txt("Fabric API", 13.5), _apiVer).VCenter(), 0);
		apiG.Add(_api.VCenter(), 0, 2);
		_apiRow = SepRow(apiG);
#else
		_apiRow = SepRow(Row(Ui.Txt("Fabric API", 13.5), I18n.L("大多数 Fabric 模组都需要"), _api));
#endif
		FrameworkElement ofGrid = Row(_ofName, null, _of);
		((Grid)ofGrid).Add(_ofWhy.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 1);
		_liteRow = SepRow(Row(Ui.Txt("LiteLoader", 13.5), I18n.L("仅老版本可用"), _lite));
		_liteRow.Visibility = Visibility.Collapsed;
		Border vbox = Ui.Card(Ui.V(0.0, _verRow, _apiRow, SepRow(ofGrid), _liteRow), 0.0);
		vbox.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		vbox.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);

		Grid nm = new Grid();
		nm.Children.Add(_name);
		nm.Children.Add(_nameHint.Right().VCenter().M(0.0, 0.0, 14.0, 0.0));
		_nameHint.IsHitTestVisible = false;
#if PREMIUM
		_pathHint.Margin = new Thickness(2.0, 6.0, 0.0, 0.0);
		_pathHint.IsHitTestVisible = false;
		SPanel body = Ui.V(0.0, Label(I18n.L("实例名称"), 8.0), nm, _pathHint, Label(I18n.L("模组加载器"), 14.0), lds, vbox);
#else
		SPanel body = Ui.V(0.0, Label(I18n.L("实例名称"), 8.0), nm, Label(I18n.L("模组加载器"), 14.0), lds, vbox);
#endif
		body.Margin = new Thickness(22.0, 0.0, 22.0, 16.0);

		_go = Ui.Btn(I18n.L("开始安装"), BtnKind.Primary, delegate
		{
			PageBase.Run(InstallAsync, I18n.L("安装失败"));
		}, "\ue896");
		_go.Height = 38.0;
		_go.Padding = new Thickness(20.0, 0.0, 20.0, 0.0);
		_size.ToolTip = I18n.L("估算值，不含首次下载的共享资源文件");
		Grid foot = Ui.G(null, "Auto,*,Auto");
		foot.Add(Ui.H(6.0, Ui.Muted(I18n.L("将安装")), _sum, _size).VCenter(), 0);
		foot.Add(_go, 0, 2);
		Border footBar = new Border
		{
			Child = foot,
			Padding = new Thickness(22.0, 12.0, 22.0, 12.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0)
		};
		footBar.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		footBar.SetResourceReference(Border.BorderBrushProperty, "B.RowLine");

		Grid root = Ui.G("Auto,*,Auto");
		root.Add(head, 0);
		root.Add(Ui.Scroll(body), 1);
		root.Add(footBar, 2);
		Content = root;
		Select(0);
		PageBase.Run(LoadAsync);
	}

	private static TextBlock Label(string text, double top)
	{
		TextBlock t = Ui.Txt(text, 12.5, bold: true, "B.InkMuted");
		t.Margin = new Thickness(2.0, top, 0.0, 7.0);
		return t;
	}

	private static FrameworkElement Row(TextBlock name, string? hint, Control right)
	{
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Height = 42.0;
		g.Margin = new Thickness(16.0, 0.0, 16.0, 0.0);
		g.Add(name.VCenter(), 0);
		if (hint != null)
		{
			g.Add(Ui.Small(hint).VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 1);
		}
		g.Add(right.VCenter(), 0, 2);
		return g;
	}

	// 行上方 1px 分隔线（install2 .vr，随行一起折叠）
	private static FrameworkElement SepRow(FrameworkElement row)
	{
		Border b = new Border
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = row
		};
		b.SetResourceReference(Border.BorderBrushProperty, "B.RowLine");
		return b;
	}

	private void Changed(object s, RoutedEventArgs e)
	{
		Sync();
	}

	private async Task LoadAsync()
	{
		List<InstanceInfo> infos = (await AppServices.Client.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
#if PREMIUM
		_infos = infos;
#endif
		_instances = infos.Select((InstanceInfo i) => i.Name).ToList();
		SyncName();
		string[] keys = new string[6] { "fabric", "forge", "neoforge", "quilt", "optifine", "liteloader" };
		List<LoaderVer>[] res = await Task.WhenAll(keys.Select((string k) => AppServices.Client.TryCallAsync("list_loader_versions", new
		{
			mc_version = _ver,
			loader = k
		}, new List<LoaderVer>())));
		for (int i = 1; i < 5; i++)
		{
			List<LoaderVer> l = res[i - 1] ?? new List<LoaderVer>();
			_vers[i] = l;
			_subs[i].Text = (l.Count == 0) ? I18n.L("不支持此版本") : Best(l).Id;
			_cards[i].IsEnabled = l.Count > 0;
			_cards[i].Opacity = (l.Count > 0) ? 1.0 : 0.45;
		}
		_hasOf = (res[4]?.Count ?? 0) > 0;
		_liteRow.Visibility = ((res[5]?.Count ?? 0) > 0) ? Visibility.Visible : Visibility.Collapsed;
		Select(_sel);
#if PREMIUM
		_ = LoadFabricApiVersionAsync();
#endif
	}

#if PREMIUM
	// Fabric API 具体版本号：走既有 Modrinth 通道（list_catalog_files，slug 定位），
	// 取该 MC 版本下最新 release 的 version_number；任何失败静默留空（离线不显示）
	private async Task LoadFabricApiVersionAsync()
	{
		string ver = _ver;
		List<CatalogFile> files = (await AppServices.Client.TryCallAsync("list_catalog_files", new
		{
			extra = new Dictionary<string, object>
			{
				["name"] = "Fabric API",
				["source"] = "Modrinth",
				["slug"] = "fabric-api",
				["game_version"] = ver
			}
		}, new List<CatalogFile>())) ?? new List<CatalogFile>();
		CatalogFile f = files.FirstOrDefault((CatalogFile x) => x.ReleaseType == "release") ?? files.FirstOrDefault();
		_apiVer.Text = f?.VersionNumber ?? "";
	}
#endif

	private static LoaderVer Best(List<LoaderVer> l)
	{
		return l.FirstOrDefault((LoaderVer v) => v.Stable) ?? l[0];
	}

	private void Select(int k)
	{
		_sel = k;
		for (int i = 0; i < 5; i++)
		{
			bool on = i == k;
			_cards[i].SetResourceReference(Border.BorderBrushProperty, on ? "B.Accent" : "B.CardBorder");
			_cards[i].SetResourceReference(Border.BackgroundProperty, on ? "B.AccentTint" : "B.Card");
			foreach (UIElement c in ((Grid)_cards[i].Child).Children)
			{
				if (c is Border b && (b.Tag as string) == "chk")
				{
					b.Show(on);
				}
			}
		}
		List<LoaderVer> l = _vers[k];
		_verRow.Visibility = (k == 0) ? Visibility.Collapsed : Visibility.Visible;
		_verLabel.Text = I18n.L("{0} 版本", Names[k]);
		if (k > 0)
		{
			List<string> items = new List<string>();
			if (l == null || l.Count == 0)
			{
				items.Add(I18n.L("最新"));
			}
			else
			{
				LoaderVer best = Best(l);
				items.AddRange(l.Select((LoaderVer v) => (v == best) ? (v.Id + " · " + I18n.L("推荐")) : v.Id));
			}
			_loaderVer.Fill(items, items[0]);
			if (l != null && l.Count > 0)
			{
				_loaderVer.SelectedIndex = l.IndexOf(Best(l));
			}
		}
		bool fab = k == 1 || k == 4;
		_apiRow.Visibility = fab ? Visibility.Visible : Visibility.Collapsed;
		_api.IsChecked = fab;
		bool ofOk = _hasOf && (k == 0 || k == 2);
		_of.IsEnabled = ofOk;
		if (!ofOk)
		{
			_of.IsChecked = false;
		}
		_ofName.Opacity = ofOk ? 1.0 : 0.5;
		_ofWhy.Text = ofOk ? "" : (!_hasOf ? I18n.L("此版本没有可用的 OptiFine") : I18n.L("与 {0} 不兼容，可改装 Sodium + Iris", Names[k]));
		if (_autoName)
		{
			_name.Text = (k == 0) ? _ver : (Names[k] + " " + _ver);
		}
		Sync();
	}

	private string LoaderVersion()
	{
		List<LoaderVer> l = _vers[_sel];
		int i = _loaderVer.SelectedIndex;
		if (_sel == 0 || l == null || i < 0 || i >= l.Count)
		{
			return "";
		}
		return l[i].Id;
	}

	private void Sync()
	{
		List<string> parts = new List<string> { _ver };
		int mb = Mb[0];
		if (_sel > 0)
		{
			string v = LoaderVersion();
			parts.Add(Names[_sel] + (v.Length > 0 ? (" " + v) : ""));
			mb += Mb[_sel];
		}
		if (_sel > 0 && (_sel == 1 || _sel == 4) && _api.IsChecked == true)
		{
			parts.Add("Fabric API");
			mb += 2;
		}
		if (_of.IsChecked == true)
		{
			parts.Add("OptiFine");
			mb += 7;
		}
		if (_lite.IsChecked == true && _liteRow.Visibility == Visibility.Visible)
		{
			parts.Add("LiteLoader");
			mb += 1;
		}
		_sum.Text = string.Join(" + ", parts);
		_size.Text = I18n.L("· 约 {0} MB", mb);
	}

	private void SyncName()
	{
		string n = _name.Text.Trim();
		_nameHint.Text = (n.Length == 0) ? "" : (_instances.Contains(n) ? I18n.L("装入已有实例") : I18n.L("将新建实例"));
#if PREMIUM
		SyncPath(n);
#endif
	}

#if PREMIUM
	// install2：输入框下「安装到 <路径>」——已有实例用其目录，新实例用实例根目录推算；拿不到就藏
	private void SyncPath(string n)
	{
		string? p = _infos.FirstOrDefault((InstanceInfo i) => i.Name == n)?.Path;
		if (string.IsNullOrEmpty(p) && n.Length > 0)
		{
			string? root = Path.GetDirectoryName(_infos.FirstOrDefault()?.Path ?? "");
			if (!string.IsNullOrEmpty(root))
			{
				p = Path.Combine(root, n);
			}
		}
		_pathHint.Text = (p == null) ? "" : I18n.L("安装到 {0}", p);
		_pathHint.Visibility = (p == null) ? Visibility.Collapsed : Visibility.Visible;
	}
#endif

	private async Task InstallAsync()
	{
		string name = _name.Text.Trim();
		if (name.Length == 0)
		{
			AppServices.Window?.Toast(I18n.L("请填写实例名称"), "", ToastKind.Warning);
			return;
		}
		_go.IsEnabled = false;
		try
		{
			BridgeClient api = AppServices.Client;
			if (!_instances.Contains(name))
			{
				await api.CallAsync("create_instance", new { name });
			}
			Dictionary<string, object> extra = new Dictionary<string, object>
			{
				["optifine"] = _of.IsChecked == true,
				["liteloader"] = _lite.IsChecked == true && _liteRow.Visibility == Visibility.Visible
			};
			string tid = await api.StartTaskAsync("install_game", new
			{
				version = _ver,
				loader = (_sel == 0) ? "" : Names[_sel],
				loader_version = LoaderVersion(),
				instance = name,
				extra = extra
			});
			if ((_sel == 1 || _sel == 4) && _api.IsChecked == true)
			{
				string mc = _ver;
				Action<TaskRow> h = null;
				h = delegate(TaskRow r)
				{
					if (r.Id != tid || !r.Finished)
					{
						return;
					}
					TaskStore.Updated -= h;
					if (r.Success)
					{
						Dispatcher.BeginInvoke((Action)delegate
						{
							PageBase.Run(() => api.StartTaskAsync("install_mod", new
							{
								name = "Fabric API",
								instance = name,
								extra = new
								{
									source = "Modrinth",
									slug = "fabric-api",
									mc_version = mc
								}
							}), I18n.L("Fabric API 安装失败"));
						});
					}
				};
				TaskStore.Updated += h;
			}
			AppServices.Window?.FlyToTasks(_go, _ver);
			AppServices.Window?.Toast(I18n.L("已加入队列"), _sum.Text, ToastKind.Success);
			AppServices.Window?.Navigate("tasks");
		}
		finally
		{
			_go.IsEnabled = true;
		}
	}
}

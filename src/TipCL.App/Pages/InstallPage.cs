using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C6a:Windows 版 <c>Pages\InstallPage.cs</c>(14927 字节 / 498 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **安装页**(下载页原版列表「安装」进来的子视图,**不进导航注册**,与 Windows 一致)。
///
/// 它**不是** <see cref="PageBase"/>:Windows 侧也是裸 <c>UserControl</c>(被 <see cref="DownloadPage"/> 换进
/// 页内容里),所以本移植逐字保留这个形状 —— 它因此没有 <c>Title</c>/<c>OnShown</c> 那一套,只有构造与自己的状态机。
///
/// 形状:标题行(返回 + 「安装 Minecraft x.y」+ 类型标签)+ 实例名称输入 + 五张加载器卡(纯原版 / Fabric / Forge /
/// NeoForge / Quilt)+ 加载器版本行 + Fabric API 行 + OptiFine 行 + LiteLoader 行(按可用性显示)+ 底部「将安装 … · 约 N MB」
/// 与「开始安装」。
///
/// 行为(与 Windows 一致):<c>get_instances</c> + 六个 <c>list_loader_versions</c>(fabric/forge/neoforge/quilt/optifine/liteloader)
/// 起表;选卡改名称与体积估算;「开始安装」= 需要时 <c>create_instance</c> → <c>install_game</c> → (Fabric/Quilt 且勾了 API 时)
/// 等任务完成再 <c>install_mod</c>(Fabric API)→ 飞向任务页 → 跳到 tasks。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Style = Ui.S("键")</c> → <c>StyleKeys.Apply(...)</c>;<c>SetResourceReference</c> → <c>.Dyn(...)</c>;
///   <c>Cursor = Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>;</item>
/// <item><c>MouseLeftButtonUp</c> → <c>PointerReleased</c>;三个开关的 <c>Checked</c>/<c>Unchecked</c> → <c>IsCheckedChanged</c>
///   (Avalonia 11.3 把前两个标成过时 CS0618,与 C4/C5a 同一口径);</item>
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>FontWeights.*</c> → <c>FontWeight.*</c>;</item>
/// <item><c>AppServices.Window?.Toast(...)</c> → <c>AppServices.Toast(...)</c>(mac 的 <c>AppServices.Window</c> 静态类型是
///   Avalonia 的 <c>Window</c>,没有 <c>Toast</c> 成员;Toast 走 <c>AppServices.ToastSink</c>,落到同一层);
///   <c>AppServices.Window?.FlyToTasks(...)</c> / <c>?.Navigate(...)</c> → <c>(AppServices.Window as MainWindow)?.…</c>
///   (D-131 记的就是这一种形状:mac 的 <c>MainWindow</c> 是 internal,要多一次 cast);</item>
/// <item><c>Dispatcher.BeginInvoke((Action)...)</c> → <c>Dispatcher.UIThread.Post(...)</c>;</item>
/// <item><b><c>#if PREMIUM</c> 两支不搬</b>:Windows 的默认构建(compact)里这些成员**不存在**(<c>_pathHint</c> / <c>_apiVer</c> /
///   <c>_infos</c> / <c>LoadFabricApiVersionAsync</c> / <c>SyncPath</c>),照 mac 既有的"与 Windows 默认构建一致"口径逐字保留
///   <c>#else</c> 那一支(行内保留了注释,说明 premium 支为什么不在这里)。</item>
/// </list>
/// </summary>
public sealed class InstallPage : UserControl
{
	/// <summary>【Windows 源】<c>:18</c> 五张加载器卡的名字(第 0 张 = 纯原版)。</summary>
	private static readonly string[] Names = new string[5] { I18n.L("不安装"), "Fabric", "Forge", "NeoForge", "Quilt" };

	/// <summary>【Windows 源】<c>:21</c> 粗略体积估算(MB,不含首次下载的共享资源文件)。</summary>
	private static readonly int[] Mb = new int[5] { 30, 1, 15, 15, 2 };

	private readonly string _ver;

	private readonly TextBox _name;

	private readonly TextBlock _nameHint = Ui.Small("");

	private readonly Border[] _cards = new Border[5];

	private readonly TextBlock[] _subs = new TextBlock[5];

	private readonly List<LoaderVer>?[] _vers = new List<LoaderVer>?[5];

	private readonly ComboBox _loaderVer = Ui.Combo(null, null, 220.0);

	private readonly TextBlock _verLabel = Ui.Txt("", 13.5);

	private readonly CheckBox _api = Ui.Switch();

	private readonly CheckBox _of = Ui.Switch();

	private readonly CheckBox _lite = Ui.Switch();

	private readonly TextBlock _ofWhy = Ui.Small("");

	private readonly TextBlock _ofName = Ui.Txt("OptiFine", 13.5);

	private readonly Control _verRow;

	private readonly Control _apiRow;

	private readonly Control _liteRow;

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
		_name.FontWeight = FontWeight.SemiBold;
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
		title.FontWeight = FontWeight.ExtraBold;
		SPanel head = Ui.H(10.0, bk, title.VCenter(), Ui.Tag(typeLabel, "B.Ok", "B.AccentSoft"));
		head.Margin = new Thickness(22.0, 14.0, 22.0, 10.0);

		Grid lds = Ui.G(null, "*,*,*,*,*");
		for (int i = 0; i < 5; i++)
		{
			int k = i;
			_subs[i] = Ui.Small(i == 0 ? I18n.L("纯原版") : "...");
			Border chk = new Border
			{
				Width = 16.0,
				Height = 16.0,
				CornerRadius = new CornerRadius(8.0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 9.0, 10.0, 0.0),
				Child = Ui.Glyph("\ue73e", 9.0, "B.OnAccent").Center(),
				Tag = "chk",
			};
			chk.Dyn(Border.BackgroundProperty, "B.Accent");
			Grid g = new Grid();
			g.Children.Add(Ui.H(10.0, LoaderIcons.Tile(i == 0 ? "vanilla" : Names[i], Names[i]), Ui.V(1.0, Ui.Txt(Names[i], 14.0, bold: true), _subs[i]).VCenter()));
			g.Children.Add(chk);
			Border card = Ui.Card(g, 11.0);
			card.BorderThickness = new Thickness(1.5);
			card.Cursor = new Cursor(StandardCursorType.Hand);
			card.Margin = new Thickness(i == 0 ? 0.0 : 5.0, 0.0, i == 4 ? 0.0 : 5.0, 0.0);
			card.PointerReleased += delegate
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
		// 【Avalonia 差异】Windows 分别接 Checked / Unchecked,这里统一 IsCheckedChanged(CS0618)。
		_api.IsCheckedChanged += delegate
		{
			Sync();
		};
		_of.IsCheckedChanged += delegate
		{
			Sync();
		};
		_lite.IsCheckedChanged += delegate
		{
			Sync();
		};
		_verRow = Row(_verLabel, null, _loaderVer);
		// Windows 默认构建(compact)走这一支:固定提示「大多数 Fabric 模组都需要」。
		// (premium 支换成「Fabric API + 具体版本号」两行,并多一次 list_catalog_files —— 默认构建里不存在,不搬。)
		_apiRow = SepRow(Row(Ui.Txt("Fabric API", 13.5), I18n.L("大多数 Fabric 模组都需要"), _api));
		Control ofGrid = Row(_ofName, null, _of);
		((Grid)ofGrid).Add(_ofWhy.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 1);
		_liteRow = SepRow(Row(Ui.Txt("LiteLoader", 13.5), I18n.L("仅老版本可用"), _lite));
		_liteRow.IsVisible = false;
		Border vbox = Ui.Card(Ui.V(0.0, _verRow, _apiRow, SepRow(ofGrid), _liteRow), 0.0);
		vbox.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		vbox.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);

		Grid nm = new Grid();
		nm.Children.Add(_name);
		nm.Children.Add(_nameHint.Right().VCenter().M(0.0, 0.0, 14.0, 0.0));
		_nameHint.IsHitTestVisible = false;
		SPanel body = Ui.V(0.0, Label(I18n.L("实例名称"), 8.0), nm, Label(I18n.L("模组加载器"), 14.0), lds, vbox);
		body.Margin = new Thickness(22.0, 0.0, 22.0, 16.0);

		_go = Ui.Btn(I18n.L("开始安装"), BtnKind.Primary, delegate
		{
			PageBase.Run(InstallAsync, I18n.L("安装失败"));
		}, "\ue896");
		_go.Height = 38.0;
		_go.Padding = new Thickness(20.0, 0.0, 20.0, 0.0);
		ToolTip.SetTip(_size, I18n.L("估算值，不含首次下载的共享资源文件"));
		Grid foot = Ui.G(null, "Auto,*,Auto");
		foot.Add(Ui.H(6.0, Ui.Muted(I18n.L("将安装")), _sum, _size).VCenter(), 0);
		foot.Add(_go, 0, 2);
		Border footBar = new Border
		{
			Child = foot,
			Padding = new Thickness(22.0, 12.0, 22.0, 12.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
		};
		footBar.Dyn(Border.BackgroundProperty, "B.GlassStrong");
		footBar.Dyn(Border.BorderBrushProperty, "B.RowLine");

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

	private static Control Row(TextBlock name, string? hint, Control right)
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

	/// <summary>行上方 1px 分隔线(install2 的 <c>.vr</c>,随行一起折叠)。</summary>
	private static Control SepRow(Control row)
	{
		Border b = new Border
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = row,
		};
		b.Dyn(Border.BorderBrushProperty, "B.RowLine");
		return b;
	}

	/// <summary>【Windows 源】<c>:251-279</c>:实例列表 + 六个加载器版本(optifine/liteloader 决定两行的可用性)。</summary>
	internal async Task LoadAsync()
	{
		List<InstanceInfo> infos = (await AppServices.Client.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_instances = infos.Select((InstanceInfo i) => i.Name).ToList();
		SyncName();
		string[] keys = new string[6] { "fabric", "forge", "neoforge", "quilt", "optifine", "liteloader" };
		List<LoaderVer>[] res = await Task.WhenAll(keys.Select((string k) => AppServices.Client.TryCallAsync("list_loader_versions", new
		{
			mc_version = _ver,
			loader = k,
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
		_liteRow.IsVisible = (res[5]?.Count ?? 0) > 0;
		Select(_sel);
	}

	private static LoaderVer Best(List<LoaderVer> l)
	{
		return l.FirstOrDefault((LoaderVer v) => v.Stable) ?? l[0];
	}

	/// <summary>【Windows 源】<c>:307-360</c>:选卡(卡边框/底色、勾选片、加载器版本行、API/OptiFine 行、名称与体积)。</summary>
	internal void Select(int k)
	{
		_sel = k;
		for (int i = 0; i < 5; i++)
		{
			bool on = i == k;
			_cards[i].Dyn(Border.BorderBrushProperty, on ? "B.Accent" : "B.CardBorder");
			_cards[i].Dyn(Border.BackgroundProperty, on ? "B.AccentTint" : "B.Card");
			foreach (Control c in ((Grid)_cards[i].Child!).Children)
			{
				if (c is Border b && (b.Tag as string) == "chk")
				{
					b.Show(on);
				}
			}
		}
		List<LoaderVer>? l = _vers[k];
		_verRow.IsVisible = k != 0;
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
		_apiRow.IsVisible = fab;
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
		List<LoaderVer>? l = _vers[_sel];
		int i = _loaderVer.SelectedIndex;
		if (_sel == 0 || l == null || i < 0 || i >= l.Count)
		{
			return "";
		}
		return l[i].Id;
	}

	/// <summary>【Windows 源】<c>:373-400</c>:底部「将安装 …」摘要与体积估算。</summary>
	private void Sync()
	{
		List<string> parts = new List<string> { _ver };
		int mb = Mb[0];
		if (_sel > 0)
		{
			string v = LoaderVersion();
			parts.Add(Names[_sel] + ((v.Length > 0) ? (" " + v) : ""));
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
		if (_lite.IsChecked == true && _liteRow.IsVisible)
		{
			parts.Add("LiteLoader");
			mb += 1;
		}
		_sum.Text = string.Join(" + ", parts);
		_size.Text = I18n.L("· 约 {0} MB", mb);
	}

	private void SyncName()
	{
		string n = _name.Text?.Trim() ?? "";
		_nameHint.Text = (n.Length == 0) ? "" : (_instances.Contains(n) ? I18n.L("装入已有实例") : I18n.L("将新建实例"));
	}

	/// <summary>
	/// 【Windows 源】<c>:429-497</c>:开始安装 —— 需要时建实例 → <c>install_game</c> →
	/// (Fabric/Quilt 且勾了 API)等这个任务完成再 <c>install_mod</c>(Fabric API)→ 飞任务页 → 跳 tasks。
	/// </summary>
	internal async Task InstallAsync()
	{
		string name = _name.Text?.Trim() ?? "";
		if (name.Length == 0)
		{
			// Windows 是 AppServices.Window?.Toast(...);mac 的 Window 没有 Toast 成员 ⇒ 走 AppServices.Toast(同一层)。
			AppServices.Toast(I18n.L("请填写实例名称"), "", ToastKind.Warning);
			return;
		}
		_go.IsEnabled = false;
		try
		{
			BridgeClient api = AppServices.Client;
			if (!_instances.Contains(name))
			{
				await api.CallAsync("create_instance", new
				{
					name,
				});
			}
			Dictionary<string, object> extra = new Dictionary<string, object>
			{
				["optifine"] = _of.IsChecked == true,
				["liteloader"] = _lite.IsChecked == true && _liteRow.IsVisible,
			};
			string tid = await api.StartTaskAsync("install_game", new
			{
				version = _ver,
				loader = (_sel == 0) ? "" : Names[_sel],
				loader_version = LoaderVersion(),
				instance = name,
				extra,
			});
			if ((_sel == 1 || _sel == 4) && _api.IsChecked == true)
			{
				string mc = _ver;
				Action<TaskRow>? h = null;
				h = delegate(TaskRow r)
				{
					if (r.Id != tid || !r.Finished)
					{
						return;
					}
					TaskStore.Updated -= h;
					if (r.Success)
					{
						Dispatcher.UIThread.Post(delegate
						{
							PageBase.Run(() => api.StartTaskAsync("install_mod", new
							{
								name = "Fabric API",
								instance = name,
								extra = new
								{
									source = "Modrinth",
									slug = "fabric-api",
									mc_version = mc,
								},
							}), I18n.L("Fabric API 安装失败"));
						});
					}
				};
				TaskStore.Updated += h;
			}
			// 【mac 差异】Windows 是 AppServices.Window?.FlyToTasks / ?.Navigate —— mac 的 MainWindow 是 internal,
			// 按 D-131 的口径多一次 cast。
			(AppServices.Window as MainWindow)?.FlyToTasks(_go, _ver);
			AppServices.Toast(I18n.L("已加入队列"), _sum.Text, ToastKind.Success);
			(AppServices.Window as MainWindow)?.Navigate("tasks");
		}
		finally
		{
			_go.IsEnabled = true;
		}
	}
}

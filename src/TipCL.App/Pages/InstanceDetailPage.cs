using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C4:Windows 版 <c>Pages\InstanceDetailPage.cs</c>(10450 字节 / 306 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **实例详情页**。
///
/// <b>它不是导航页</b>:Windows 的导航表(<c>MainWindow.NavDefs</c> / <c>NavModel.PageForNavKey</c>)里
/// **没有**它的 id,它是被 <see cref="LibraryPage"/> 的「实例详情」按钮**换进页内容**里的
/// (<c>base.Content = new InstanceDetailPage(info, 返回回调)</c>),自己的「返回」再把原内容换回去。
/// mac 侧照 Windows 一样处理 —— **不注册页面**、不给 id。
///
/// 形状:头部(返回钮 + 标题 = 实例名 + 「Minecraft x.y · 整合包」副行 + 「启动」)+ 一条工具栏
/// (五个分段 <c>ToggleButton</c>:模组/存档/资源包/光影/设置 + 灰色说明 + 版本下拉 + 「更多管理」)
/// + 一张白卡行表(每行最小高 46、内边距 18/4、行间 <c>B.RowLine</c> 细线、悬停 <c>B.RowHover</c>)。
///
/// 行为(与 Windows 一致):<c>get_version_rows</c> 填版本下拉;选中版本若 <c>isolated == false</c>(共享/大锅饭)
/// 就藏掉前四个分段、只留「设置」并显示灰字说明;<c>seg == 0/1</c> 时右侧「更多管理」打开
/// <see cref="ModsDialog"/> / <see cref="SavesDialog"/>(不在本页重做完整功能)。
///
/// <b>一处用户可见的降级(请 Lead 裁决,见 D-134)</b>:Windows 的「设置」分段
/// 内嵌 <c>VersionManagePage</c>(34490 字节的整页,属别的工作包,mac 侧**不存在**)。
/// 本包按红线不搬它,做法照 D-119 的先例 —— 分段按钮**照 Windows 建**、点击走新增的注入缝
/// <see cref="SettingsPageFactory"/>;**挂点没接时**那一栏显示一句如实的占位说明
/// (「版本管理页还没移植,设置分段暂不可用」),不是空白也不是假装能用。
/// 将来的版本管理包只要把工厂接上,这里一行都不用改。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;<c>Style = Ui.S("键")</c> →
///   <c>StyleKeys.Apply(控件, "键")</c>(<c>T.PageTitle</c> / <c>Seg.Item</c> / <c>Seg.Rail</c> 三个键都落得到);</item>
/// <item><c>System.Windows.Media.Brushes.Transparent</c> → <c>Avalonia.Media.Brushes.Transparent</c>;</item>
/// <item><c>row.SetResourceReference(Border.BorderBrushProperty, "B.RowLine")</c> → <c>row.Dyn(...)</c>;</item>
/// <item><c>MouseEnter</c> / <c>MouseLeave</c> → <c>PointerEntered</c> / <c>PointerExited</c>;</item>
/// <item><c>_settings</c> 的静态类型由 <c>VersionManagePage</c> 改成 <c>PageBase</c>(那个类型在 mac 侧不存在,
///   见上面的降级说明)—— <c>EnsureLoadedAsync</c> / <c>OnShown</c> 都是 <see cref="PageBase"/> 的成员,
///   所以调用形状一字未改。</item>
/// </list>
/// </summary>
public sealed class InstanceDetailPage : PageBase
{
	/// <summary>五个分段【Windows 源】<c>InstanceDetailPage.cs:17</c>(<c>static readonly</c>,文案在类型初始化时定死 —— 逐字保留)。</summary>
	private static readonly string[] Segs = new string[5] { I18n.L("模组"), I18n.L("存档"), I18n.L("资源包"), I18n.L("光影"), I18n.L("设置") };

	private readonly string _inst;

	private readonly ToggleButton[] _segBtns = new ToggleButton[5];

	private readonly ContentControl _host = new ContentControl();

	private readonly SPanel _rows = Ui.V(0.0);

	private readonly Button _more;

	private readonly SmoothScroll _listView;

	/// <summary>「设置」分段内嵌的那个页面(Windows 是 <c>VersionManagePage?</c>,mac 见 <see cref="SettingsPageFactory"/>)。</summary>
	private PageBase? _settings;

	private int _seg;

	// 版本选择器:共享(大锅饭)模式下隐藏自有资源分段(2026-11-07)
	private readonly ComboBox _verBox = new ComboBox();

	private readonly List<VersionRowDto> _vers = new List<VersionRowDto>();

	private readonly TextBlock _sharedNote;

	private readonly TextBlock _isolatedNote;

	/// <summary>
	/// C4 注入缝(见 D-134):「设置」分段要嵌的那个页面(Windows 是 <c>new VersionManagePage { Folder = _inst }</c>)。
	/// 参数 = 实例名。**挂点没接时**这一栏显示如实的占位说明 —— 绝不假装能用,也不静默空白。
	/// </summary>
	internal static Func<string, PageBase>? SettingsPageFactory { get; set; }

	public override string Title => _inst;

	/// <summary>
	/// 建出实例详情页。【Windows 源】<c>InstanceDetailPage.cs:46-136</c>(参数 <c>(InstanceInfo info, Action back)</c>)。
	/// </summary>
	/// <param name="info">实例(页头副行与标题用它;<c>Name</c> 是后续所有后端调用的 <c>instance</c> 参数)。</param>
	/// <param name="back">「返回」回调(Windows 由调用方把自己原来的内容换回来)。</param>
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
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		// Windows 用 Ui.S("T.PageTitle") 这条命名样式;Avalonia 侧是类名映射(C1b/C1c)
		StyleKeys.Apply(title, "T.PageTitle");
		string meta = string.Join(" · ", new string[2] { string.IsNullOrEmpty(info.Mc) ? "" : ("Minecraft " + info.Mc), info.Pack }.Where((string s) => !string.IsNullOrEmpty(s)));
		Button launch = Ui.Btn(I18n.L("启动"), BtnKind.Primary, delegate
		{
			// 【Windows 源】:拿启动页现成的那条启动路径(它有预检 / 多开确认 / 进度那一整套)
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

		_sharedNote = Ui.Txt(I18n.L("该版本为共享（大锅饭）模式，模组/存档使用实例共享目录；开启独立隔离请见版本管理页"), 12.5, bold: false, "B.InkMuted");
		_sharedNote.TextWrapping = TextWrapping.Wrap;
		_sharedNote.VerticalAlignment = VerticalAlignment.Center;
		_sharedNote.MaxWidth = 420.0;
		_sharedNote.Show(shown: false);
		_isolatedNote = Ui.Txt(I18n.L("该版本已开启隔离:模组/日志/资源包位于 versions\\<版本> 目录下,与实例根目录分开"), 12.5, bold: false, "B.InkMuted");
		_isolatedNote.TextWrapping = TextWrapping.Wrap;
		_isolatedNote.VerticalAlignment = VerticalAlignment.Center;
		_isolatedNote.MaxWidth = 420.0;
		_isolatedNote.Show(shown: false);
		_verBox.MinWidth = 200.0;
		_verBox.SelectionChanged += delegate
		{
			ApplyVersionSegs();
		};

		SPanel rail = Ui.H(0.0);
		for (int i = 0; i < 5; i++)
		{
			int k = i;
			ToggleButton t = new ToggleButton
			{
				Content = Segs[i]
			};
			StyleKeys.Apply(t, "Seg.Item");
			t.Click += delegate
			{
				ShowSeg(k);
			};
			_segBtns[i] = t;
			rail.Children.Add(t);
		}
		// 右侧:打开既有完整管理对话框(导入/检查更新/备份等不在本页重做)
		_more = Ui.Btn(I18n.L("更多管理"), BtnKind.Chip, delegate
		{
			Run(() => (_seg == 0) ? ModsDialog.ShowAsync(_inst) : SavesDialog.ShowAsync(_inst, ""));
		});
		Grid bar = Ui.G(null, "Auto,*,Auto,Auto");
		bar.Margin = new Thickness(22.0, 0.0, 22.0, 12.0);
		Border railBox = new Border
		{
			Child = rail,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		StyleKeys.Apply(railBox, "Seg.Rail");
		bar.Add(railBox, 0);
		bar.Add(_sharedNote, 0, 1);
		bar.Add(_isolatedNote, 0, 1);
		bar.Add(_verBox.VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 2);
		bar.Add(_more.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);

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
		Run(LoadVersionsAsync);
	}

	// get_version_rows 返回当前(默认)实例的版本;详情页由实例库「管理」进入,目标实例即默认实例
	private async Task LoadVersionsAsync()
	{
		_vers.Clear();
		foreach (VersionRowDto r in (await Api.TryCallAsync("get_version_rows", new { include_hidden = false }, new List<VersionRowDto>())) ?? new List<VersionRowDto>())
		{
			_vers.Add(r);
		}
		if (_vers.Count == 0)
		{
			_verBox.Show(shown: false);
			ApplyVersionSegs();
			return;
		}
		string keep = _verBox.SelectedItem as string;
		_verBox.Items.Clear();
		foreach (VersionRowDto r in _vers)
		{
			// 【Windows 源】:有隔离标签就显示「id · 标签」
			_verBox.Items.Add(string.IsNullOrEmpty(r.IsolationLabel) ? r.Id : (r.Id + " · " + r.IsolationLabel));
		}
		int idx = _vers.FindIndex((VersionRowDto r) => r.Id == keep);
		_verBox.SelectedIndex = (idx >= 0) ? idx : 0;
	}

	// 选中版本 isolated=false(共享/大锅饭):隐藏模组/存档/资源包/光影分段,只留设置并显示灰字说明
	internal void ApplyVersionSegs()
	{
		VersionRowDto? cur = ((_verBox.SelectedIndex >= 0) && (_verBox.SelectedIndex < _vers.Count)) ? _vers[_verBox.SelectedIndex] : null;
		bool shared = cur != null && !cur.Isolated;
		for (int i = 0; i < 4; i++)
		{
			_segBtns[i].Show(!shared);
		}
		_sharedNote.Show(shared);
		_isolatedNote.Show(cur != null && cur.Isolated);
		// 共享模式下一旦还停在前四个分段里,强制跳到「设置」
		if (shared && _seg < 4)
		{
			ShowSeg(4);
		}
	}

	/// <summary>切到第 <paramref name="k"/> 个分段(0..4);<c>k == 4</c> 时嵌「设置」页,否则列出资源行。</summary>
	internal void ShowSeg(int k)
	{
		_seg = k;
		for (int i = 0; i < 5; i++)
		{
			_segBtns[i].IsChecked = i == k;
		}
		_more.Show(k < 2);
		if (k == 4)
		{
			PageBase? page = _settings ??= SettingsPageFactory?.Invoke(_inst);
			if (page == null)
			{
				// 【mac 侧降级,见 D-134】挂点没接:如实说明,不假装能用
				_host.Content = Ui.Empty("\ue80a", I18n.L("版本管理页还没移植"), I18n.L("这一栏内嵌的是「版本管理」页,它属于后续工作包;已注入 SettingsPageFactory 时会正常显示"));
				return;
			}
			_host.Content = page;
			Run(async delegate
			{
				await page.EnsureLoadedAsync();
				page.OnShown();
			});
			return;
		}
		_host.Content = _listView;
		Run(LoadSegAsync, I18n.L("读取失败"));
	}

	private async Task LoadSegAsync()
	{
		int seg = _seg;
		List<Control> rows = new List<Control>();
		if (seg == 0)
		{
			// 与 ModsDialog 默认目标一致:优先实例共享目录(Value 为空),否则第一个
			List<ModsTarget> ts = (await Api.TryCallAsync("get_mods_targets", new { instance = _inst }, new List<ModsTarget>())) ?? new List<ModsTarget>();
			string ver = ts.Any((ModsTarget t) => t.Value == "") ? "" : (ts.FirstOrDefault()?.Value ?? "");
			foreach (ModEntry m in (await Api.TryCallAsync("get_installed_mod_entries", new { instance = _inst, version = ver }, new List<ModEntry>())) ?? new List<ModEntry>())
			{
				string file = m.Filename;
				CheckBox sw = Ui.Switch(m.Enabled);
				// 【Avalonia 差异】Windows 分别接 Checked / Unchecked 两个事件;Avalonia 11.3 把这两个标记成
				// 过时(CS0618),等价物是 IsCheckedChanged + 判 IsChecked(与 Ui.Check/Switch 门面同一口径)。
				// 初值先设、订阅在后,所以不会多触发一次。
				sw.IsCheckedChanged += delegate
				{
					bool on = sw.IsChecked == true;
					Run(() => Api.TryCallAsync<object>(on ? "enable_mod" : "disable_mod", new { instance = _inst, filename = file, version = ver }));
				};
				// 【Windows 源】:premium 支在副行多显示一个版本号;mac 的编译常量里没有 PREMIUM
				// (与 Windows 默认构建一致),所以走 #else 那支 —— 逐字保留两分支,不做"顺手"改动。
#if PREMIUM
				// premium:副行显示版本号(后端 ModEntry 无作者字段,作者无法显示);compact 保持文件名
				string sub = string.Join(" · ", new[] { m.Version, file }.Where((string x) => !string.IsNullOrWhiteSpace(x)).Distinct());
				rows.Add(Row(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name, sub, sw));
#else
				rows.Add(Row(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name, file, sw));
#endif
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
							// 与 Windows 同:两个分段共用一个 "delete_" + kind 方法名
							await Api.CallAsync("delete_" + kind, new { instance = _inst, filename = file });
							await LoadSegAsync();
						}
					}, I18n.L("删除失败"));
				})));
			}
		}
		// 【Windows 源】:异步回来时用户可能已经切到别的分段了,那就别把这个分段的结果画上去
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
			// 第一行不画上分隔线,其余行画一条 B.RowLine
			if (i > 0 && rows[i] is Border b)
			{
				b.BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0);
			}
			_rows.Children.Add(rows[i]);
		}
	}

	// 白卡内一行:高 46,行间 B.RowLine 细线,悬停 B.RowHover(同 DownloadPage)
	private static Border Row(string name, string? sub, Control tail)
	{
		Grid g = Ui.G(null, "*,Auto");
		g.Add(Ui.V(1.0, Ui.Txt(name, 13.5, bold: true).Trim(), string.IsNullOrEmpty(sub) ? null : Ui.Txt(sub, 11.5, bold: false, "B.InkFaint").Trim()).VCenter(), 0);
		g.Add(tail.VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 1);
		Border row = new Border
		{
			MinHeight = 46.0,
			Padding = new Thickness(18.0, 4.0, 18.0, 4.0),
			Background = Brushes.Transparent,
			Child = g
		};
		row.Dyn(Border.BorderBrushProperty, "B.RowLine");
		row.PointerEntered += delegate
		{
			row.Dyn(Border.BackgroundProperty, "B.RowHover");
		};
		row.PointerExited += delegate
		{
			row.Background = Brushes.Transparent;
		};
		return row;
	}
}

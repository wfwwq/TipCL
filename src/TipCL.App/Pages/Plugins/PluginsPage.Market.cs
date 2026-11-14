using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批4 块1):Windows 版 <c>Pages\Plugins\PluginsPage.Market.cs</c>(12798 字节 / 399 行 / WPF / net48)到
/// Avalonia 11 / net8 的移植 —— 「市场」页签(契约 §9):搜索、标签、审核标记、安装 / 更新(进度跟任务事件走)、
/// 详情(介绍、截图、权限、更新日志)。
///
/// Windows → mac 的差异(**只有三处适配,其余 1:1**):
/// <list type="number">
/// <item><c>RowCard</c> 的点击处理器:<c>MouseButtonEventArgs</c> → <c>PointerReleasedEventArgs</c>;
///   <c>IsInsideButton(DependencyObject)</c> 那圈 WPF 树遍历(<c>VisualTreeHelper</c>/<c>LogicalTreeHelper</c>)
///   → <c>GetSelfAndVisualAncestors().OfType&lt;ButtonBase&gt;()</c>(Avalonia 的正确口径);</item>
/// <item><c>Cursors.Hand</c> → <c>new Cursor(StandardCursorType.Hand)</c>;<c>MouseLeftButtonUp</c> → <c>PointerReleased</c>;</item>
/// <item><c>Visibility.Collapsed/Visible</c> → <c>IsVisible</c>;<c>ToolTip = …</c> → <c>Tip(…)</c>;
///   <c>SetResourceReference(Border.BackgroundProperty, …)</c> → <c>Dyn(…)</c>。</item>
/// </list>
/// </summary>
public sealed partial class PluginsPage
{
	private readonly SPanel _marketHost;

	private Control _marketView = null!;

	private TextBox _search = null!;

	private WrapPanel _tagBar = null!;

	private TextBlock _marketInfo = null!;

	private bool _marketLoaded;

	private List<MarketItem> _market = new List<MarketItem>();

	private string _tag = "";

	private bool _allowUnverified;

	private List<string> _indexes = new List<string>();

	private void BuildMarketChrome()
	{
		_search = Ui.Input(I18n.L("搜索插件名、作者、简介"), "", 300.0);
		_search.TextChanged += delegate
		{
			RenderMarket();
		};
		Button refresh = Ui.Btn(I18n.L("刷新市场"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => LoadMarketAsync(refresh: true), I18n.L("插件市场加载失败"));
		}, "\ue72c");
		Button sources = Ui.Btn(I18n.L("市场地址"), BtnKind.Chip, delegate
		{
			PageBase.Run(EditIndexesAsync);
		}, "\ue71b");
		_marketInfo = Ui.Small("");
		_tagBar = new WrapPanel();
		Grid top = Ui.G(null, "Auto,*,Auto");
		top.Add(Ui.H(6.0, Ui.Glyph("\ue721", 13.0, "B.InkMuted"), _search).VCenter(), 0);
		top.Add(_marketInfo.VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 1);
		top.Add(Ui.H(6.0, sources, refresh).VCenter(), 0, 2);
		_marketView = PageBase.ScrollBody(top, _tagBar, _marketHost);
	}

	private async Task LoadSettingsAsync()
	{
		Dictionary<string, JsonElement> s = (await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings")) ?? new Dictionary<string, JsonElement>();
		_allowUnverified = s.TryGetValue("plugin_allow_unverified", out JsonElement a) && a.ValueKind == JsonValueKind.True;
		_indexes = PluginSettingsCard.ParseIndexes(s.TryGetValue("plugin_market_indexes", out JsonElement ix) ? ix : default(JsonElement));
	}

	private async Task LoadMarketAsync(bool refresh)
	{
		if (!AppServices.Ready)
		{
			return;
		}

		if (_market.Count == 0)
		{
			_marketHost.Children.Clear();
			_marketHost.Children.Add(Ui.Skeleton(72.0));
			_marketHost.Children.Add(Ui.Skeleton(72.0));
		}

		await LoadSettingsAsync();
		JsonElement r;
		try
		{
			r = await PageBase.Api.CallAsync("plugins_market", new { refresh });
		}
		catch (Exception ex)
		{
			_marketLoaded = true;
			_market.Clear();
			_marketHost.Children.Clear();
			_marketHost.Children.Add(Ui.Empty("\ue7ba", I18n.L("插件市场打不开"), ex.Message));
			return;
		}

		_marketLoaded = true;
		_market = J.Items(r).Select(MarketItem.From).Where((MarketItem m) => m.Id.Length > 0).ToList();
		// 合并结果里可能带每个索引的错误:{plugins:[...], errors:[{url, message}]}
		List<string> errors = new List<string>();
		if (r.ValueKind == JsonValueKind.Object && J.TryGet(r, "errors", out JsonElement errs) && errs.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement e in errs.EnumerateArray())
			{
				errors.Add((e.ValueKind == JsonValueKind.String) ? (e.GetString() ?? "") : (J.Str(e, "url") + ": " + J.Str(e, "message")));
			}
		}

		_marketInfo.Text = (errors.Count > 0) ? I18n.L("{0} 个市场地址读取失败", errors.Count) : "";
		if (errors.Count > 0)
		{
			_marketInfo.Tip(string.Join("\n", errors));
		}

		RenderTags();
		RenderMarket();
	}

	private void RenderTags()
	{
		_tagBar.Children.Clear();
		List<string> tags = (from t in _market.SelectMany((MarketItem m) => m.Tags)
			group t by t into g
			orderby g.Count() descending, g.Key
			select g.Key).Take(24).ToList();
		if (tags.Count == 0)
		{
			_tagBar.IsVisible = false;
			return;
		}

		_tagBar.IsVisible = true;
		if (_tag.Length > 0 && !tags.Contains(_tag))
		{
			_tag = "";
		}

		AddTag("", I18n.L("全部"));
		foreach (string t2 in tags)
		{
			AddTag(t2, t2);
		}

		void AddTag(string key, string text)
		{
			Button b = Ui.Btn(text, (_tag == key) ? BtnKind.Primary : BtnKind.Chip);
			b.Margin = new Avalonia.Thickness(0.0, 0.0, 6.0, 6.0);
			b.Click += delegate
			{
				_tag = key;
				RenderTags();
				RenderMarket();
			};
			_tagBar.Children.Add(b);
		}
	}

	private void RenderMarket()
	{
		if (!_marketLoaded)
		{
			return;
		}

		_marketHost.Children.Clear();
		foreach (List<ProgressBar> list in _bars.Values)
		{
			list.RemoveAll((ProgressBar b) => (b.Tag as string) == "market");
		}

		if (_indexes.Count == 0 && _market.Count == 0)
		{
			SPanel empty = Ui.Empty("\uea86", I18n.L("还没有配置插件市场"), I18n.L("填一个或多个插件市场的索引地址（index.json），就能在这里浏览和安装插件。"));
			empty.Children.Add(Ui.Btn(I18n.L("添加市场地址"), BtnKind.Primary, delegate
			{
				PageBase.Run(EditIndexesAsync);
			}, "\ue710").Center());
			_marketHost.Children.Add(empty);
			return;
		}

		string q = _search.Text.Trim();
		List<MarketItem> shown = _market.Where((MarketItem m) => (_tag.Length == 0 || m.Tags.Contains(_tag)) && (q.Length == 0 || Contains(m.DisplayName, q) || Contains(m.Id, q) || Contains(m.Author, q) || Contains(m.Description, q))).OrderByDescending((MarketItem m) => m.Updatable).ThenByDescending((MarketItem m) => m.Verified).ThenByDescending((MarketItem m) => m.Downloads).ThenBy((MarketItem m) => m.DisplayName, StringComparer.CurrentCulture).ToList();
		if (shown.Count == 0)
		{
			_marketHost.Children.Add(Ui.Empty("\ue721", (_market.Count == 0) ? I18n.L("市场里还没有插件") : I18n.L("没有找到匹配的插件")));
			return;
		}

		foreach (MarketItem m2 in shown)
		{
			_marketHost.Children.Add(MarketCard(m2));
		}

		static bool Contains(string s, string q2)
		{
			return s.IndexOf(q2, StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}

	private static Border VerifiedTag(bool verified)
	{
		if (!verified)
		{
			return Ui.Tag(I18n.L("未审核"), "B.Danger", "B.DangerSoft");
		}

		return Ui.Tag(I18n.L("已审核"), "B.Ok", "B.AccentSoft");
	}

	private Button InstallButton(MarketItem m)
	{
		if (m.Installed && !m.Updatable)
		{
			Button done = Ui.Btn(I18n.L("已安装"), BtnKind.Ghost, null, "\ue73e");
			done.IsEnabled = false;
			return done;
		}

		Button b = Ui.Btn(m.Updatable ? I18n.L("更新") : I18n.L("安装"), m.Updatable ? BtnKind.Soft : BtnKind.Primary, null, "\ue896");
		b.Click += delegate
		{
			PageBase.Run(() => InstallFromMarketAsync(m, b), m.Updatable ? I18n.L("更新失败") : I18n.L("安装失败"));
		};
		if (m.Updatable && m.InstalledVersion.Length > 0)
		{
			b.Tip(I18n.L("{0} → {1}", m.InstalledVersion, m.Version));
		}

		return b;
	}

	private Border MarketCard(MarketItem m)
	{
		Control icon = PluginIcon.Make(m.Icon, 36.0, "B.AccentText");
		Border iconBox = new Border
		{
			Width = 48.0,
			Height = 48.0,
			CornerRadius = new Avalonia.CornerRadius(10.0),
			Child = icon.Center().VCenter(),
		};
		iconBox.Dyn(Border.BackgroundProperty, "B.AccentTint");
		SPanel title = Ui.H(6.0, Ui.Txt(m.DisplayName, 14.0, bold: true).Trim().VCenter(), (m.Version.Length > 0) ? Ui.Tag("v" + m.Version) : null, VerifiedTag(m.Verified), (m.Runtime == "dotnet") ? Ui.Tag(".NET") : null);
		List<string> metaParts = new List<string>();
		if (m.Author.Length > 0)
		{
			metaParts.Add(I18n.L("作者 {0}", m.Author));
		}

		if (m.Downloads > 0)
		{
			metaParts.Add(Fmt.Downloads(m.Downloads));
		}

		if (m.Size > 0)
		{
			metaParts.Add(Fmt.Size(m.Size));
		}

		TextBlock desc = Ui.Muted(m.Description).Wrap();
		desc.MaxHeight = 40.0;
		desc.TextTrimming = TextTrimming.CharacterEllipsis;
		WrapPanel tags = new WrapPanel();
		foreach (string t in m.Tags.Take(6))
		{
			tags.Children.Add(Ui.Tag(t).M(0.0, 0.0, 4.0, 0.0));
		}

		ProgressBar bar = Ui.Prog();
		bar.Height = 4.0;
		bar.Tag = "market";
		bar.IsVisible = false;
		Track(m.Id, bar);
		SPanel info = Ui.V(3.0, title, (metaParts.Count > 0) ? Ui.Small(string.Join(" · ", metaParts)) : null, (m.Description.Length > 0) ? desc : null, (tags.Children.Count > 0) ? tags : null, bar.M(0.0, 4.0, 0.0, 0.0));
		Button install = InstallButton(m);
		Button detail = Ui.IconBtn("\ue946", I18n.L("详情"), delegate
		{
			ShowMarketDetail(m);
		});
		Grid g = Ui.G(null, "Auto,*,Auto");
		g.Add(iconBox.VTop(), 0);
		g.Add(info.M(14.0, 0.0, 12.0, 0.0), 0, 1);
		g.Add(Ui.H(6.0, detail, install).VCenter(), 0, 2);
		return Ui.RowCard(g, delegate(object? _, PointerReleasedEventArgs e)
		{
			if (!IsInsideButton(e.Source as Visual))
			{
				ShowMarketDetail(m);
			}
		}, 14.0);
	}

	/// <summary>【Windows 源】<c>IsInsideButton(DependencyObject)</c> 那圈 WPF 树遍历 →
	/// Avalonia 的 <c>GetSelfAndVisualAncestors()</c>(判据一致:点在按钮里就不算"点卡片")。
	/// 这里按**三种按钮类型**判,而不是 Avalonia 的 <c>ButtonBase</c>(它在 11 里的命名空间与 WPF 不同,
	/// 直接写名字会 CS0246/CS0234;三种按钮类型两平台都在)。</summary>
	private static bool IsInsideButton(Visual? source)
	{
		return source != null && source.GetSelfAndVisualAncestors().Any((Visual a) => a is Button || a is Avalonia.Controls.Primitives.ToggleButton);
	}

	private void ShowMarketDetail(MarketItem m)
	{
		SPanel body = Ui.V(14.0);
		Control icon = PluginIcon.Make(m.Icon, 44.0, "B.AccentText");
		SPanel head = Ui.H(12.0, icon.VTop(), Ui.V(3.0, Ui.H(6.0, Ui.Txt(m.DisplayName, 16.0, bold: true).VCenter(), (m.Version.Length > 0) ? Ui.Tag("v" + m.Version) : null, VerifiedTag(m.Verified)), Ui.Small(string.Join(" · ", new string[3]
		{
			(m.Author.Length > 0) ? I18n.L("作者 {0}", m.Author) : "",
			m.Id,
			(m.Size > 0) ? Fmt.Size(m.Size) : "",
		}.Where((string s) => s.Length > 0)))));
		body.Children.Add(head);
		if (m.Description.Length > 0)
		{
			body.Children.Add(Ui.Txt(m.Description, 13.0, bold: false, "B.InkSoft").Wrap());
		}

		if (m.Screenshots.Count > 0)
		{
			StackPanel shots = new StackPanel
			{
				Orientation = Orientation.Horizontal,
			};
			foreach (string url in m.Screenshots.Take(8))
			{
				Image img = new Image
				{
					Height = 180.0,
					Stretch = Stretch.Uniform,
					Margin = new Avalonia.Thickness(0.0, 0.0, 8.0, 0.0),
					Cursor = new Cursor(StandardCursorType.Hand),
				};
				img.Tip(I18n.L("点击在浏览器里看大图"));
				string u = url;
				img.PointerReleased += delegate
				{
					Ui.OpenUrl(u);
				};
				shots.Children.Add(img);
				LoadShot(img, url);
			}

			body.Children.Add(new ScrollViewer
			{
				Content = shots,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			});
		}

		body.Children.Add(Ui.H3(I18n.L("需要的权限")));
		body.Children.Add(PluginPerms.List(PluginPerms.Effective(m.Permissions, m.Runtime)));
		if (m.Changelog.Length > 0)
		{
			body.Children.Add(Ui.H3(I18n.L("更新日志")));
			body.Children.Add(Ui.Txt(m.Changelog, 12.5, bold: false, "B.InkSoft").Wrap());
		}

		Button install = InstallButton(m);
		SPanel actions = Ui.H(8.0, install);
		if (m.Homepage.Length > 0)
		{
			actions.Children.Insert(0, Ui.Btn(I18n.L("插件主页"), BtnKind.Normal, delegate
			{
				Ui.OpenUrl(m.Homepage);
			}, "\ue8a7"));
		}

		// WP-S11:操作按钮行放滚动区外,钉在弹窗底部,矮窗口下不再被卷走
		ScrollViewer sv = new ScrollViewer
		{
			Content = body,
			MaxHeight = Ui.WindowPctOf(0.62, 300.0),
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		};
		Dlg.Layer layer = Dlg.Panel(m.DisplayName, Ui.V(12.0, sv, actions.Right()), 760.0);
		install.Click += delegate
		{
			layer.Close();
		};
	}

	private static async void LoadShot(Image img, string url)
	{
		try
		{
			img.Source = await Thumbs.LoadAsync(url, 360);
		}
		catch
		{
			// 【Windows 源】:空 catch —— 截图加载失败就当没有
		}
	}

	private async Task InstallFromMarketAsync(MarketItem m, Control source)
	{
		if (!m.Verified && !_allowUnverified)
		{
			if (!await Dlg.Confirm(I18n.L("未审核的插件"), I18n.L("「{0}」没有经过市场审核。安装未审核插件需要先打开「允许安装未审核插件」(设置 → 插件)。\n\n现在打开并继续安装?", m.DisplayName), I18n.L("打开并继续"), null, danger: true))
			{
				return;
			}

			await PageBase.Api.CallAsync<object>("save_settings", new
			{
				data = new Dictionary<string, object> { ["plugin_allow_unverified"] = true }
			});
			_allowUnverified = true;
		}

		// 权限确认由内核在校验完下载包后通过 plugins.confirm 事件弹出
		await StartPluginTaskAsync("plugins_install", new { id = m.Id }, m.Id, source);
	}

	private async Task EditIndexesAsync()
	{
		await LoadSettingsAsync();
		List<string>? next = await PluginSettingsCard.EditIndexesDialogAsync(_indexes);
		if (next != null)
		{
			_indexes = next;
			await LoadMarketAsync(refresh: true);
		}
	}
}

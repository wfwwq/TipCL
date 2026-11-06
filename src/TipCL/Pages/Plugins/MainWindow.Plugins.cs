using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TipCL.Pages;
using TipCL.Pages.Plugins;
using TipCL.Services;

namespace TipCL;

// 主窗口里和插件有关的部分：侧栏里的插件页面入口、插件页面对象、插件事件转发、浮层挂接。
public partial class MainWindow
{
	private readonly Dictionary<string, ToggleButton> _pluginNavRows = new Dictionary<string, ToggleButton>();

	private bool _pluginMoreRendered;

	private bool _pluginsHooked;

	/// <summary>构造时调用一次：订阅插件界面变化。</summary>
	private void HookPlugins()
	{
		if (_pluginsHooked)
		{
			return;
		}
		_pluginsHooked = true;
		PluginUi.Changed += OnPluginUiChanged;
		PluginOverlays.Attach(this);
	}

	/// <summary>连上后端后拉一次插件界面贡献。</summary>
	private void LoadPluginsAfterConnect()
	{
		HookPlugins();
		Run(PluginUi.RefreshAsync);
	}

	private void OnPluginUiChanged()
	{
		// 侧栏入口
		BuildNav();
		// 已经打开过的插件页面：更新地址，或者标成不可用
		foreach (KeyValuePair<string, PageBase> kv in _pages.ToList())
		{
			if (kv.Value is PluginHostPage page)
			{
				UiContribution? c = PluginUi.FindPage(kv.Key);
				if (c != null)
				{
					page.Update(c);
				}
				else
				{
					page.MarkGone();
					if (_currentId != kv.Key)
					{
						_pages.Remove(kv.Key);
					}
				}
			}
		}
	}

	/// <summary>NavDefs 里没有的页面 id：插件页面 plugin:&lt;plugin&gt;:&lt;id&gt;。</summary>
	private PageBase? CreatePluginPage(string id)
	{
		if (!id.StartsWith("plugin:", StringComparison.Ordinal))
		{
			return null;
		}
		UiContribution? c = PluginUi.FindPage(id);
		if (c == null)
		{
			return null;
		}
		return new PluginHostPage(c)
		{
			Id = id
		};
	}

	private static IEnumerable<UiContribution> PluginNavPages()
	{
		return PluginUi.OfKind("page");
	}

	/// <summary>BuildGroup("more") 末尾：把 group 为 more（默认）的插件页面挂在「更多」分区里。</summary>
	private void AppendPluginMoreRows(Panel panel)
	{
		_pluginMoreRendered = true;
		foreach (UiContribution c in PluginNavPages().Where((UiContribution c) => c.Group != "top"))
		{
			panel.Children.Add(MakePluginRow(c, inSection: true));
		}
	}

	/// <summary>BuildNav 末尾：group 为 top 的插件页面（以及「更多」分区被隐藏时的全部插件页面）放在主列表末尾。</summary>
	private void AppendPluginTopRows()
	{
		List<UiContribution> list = PluginNavPages().Where((UiContribution c) => c.Group == "top" || !_pluginMoreRendered).ToList();
		if (list.Count == 0)
		{
			return;
		}
		if (NavModel.NavStyle(_navCfg) == "grouped")
		{
			NavHost.Children.Add(MakeHeader(I18n.L("插件")));
		}
		foreach (UiContribution c in list)
		{
			NavHost.Children.Add(MakePluginRow(c, inSection: false));
		}
	}

	private ToggleButton MakePluginRow(UiContribution c, bool inSection)
	{
		FrameworkElement icon = PluginIcon.Make(c.Icon, 16.0, "B.InkSoft");
		icon.HorizontalAlignment = HorizontalAlignment.Center;
		TextBlock label = Ui.Txt(c.Label).VCenter();
		label.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid grid = Ui.G(null, "18,*,Auto");
		grid.Add(icon.VCenter(), 0);
		grid.Add(label.M(10.0, 0.0, 0.0, 0.0), 0, 1);
		ToggleButton btn = new ToggleButton
		{
			Style = Ui.S("NavItem"),
			Content = grid,
			Tag = c.PageId,
			Margin = new Thickness(inSection ? 12 : 0, 0.0, 0.0, 0.0),
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			ToolTip = I18n.L("插件：{0}", PluginUi.PluginName(c.Plugin))
		};
		if (inSection)
		{
			icon.Opacity = 0.85;
		}
		string pageId = c.PageId;
		btn.Click += delegate
		{
			btn.IsChecked = true;
			Navigate(pageId);
		};
		ContextMenu menu = new ContextMenu();
		Item(menu, I18n.L("管理插件"), delegate
		{
			Navigate("plugins");
		});
		if (c.Url.Length > 0)
		{
			string url = c.Url;
			Item(menu, I18n.L("在浏览器中打开"), delegate
			{
				Ui.OpenUrl(WebViewHost.WithParams(url));
			});
		}
		btn.ContextMenu = menu;
		btn.IsChecked = _currentId == pageId;
		_pluginNavRows[pageId] = btn;
		return btn;
	}

	private void SyncPluginNavSelection()
	{
		foreach (KeyValuePair<string, ToggleButton> kv in _pluginNavRows)
		{
			kv.Value.IsChecked = kv.Key == _currentId;
		}
	}
}

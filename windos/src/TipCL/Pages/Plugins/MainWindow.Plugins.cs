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

// 主窗口里和插件有关的部分：插件页面对象、插件事件转发、浮层挂接。
public partial class MainWindow
{
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
		RebuildPluginTabs();
	}

	private readonly List<ToggleButton> _pluginTabs = new List<ToggleButton>();

	// 旧侧栏删除后（WP-F），group "top" 的插件页面（老版渲染为侧栏一级入口）在壳上唯一的落脚点：
	// 追加为顶部页签（设置之后），按 Order 稳定排序；插件卸载 / 页面注销时由 OnPluginUiChanged 重建。
	internal void RebuildPluginTabs()
	{
		if (TopTabPanel == null)
		{
			return;
		}
		foreach (ToggleButton t in _pluginTabs)
		{
			TopTabPanel.Children.Remove(t);
		}
		_pluginTabs.Clear();
		foreach (UiContribution c in PluginUi.OfKind("page")
			.Where((UiContribution p) => p.Group == "top")
			.OrderBy((UiContribution p) => p.Order)
			.ThenBy((UiContribution p) => p.Plugin, StringComparer.Ordinal)
			.ToList())
		{
			string pageId = c.PageId;
			string label = (string.IsNullOrEmpty(c.Icon) ? "" : c.Icon + " ") + c.Label;
			TextBlock text = new TextBlock
			{
				Text = label,
				MaxWidth = 128.0,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			ToggleButton tab = new ToggleButton
			{
				Style = (Style)FindResource("Tab.Top"),
				Content = text,
				Tag = pageId,
				ToolTip = c.Label
			};
			tab.Click += delegate
			{
				Navigate(pageId);
			};
			TopTabPanel.Children.Add(tab);
			_pluginTabs.Add(tab);
		}
		SyncTabs();
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
}

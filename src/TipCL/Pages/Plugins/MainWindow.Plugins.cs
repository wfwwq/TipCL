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

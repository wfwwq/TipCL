using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 插件动作按钮（kind "action"，spec.where ∈ instance / launch / ai.input / tray）。
/// 点击调用 plugins_action(plugin, id, context)，内核再发 action.invoke 给插件。没有动作时整条隐藏。
/// </summary>
public sealed class PluginActionBar : WrapPanel
{
	private readonly string _where;

	private readonly Func<Dictionary<string, object?>> _context;

	public PluginActionBar(string where, Func<Dictionary<string, object?>>? context = null)
	{
		_where = where;
		_context = context ?? (() => new Dictionary<string, object>());
		base.Orientation = Orientation.Horizontal;
		base.VerticalAlignment = VerticalAlignment.Center;
		Rebuild();
		// 弱一点的订阅方式没必要：这些按钮条和所在页面一样常驻
		PluginUi.Changed += delegate
		{
			base.Dispatcher.BeginInvoke((Action)Rebuild);
		};
	}

	private void Rebuild()
	{
		base.Children.Clear();
		List<UiContribution> list = PluginUi.OfKind("action").Where((UiContribution c) => c.Where == _where).ToList();
		foreach (UiContribution c in list)
		{
			UiContribution cc = c;
			string icon = cc.Icon;
			Button b = Ui.Btn(cc.Label, BtnKind.Chip, null, (icon.Length > 0 && icon.Length <= 2) ? icon : "\uea86");
			b.Margin = new Thickness(0.0, 0.0, 6.0, 0.0);
			b.ToolTip = I18n.L("插件：{0}", PluginUi.PluginName(cc.Plugin));
			b.Click += delegate
			{
				Dictionary<string, object?> ctx = _context();
				ctx["where"] = _where;
				PageBase.Run(async delegate
				{
					await AppServices.Client.CallAsync("plugins_action", new
					{
						plugin = cc.Plugin,
						id = cc.Id,
						context = ctx
					});
				}, I18n.L("插件动作失败"));
			};
			base.Children.Add(b);
		}
		base.Visibility = (base.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批4):Windows 版 <c>Pages\Plugins\PluginActionBar.cs</c>(1979 字节 / 63 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 插件动作按钮(kind <c>action</c>,<c>spec.where ∈ instance / launch / ai.input / tray</c>)。
/// 点击调 <c>plugins_action(plugin, id, context)</c>,内核再发 <c>action.invoke</c> 给插件。
/// **没有匹配的动作时整条隐藏**(Windows:<c>Visibility.Collapsed</c> ⇒ mac <c>IsVisible = false</c>)。
///
/// Windows → mac 的差异:
/// <list type="bullet">
/// <item><c>WrapPanel</c> 两平台同名(本类**就是** <see cref="WrapPanel"/> 的子类,这一点照搬);
///   <c>Orientation.Horizontal</c> / <c>VerticalAlignment.Center</c> 走 Avalonia 的同名枚举;</item>
/// <item><c>Dispatcher.BeginInvoke((Action)Rebuild)</c> → <c>Dispatcher.UIThread.Post(Rebuild)</c>(同 C2c 的既有口径);</item>
/// <item><c>b.ToolTip = …</c> → <c>b.Tip(…)</c>(Avalonia 的 ToolTip 是附加属性,本仓统一走 UiExt);</item>
/// <item>其余调用形状一字未改:<c>PluginUi.OfKind("action")</c>、<c>c.Where</c>、<c>Ui.Btn(Label, Chip, …, icon)</c>
///   (图标只有 1-2 字符时才当字形用,否则给默认字形)、<c>plugins_action</c> 的 <c>plugin/id/context</c> 三参
///   (并把 <c>where</c> 塞进 context)。</item>
/// </list>
/// </summary>
public sealed class PluginActionBar : WrapPanel
{
	private readonly string _where;

	private readonly Func<Dictionary<string, object?>> _context;

	/// <summary>订阅委托存成字段:退订时拿得到**同一个**实例(<c>PluginUi.Changed</c> 是进程级静态)。</summary>
	private readonly Action _onChanged;

	public PluginActionBar(string where, Func<Dictionary<string, object?>>? context = null)
	{
		_where = where;
		_context = context ?? (() => new Dictionary<string, object>());
		Orientation = Orientation.Horizontal;
		VerticalAlignment = VerticalAlignment.Center;
		Rebuild();
		// 【Windows 源】注释原话:弱一点的订阅方式没必要 —— 这些按钮条和所在页面一样常驻
		_onChanged = delegate
		{
			Dispatcher.UIThread.Post(Rebuild);
		};
		PluginUi.Changed += _onChanged;
	}

	/// <summary>
	/// 退订(**给测试与页面卸载用**;Windows 没有这一步 —— 那些按钮条常驻到进程结束)。
	/// 不退订的话,进程级静态事件会一直抓着这个控件(测试之间就会互相影响)。
	/// </summary>
	internal void Detach()
	{
		PluginUi.Changed -= _onChanged;
	}

	private void Rebuild()
	{
		Children.Clear();
		List<UiContribution> list = PluginUi.OfKind("action").Where((UiContribution c) => c.Where == _where).ToList();
		foreach (UiContribution c in list)
		{
			UiContribution cc = c;
			string icon = cc.Icon;
			Button button = Ui.Btn(cc.Label, BtnKind.Chip, null, (icon.Length > 0 && icon.Length <= 2) ? icon : "\uea86");
			button.Margin = new Avalonia.Thickness(0.0, 0.0, 6.0, 0.0);
			button.Tip(I18n.L("插件：{0}", PluginUi.PluginName(cc.Plugin)));
			button.Click += delegate
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
			Children.Add(button);
		}

		// 【Windows 源】:没有动作时整条 Collapsed
		IsVisible = Children.Count > 0;
	}
}

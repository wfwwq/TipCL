using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 插件侧栏页面（kind "page"）。页面对象被 MainWindow 缓存，切走再回来 WebView 不重建（和 DSH 保留面板一样）。
/// 显示 / 隐藏时通过 plugins_ui_event 告诉插件（Node 宿主转成 ui.event 通知），比如大肥鱼在游戏库打开时自己收起浮层。
/// </summary>
public sealed class PluginHostPage : PageBase
{
	private UiContribution _c;

	private readonly Border _frame;

	private FrameworkElement? _content;

	private bool _gone;

	public override string Title => _c.Label;

	public UiContribution Contribution => _c;

	public PluginHostPage(UiContribution c)
	{
		_c = c;
		_frame = new Border
		{
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(16.0, 8.0, 16.0, 16.0),
			ClipToBounds = true
		};
		_frame.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_frame.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		base.Content = _frame;
		Build();
	}

	private void Build()
	{
		(_content as IDisposable)?.Dispose();
		_content = NativePluginUi.Content(_c);
		_frame.Child = _content;
	}

	/// <summary>插件重载或界面列表刷新后：地址变了就换地址，不重建页面。</summary>
	public void Update(UiContribution c)
	{
		bool wasGone = _gone;
		_gone = false;
		UiContribution old = _c;
		_c = c;
		if (wasGone || old.Native != c.Native || (!c.Native && !(_content is PluginWebView)))
		{
			Build();
		}
		else if (_content is PluginWebView web)
		{
			web.SetUrl(c.Url);
		}
	}

	/// <summary>插件被禁用 / 卸载：关掉浏览器，显示提示。</summary>
	public void MarkGone()
	{
		if (_gone)
		{
			return;
		}
		_gone = true;
		(_content as IDisposable)?.Dispose();
		Button manage = Ui.Btn(I18n.L("管理插件"), BtnKind.Soft, delegate
		{
			AppServices.Window?.Navigate("plugins");
		}, "\uea86");
		SPanel empty = Ui.Empty("\uea86", I18n.L("这个插件页面已经不可用"), I18n.L("插件可能被禁用、卸载或正在重新加载。"));
		empty.Children.Add(manage.Center());
		_content = empty;
		_frame.Child = empty;
	}

	public override void OnShown()
	{
		Notify("shown");
	}

	public override void OnHidden()
	{
		Notify("hidden");
	}

	private void Notify(string ev)
	{
		if (!AppServices.Ready || _gone)
		{
			return;
		}
		UiContribution c = _c;
		PageBase.Run(async delegate
		{
			await AppServices.Client.TryCallAsync<object>("plugins_ui_event", new
			{
				plugin = c.Plugin,
				kind = c.Kind,
				id = c.Id,
				@event = ev,
				data = new Dictionary<string, object>()
			});
		});
	}
}

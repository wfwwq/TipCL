using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// C7b(批3 轮A):Windows 版 <c>Pages\Plugins\PluginHostPage.cs</c>(2726 字节 / 114 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 插件侧栏页面(kind <c>page</c>):外壳缓存页面对象,切走再回来内容不重建。
/// 显示 / 隐藏时通过 <c>plugins_ui_event</c> 告诉插件(Node 宿主转成 ui.event),比如插件在别的页打开时自己收起浮层。
///
/// Windows → mac 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>FrameworkElement</c> → <c>Control</c>;内容一律走 <see cref="NativePluginUi.Content"/>
///   (原生工厂优先;网页贡献 ⇒ 那张**如实内容卡 + 「在浏览器中打开」**,见 D-154/D-158 的设计差异);</item>
/// <item>**没有 <c>PluginWebView</c>**:所以 <c>Update</c> 里 Windows 的"只换地址、不重建"这一支不成立 ——
///   mac 的内容是不可换地址的卡 ⇒ 贡献变了就**重建内容**(一张卡的代价,没有 WebView 重建会闪的问题),
///   但**页面对象本身不重建**(外壳缓存语义与 Windows 一致,切走再回来还是同一个实例);</item>
/// <item><c>SetResourceReference(Border.BackgroundProperty/BorderBrushProperty, "B.Paper"/"B.Line")</c> → <c>Dyn(...)</c>;</item>
/// <item><c>AppServices.Window?.Navigate("plugins")</c> → <c>(AppServices.Window as MainWindow)?.Navigate("plugins")</c>(C2a 记录的静态类型差异);</item>
/// <item><c>(content as IDisposable)?.Dispose()</c> → mac 的内容是 <c>Control</c>(不可释放)⇒ 直接换掉,不调 Dispose。</item>
/// </list>
/// </summary>
public sealed class PluginHostPage : PageBase
{
	private UiContribution _contribution;

	private readonly Border _frame;

	private Control? _content;

	private bool _gone;

	public override string Title => _contribution.Label;

	public UiContribution Contribution => _contribution;

	public PluginHostPage(UiContribution c)
	{
		_contribution = c;
		_frame = new Border
		{
			BorderThickness = new Avalonia.Thickness(1.0),
			Margin = new Avalonia.Thickness(16.0, 8.0, 16.0, 16.0),
			ClipToBounds = true,
		};
		_frame.Dyn(Border.BackgroundProperty, "B.Paper");
		_frame.Dyn(Border.BorderBrushProperty, "B.Line");
		Content = _frame;
		Build();
	}

	private void Build()
	{
		_content = NativePluginUi.Content(_contribution);
		_frame.Child = _content;
	}

	/// <summary>插件重载或界面列表刷新后:换掉内容(页面对象不重建,外壳缓存的还是同一个实例)。</summary>
	public void Update(UiContribution c)
	{
		_gone = false;
		_contribution = c;
		// 【Windows 源】:wasGone / 原生↔网页 切换 / 内容不是 WebView 时重建,否则只 SetUrl。
		// 【mac 差异】:没有 WebView,内容是一张卡 ⇒ 一律重建(且原生↔网页的切换自然也被覆盖)。
		Build();
	}

	/// <summary>插件被禁用 / 卸载:换成一屏如实提示 + 「管理插件」按钮。</summary>
	public void MarkGone()
	{
		if (_gone)
		{
			return;
		}

		_gone = true;
		Button manage = Ui.Btn(I18n.L("管理插件"), BtnKind.Soft, delegate
		{
			(AppServices.Window as MainWindow)?.Navigate("plugins");
		}, "\uea86");
		SPanel empty = Ui.Empty("\uea86", I18n.L("这个插件页面已经不可用"), I18n.L("插件可能被禁用、卸载或正在重新加载。"));
		empty.Children.Add(manage.Center());
		_content = empty;
		_frame.Child = empty;
	}

	/// <summary>【Windows 源】<c>:85-88</c>:显示时告诉插件。</summary>
	public override void OnShown()
	{
		Notify("shown");
	}

	/// <summary>【Windows 源】<c>:90-93</c>:隐藏时告诉插件。</summary>
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

		UiContribution c = _contribution;
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

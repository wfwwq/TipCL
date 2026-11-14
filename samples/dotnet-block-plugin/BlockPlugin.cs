using System;
using System.Windows;
using System.Windows.Controls;
using TipCL.Plugins;

namespace DotNetBlockSample;

/// <summary>
/// 最小 .NET 原生插件(kind = <c>block</c>,DESIGN §3.2 / §4.F):
/// <see cref="Apply"/> 里用 <see cref="IPluginUi.RegisterBlock"/> 注册一个布局区块,
/// 布局文档再以 <c>plugin:dotnet-block-sample:hello</c> 引用它。
///
/// 刻意不依赖 <c>TipCL.Services.Ui</c> / 主题资源:纯 WPF 控件就够,样例读起来最短,
/// 也让运行时 E2E 不依赖任何界面主题初始化。
/// </summary>
public sealed class BlockPlugin : ITipCLPlugin
{
	/// <summary>块内容里必须出现的一行字(E2E 就是按它断言"布局里拿到的是插件自己的控件")。</summary>
	public const string BlockText = "来自 .NET 示例插件的区块";

	/// <summary>注册进注册表的块 id:布局里的块 id = <c>plugin:dotnet-block-sample:hello</c>。</summary>
	public const string BlockId = "hello";

	public void Apply(IPluginContext ctx)
	{
		ctx.Logger.Info("dotnet-block-sample: Apply 开始");
		// RegisterBlock 内部会写 PluginUiRegistry(Kind="block"),并把句柄包进 Effect 逆序撤销;
		// 需要 ui 权限(manifest 已声明)。
		ctx.Ui.RegisterBlock(
			new PluginBlockSpec
			{
				Id = BlockId,
				Label = "示例 .NET 区块",
				Group = "sample",
			},
			CreateBlock);
		ctx.Logger.Info("dotnet-block-sample: 已注册 block " + BlockId);
	}

	/// <summary>
	/// 造块内容。宿主每次需要这个块时都会调一次工厂,所以**每次都返回新控件**
	/// (同一个控件不能被挂到两处可视树)。
	/// </summary>
	private static FrameworkElement CreateBlock()
	{
		StackPanel panel = new StackPanel
		{
			Margin = new Thickness(10.0),
		};
		panel.Children.Add(new TextBlock
		{
			Text = BlockText,
			FontWeight = FontWeights.SemiBold,
		});
		Button button = new Button
		{
			Content = "点我 +1",
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Padding = new Thickness(10.0, 3.0, 10.0, 3.0),
		};
		TextBlock counter = new TextBlock
		{
			Text = "点了 0 次",
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
		};
		int clicked = 0;
		button.Click += delegate
		{
			clicked++;
			counter.Text = "点了 " + clicked + " 次";
		};
		panel.Children.Add(button);
		panel.Children.Add(counter);
		return panel;
	}
}

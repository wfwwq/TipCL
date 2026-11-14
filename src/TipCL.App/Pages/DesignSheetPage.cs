using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// U6:Windows 版 <c>Pages\DesignSheetPage.cs</c>(6325 字节 / 196 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// **设计样张页**:把设计规范里的全部令牌与组件一次画出来(间距栅格 / 字号阶梯 / 圆角 / 色板 / 按钮 / 输入与选择 /
/// 标签与徽章 / 卡片解剖 / 空态与骨架),当逐页修复的视觉基准。
///
/// <b>门槛与可达性(与 Windows 一致)</b>:Windows 侧它是 dev-only 样张页,靠 <c>MainWindow.xaml.cs:817-828</c>
/// 处理 <c>id == "design-sheet"</c> 才到得了,<b>不进默认导航</b>(原注释:「dev-only 样张页,不在导航注册」)。
/// mac 侧的等价物是页面注册表:<see cref="DesignSheetPageRegistration"/> 用 <c>[ModuleInitializer]</c> 把
/// <c>"design-sheet"</c> 注册进 <see cref="PageRegistry"/>,于是 <c>PageRegistry.Create("design-sheet")</c>
/// 建得出这一页(外壳的 <c>NavDefs</c> 一个字不动 ⇒ 侧栏/导航里不会多出任何东西,用户可见面为零)。
///
/// Windows → Avalonia 的差异(逐条):
/// <list type="bullet">
/// <item><c>UIElement</c> → <c>Control</c>;<c>Application.Current.TryFindResource(key) is Brush b</c> →
///   <c>Palette.TryGet(key, Palette.Current, out ...)</c>(**语义一字不改**:查不到就不设背景。
///   不要换成 <c>Ui.Res(key)</c> —— 那条口径是"查不到给中性灰兜底",这里要的是 Windows 的"查不到就不动");</item>
/// <item><c>Border.SetResourceReference(prop, key)</c> → <c>Dyn(prop, key)</c>(UiExt 的既有等价物,UiExt.cs:25-29);</item>
/// <item><c>TextTrimming.CharacterEllipsis</c> / <c>CornerRadius</c> / <c>Thickness</c> 在 Avalonia 同名同义;</item>
/// <item>样张上的标签文案在 Windows 源里就是**字面串**(不走 <c>I18n.L</c>,它们是规范标题不是用户文案),
///   逐字保留 —— 本文件唯一走 <c>I18n.L</c> 的是 <see cref="Title"/>(与 Windows 的 <c>Title => I18n.L("设计样张")</c> 同)。</item>
/// </list>
/// </summary>
public sealed class DesignSheetPage : PageBase
{
	/// <summary>【Windows 源】<c>:14</c>:标题「设计样张」。</summary>
	public override string Title => I18n.L("设计样张");

	/// <summary>【Windows 源】<c>:16-20</c>:正文 = 可滚动的样张(<c>ScrollBody(Sheet())</c>,外边距、滚动容器与别的页面同一套)。</summary>
	protected override Task LoadAsync()
	{
		base.Content = ScrollBody(Sheet());
		return Task.CompletedTask;
	}

	/// <summary>【Windows 源】<c>:22-35</c>:九张卡片,顺序与标题一字不改。</summary>
	private static Control Sheet()
	{
		return Ui.V(10.0,
			Card("间距栅格 4px {4,6,8,10,12,16,20,24}", SpacingRow()),
			Card("字号阶梯 {11,12,13,15,17,20} 禁止>20", TypeLadder()),
			Card("圆角 {6 控件,8 小卡,10 卡片} 999 仅徽章", RadiusRow()),
			Card("色板(当前主题)", Swatches()),
			Card("按钮", BtnRow()),
			Card("输入与选择", InputRows()),
			Card("标签与徽章", TagRow()),
			Card("卡片解剖 Card12/RowCard10/Field/Section", CardSamples()),
			Card("空态与骨架", EmptyRow())
		);
	}

	/// <summary>【Windows 源】<c>:37-40</c>:一张样张卡(内距 12;标题走 Muted)。</summary>
	private static Border Card(string title, Control content)
	{
		return Ui.Card(Ui.V(8.0, Ui.Muted(title), content), 12.0);
	}

	/// <summary>【Windows 源】<c>:42-58</c>:间距栅格(宽度 = 值 × 4,高 14,圆角 3,底色 AccentSoft)。</summary>
	private static Control SpacingRow()
	{
		SPanel row = Ui.H(8.0);
		double[] vals = new double[8] { 4.0, 6.0, 8.0, 10.0, 12.0, 16.0, 20.0, 24.0 };
		foreach (double v in vals)
		{
			Border bar = new Border
			{
				Width = v * 4.0,
				Height = 14.0,
				CornerRadius = new CornerRadius(3.0)
			};
			bar.Dyn(Border.BackgroundProperty, "B.AccentSoft");
			row.Children.Add(Ui.V(4.0, bar, Ui.Small(((int)v).ToString()).Center()));
		}
		return row;
	}

	/// <summary>【Windows 源】<c>:60-81</c>:字号阶梯(H1 20 / H2 17 / H3 15 / 正文 13 / Muted 12 / Small 11)。</summary>
	private static Control TypeLadder()
	{
		TextBlock[] samples = new TextBlock[6]
		{
			Ui.H1("标题 20 页面标题"),
			Ui.H2("标题 17 卡片标题"),
			Ui.H3("小标题 15 行内"),
			Ui.Txt("正文 13 默认"),
			Ui.Muted("辅助 12 说明"),
			Ui.Small("小注 11 脚注")
		};
		string[] tags = new string[6] { "H1 20", "H2 17", "H3 15", "正文 13", "Muted 12", "Small 11" };
		SPanel stack = Ui.V(6.0);
		for (int i = 0; i < samples.Length; i++)
		{
			Grid g = Ui.G(null, "110,*");
			g.Add(Ui.Small(tags[i]).VCenter(), 0);
			g.Add(samples[i].VCenter(), 0, 1);
			stack.Children.Add(g);
		}
		return stack;
	}

	/// <summary>【Windows 源】<c>:83-111</c>:圆角(6 / 8 / 10 三个方块 + 一个 999 徽章)。</summary>
	private static Control RadiusRow()
	{
		SPanel row = Ui.H(8.0);
		double[] radii = new double[3] { 6.0, 8.0, 10.0 };
		string[] labels = new string[3] { "Ctl 6", "Small 8", "Card 10" };
		for (int i = 0; i < radii.Length; i++)
		{
			Border box = new Border
			{
				Width = 56.0,
				Height = 36.0,
				CornerRadius = new CornerRadius(radii[i]),
				BorderThickness = new Thickness(1.4)
			};
			box.Dyn(Border.BackgroundProperty, "B.AccentSoft");
			box.Dyn(Border.BorderBrushProperty, "B.Accent");
			row.Children.Add(Ui.V(4.0, box, Ui.Small(labels[i]).Center()));
		}
		Border pill = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(12.0, 4.0, 12.0, 5.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		pill.Dyn(Border.BackgroundProperty, "B.AccentStrong");
		pill.Child = Ui.Txt("999 徽章", 11.0).Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
		row.Children.Add(pill);
		return row;
	}

	/// <summary>【Windows 源】<c>:113-143</c>:色板 24 块(键名顺序照抄;每块 64×30、圆角 6、描边 1)。</summary>
	private static Control Swatches()
	{
		WrapPanel wrap = new WrapPanel();
		string[] keys = new string[24]
		{
			"B.Accent", "B.AccentStrong", "B.AccentText", "B.AccentLite", "B.AccentSoft", "B.AccentSoft2",
			"B.AccentBorder", "B.Paper", "B.Paper2", "B.Side", "B.Line", "B.LineSoft",
			"B.Hover", "B.Ink", "B.InkSoft", "B.InkMuted", "B.InkFaint", "B.Danger",
			"B.DangerSoft", "B.Warn", "B.WarnSoft", "B.Info", "B.InfoSoft", "B.Ok"
		};
		foreach (string key in keys)
		{
			Border box = new Border
			{
				Width = 64.0,
				Height = 30.0,
				CornerRadius = new CornerRadius(6.0),
				BorderThickness = new Thickness(1.0)
			};
			// 【Windows 源】<c>:132-135</c>:Application.Current.TryFindResource(key) is Brush b —— 查不到就**不设**背景。
			if (Palette.TryGet(key, Palette.Current, out object? value) && value is IBrush brush)
			{
				box.Background = brush;
			}
			box.Dyn(Border.BorderBrushProperty, "B.Line");
			TextBlock name = Ui.Small(key.Substring(2)).Center();
			name.MaxWidth = 68.0;
			name.TextTrimming = TextTrimming.CharacterEllipsis;
			wrap.Children.Add(Ui.V(3.0, box, name));
		}
		return wrap;
	}

	/// <summary>【Windows 源】<c>:145-156</c>:七个按钮(主要 / 常规 / 柔和 / 幽灵 / 危险 / 标签 / 图标)。</summary>
	private static Control BtnRow()
	{
		return Ui.H(8.0,
			Ui.Btn("主要", BtnKind.Primary, null, "\ue768"),
			Ui.Btn("常规", BtnKind.Normal),
			Ui.Btn("柔和", BtnKind.Soft),
			Ui.Btn("幽灵", BtnKind.Ghost),
			Ui.Btn("危险", BtnKind.Danger),
			Ui.Btn("标签", BtnKind.Chip),
			Ui.IconBtn("\ue712", "图标")
		);
	}

	/// <summary>【Windows 源】<c>:158-165</c>:输入与选择(输入框 / 下拉 / 复选 / 开关 / 未选 / 滑杆 / 进度条)。</summary>
	private static Control InputRows()
	{
		return Ui.V(10.0,
			Ui.H(10.0, Ui.Input("输入占位"), Ui.Combo(new string[3] { "选项一", "选项二", "选项三" }, null, 120.0),
				Ui.Check("复选", value: true), Ui.Switch(value: true), Ui.Check("未选")),
			Ui.H(10.0, Ui.Sld(0.0, 100.0, 40.0).MinW(160.0), Ui.Prog(60.0).MinW(160.0))
		);
	}

	/// <summary>【Windows 源】<c>:167-176</c>:标签与徽章(四个配色 + 一个徽章)。</summary>
	private static Control TagRow()
	{
		return Ui.H(8.0,
			Ui.Tag("标签"),
			Ui.Tag("强调", "B.OnAccentStrong", "B.AccentSoft"),
			Ui.Tag("警示", "B.Warn", "B.WarnSoft"),
			Ui.Tag("危险", "B.Danger", "B.DangerSoft"),
			Ui.Badge("Badge")
		);
	}

	/// <summary>【Windows 源】<c>:178-188</c>:卡片解剖(Card / RowCard / Pane 三格 + 两个 Field,再套一层 Section)。</summary>
	private static Control CardSamples()
	{
		Grid grid = Ui.G(null, "*,*,*");
		grid.Add(Ui.Card(Ui.V(6.0, Ui.H2("Card 内距12"), Ui.Txt("圆角10 描边1px", 12.0)), 12.0), 0);
		grid.Add(Ui.RowCard(Ui.H(8.0, Ui.Glyph("\ue80a", 14.0, "B.Accent"), Ui.V(1.0, Ui.Txt("RowCard 内距10"), Ui.Small("圆角8 可点")).Stretch())), 0, 1);
		grid.Add(Ui.Pane(Ui.V(6.0, Ui.Txt("Pane 内距10", 13.0, bold: true), Ui.Small("圆角8 柔底")), "B.Paper2", 8.0, 10.0), 0, 2);
		Grid fields = Ui.G(null, "*,*");
		fields.Add(Ui.Field("字段标签", Ui.Input("值"), "hint 12 号"), 0);
		fields.Add(Ui.Field("带开关", Ui.Switch(value: true)), 0, 1);
		return Ui.V(10.0, Ui.Section("Section 标题", "副题 12 号"), grid, fields);
	}

	/// <summary>【Windows 源】<c>:190-195</c>:空态与骨架(两个空态块 + 一根 14 高的骨架条)。</summary>
	private static Control EmptyRow()
	{
		SPanel row = Ui.H(16.0, Ui.Empty("\ue909", "暂无内容", "图标28 标题13 提示12"), Ui.Empty("\ue99a", "AI 未就绪"));
		row.Children.Add(Ui.V(6.0, Ui.Small("Skeleton 高14:"), Ui.Skeleton(14.0, 220.0)));
		return row;
	}
}

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TipCL.Services;

namespace TipCL.Pages;

// 设计样张页（WP1 交付物）：仅 --shot design-sheet / --design-sheet 可达，不进默认导航。
// 渲染 docs/design-spec.md 的全部令牌与组件，作为逐页修复的视觉基准。
public sealed class DesignSheetPage : PageBase
{
	public override string Title => I18n.L("设计样张");

	protected override Task LoadAsync()
	{
		base.Content = ScrollBody(Sheet());
		return Task.CompletedTask;
	}

	private static UIElement Sheet()
	{
		return Ui.V(10.0,
			Card("间距栅格 4px {4,6,8,10,12,16,20,24}", SpacingRow()),
			Card("字号阶梯 {11,12,13,15,17,20} 禁止>20", TypeLadder()),
			Card("圆角 {6 控件,8 小卡,10 卡片} 999 仅徽章", RadiusRow()),
			Card("色板（当前主题）", Swatches()),
			Card("按钮", BtnRow()),
			Card("输入与选择", InputRows()),
			Card("标签与徽章", TagRow()),
			Card("卡片解剖 Card12/RowCard10/Field/Section", CardSamples()),
			Card("空态与骨架", EmptyRow())
		);
	}

	private static Border Card(string title, UIElement content)
	{
		return Ui.Card(Ui.V(8.0, Ui.Muted(title), content), 12.0);
	}

	private static UIElement SpacingRow()
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
			bar.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
			row.Children.Add(Ui.V(4.0, bar, Ui.Small(((int)v).ToString()).Center()));
		}
		return row;
	}

	private static UIElement TypeLadder()
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

	private static UIElement RadiusRow()
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
			box.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
			box.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
			row.Children.Add(Ui.V(4.0, box, Ui.Small(labels[i]).Center()));
		}
		Border pill = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(12.0, 4.0, 12.0, 5.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		pill.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
		pill.Child = Ui.Txt("999 徽章", 11.0).Dyn(TextBlock.ForegroundProperty, "B.OnAccentStrong");
		row.Children.Add(pill);
		return row;
	}

	private static UIElement Swatches()
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
			if (Application.Current.TryFindResource(key) is Brush b)
			{
				box.Background = b;
			}
			box.SetResourceReference(Border.BorderBrushProperty, "B.Line");
			TextBlock name = Ui.Small(key.Substring(2)).Center();
			name.MaxWidth = 68.0;
			name.TextTrimming = TextTrimming.CharacterEllipsis;
			wrap.Children.Add(Ui.V(3.0, box, name));
		}
		return wrap;
	}

	private static UIElement BtnRow()
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

	private static UIElement InputRows()
	{
		return Ui.V(10.0,
			Ui.H(10.0, Ui.Input("输入占位"), Ui.Combo(new string[3] { "选项一", "选项二", "选项三" }, null, 120.0),
				Ui.Check("复选", value: true), Ui.Switch(value: true), Ui.Check("未选")),
			Ui.H(10.0, Ui.Sld(0.0, 100.0, 40.0).MinW(160.0), Ui.Prog(60.0).MinW(160.0))
		);
	}

	private static UIElement TagRow()
	{
		return Ui.H(8.0,
			Ui.Tag("标签"),
			Ui.Tag("强调", "B.OnAccentStrong", "B.AccentSoft"),
			Ui.Tag("警示", "B.Warn", "B.WarnSoft"),
			Ui.Tag("危险", "B.Danger", "B.DangerSoft"),
			Ui.Badge("Badge")
		);
	}

	private static UIElement CardSamples()
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

	private static UIElement EmptyRow()
	{
		SPanel row = Ui.H(16.0, Ui.Empty("\ue909", "暂无内容", "图标28 标题13 提示12"), Ui.Empty("\ue99a", "AI 未就绪"));
		row.Children.Add(Ui.V(6.0, Ui.Small("Skeleton 高14："), Ui.Skeleton(14.0, 220.0)));
		return row;
	}
}

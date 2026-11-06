using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

// 实例库：3 列实例卡片网格（窄窗 2 列），搜索实时过滤；
// 数据源与版本管理页一致（get_instances / default_instance），缺的字段（Mod 数、最近游玩）不造假、直接省略。
public sealed class LibraryPage : PageBase
{
	private readonly TextBox _search = Ui.Input(I18n.L("搜索实例…"));

	private readonly WrapPanel _grid = new WrapPanel();

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("还没有实例"), I18n.L("点右上「新建实例」创建一个，或到「版本管理」添加已有文件夹"));

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private string _defaultInstance = "";

	public override string Title => I18n.L("实例库");

	public LibraryPage()
	{
		_search.Width = 220.0;
		_search.Height = 32.0;
		_search.TextChanged += delegate
		{
			RenderCards();
		};
		Button newBtn = Ui.Btn(I18n.L("新建实例"), BtnKind.Primary, delegate
		{
			PageBase.Run(CreateAsync, I18n.L("创建失败"));
		}, "\ue710");
		newBtn.Height = 32.0;
		newBtn.FontSize = 13.0;
		Grid head = Ui.G(null, "*,Auto,Auto");
		head.Add(Ui.H1(I18n.L("实例库")).VCenter(), 0);
		head.Add(_search.VCenter().M(12.0, 0.0, 10.0, 0.0), 0, 1);
		head.Add(newBtn.VCenter(), 0, 2);
		_empty.Show(shown: false);
		_grid.Show(shown: false);
		_grid.HorizontalAlignment = HorizontalAlignment.Stretch;
		SPanel body = Ui.V(12.0, head, _grid, _empty);
		base.Content = PageBase.ScrollBody(body);
		base.SizeChanged += delegate
		{
			LayoutCards();
		};
	}

	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_defaultInstance = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "");
		RenderCards();
	}

	public override async Task RefreshAsync()
	{
		await LoadAsync();
	}

	private void RenderCards()
	{
		_grid.Children.Clear();
		string q = (_search.Text?.Trim() ?? "");
		IEnumerable<InstanceInfo> list = _instances.Where(delegate(InstanceInfo i)
		{
			if (q.Length == 0)
			{
				return true;
			}
			return i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
		});
		int count = 0;
		foreach (InstanceInfo i in list)
		{
			_grid.Children.Add(MakeCard(i));
			count++;
		}
		_empty.Show(count == 0);
		_grid.Show(count > 0);
		LayoutCards();
	}

	// 内容宽 900 时 3 列（gap 10）；内容区窄于约 684（窗口≈720）改 2 列
	private void LayoutCards()
	{
		double w = base.ActualWidth - 36.0;
		if (w <= 0.0 || _grid.Children.Count == 0)
		{
			return;
		}
		int cols = ((w < 684.0) ? 2 : 3);
		double cw = Math.Floor((w - 10.0 * (double)(cols - 1)) / cols);
		foreach (object child in _grid.Children)
		{
			if (child is Border c)
			{
				c.Width = cw;
			}
		}
	}

	private UIElement MakeCard(InstanceInfo i)
	{
		bool isCurrent = (i.Name == _defaultInstance);
		SPanel row1 = Ui.H(6.0);
		if (isCurrent)
		{
			Ellipse dot = new Ellipse
			{
				Width = 7.0,
				Height = 7.0
			};
			dot.SetResourceReference(Shape.FillProperty, "B.Accent");
			dot.VerticalAlignment = VerticalAlignment.Center;
			row1.Children.Add(dot);
		}
		row1.Children.Add(Ui.Txt(i.Name, 14.0, bold: true, "B.Ink").Trim().VCenter());
		// 行2：诚实数据——整合包名 / MC 版本 / 版本数 / Java，缺什么省什么
		List<string> meta = new List<string>();
		if (!string.IsNullOrEmpty(i.Pack))
		{
			meta.Add(i.Pack);
		}
		if (!string.IsNullOrEmpty(i.Mc))
		{
			meta.Add("Minecraft " + i.Mc);
		}
		if (i.Versions > 0)
		{
			meta.Add(I18n.L("{0} 个版本", i.Versions));
		}
		string java = (string.IsNullOrEmpty(i.JavaLabel) ? i.Java : i.JavaLabel);
		if (!string.IsNullOrEmpty(java))
		{
			meta.Add("Java " + java);
		}
		TextBlock row2 = Ui.Txt(string.Join(" · ", meta), 11.5, bold: false, "B.InkMuted").Trim();
		Button start = Ui.Btn(I18n.L("启动"), BtnKind.Soft, null, "\ue768");
		start.Height = 26.0;
		start.FontSize = 12.0;
		start.Padding = new Thickness(10.0, 2.0, 11.0, 3.0);
		string instName = i.Name;
		start.Click += delegate
		{
			if (PageBase.Win?.GetPage("launch") is LaunchPage lp)
			{
				PageBase.Run(() => lp.LaunchNowAsync(instName));
			}
		};
		Button manage = Ui.Btn(I18n.L("管理"), BtnKind.Chip, delegate
		{
			PageBase.Win?.Navigate("instance");
		});
		manage.Height = 26.0;
		manage.FontSize = 12.0;
		manage.Padding = new Thickness(10.0, 2.0, 11.0, 3.0);
		SPanel row4 = Ui.H(8.0, start, manage);
		SPanel cardBody = Ui.V(8.0, row1, row2, row4);
		Border card = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(14.0, 12.0, 14.0, 12.0),
			Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
			VerticalAlignment = VerticalAlignment.Top,
			Child = cardBody
		};
		card.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		card.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		return card;
	}

	private async Task CreateAsync()
	{
		string? name = (await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称（会创建独立的 .minecraft）"), "", I18n.L("例如：1.20.1 生存")))?.Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		await PageBase.Api.CallAsync("create_instance", new
		{
			name = name
		});
		PageBase.Toast(I18n.L("已创建"), name, ToastKind.Success);
		await LoadAsync();
	}
}

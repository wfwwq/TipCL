using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Core.Launcher.Ai;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C8a:**AI 工具审批卡**(= Windows <c>Pages\ConfirmCard.cs</c>,271 行)—— 就是当初 C2c1 写错文件名的那一个;
/// 它依赖 <see cref="AiIcons"/> / <see cref="AiUiText"/> / <see cref="MdView"/> / <c>ToolMeta</c>,
/// 所以必须和这批基础设施一起落。
///
/// 形状照抄:标题行(警示图标 + 「助手要执行一个操作,需要你确认」)→ 操作行(风险图标 + 标签 + 风险徽章)
/// → 危险类提示 / 预览框 / 理由 → 动作行(允许 / 本对话都允许 / 范围下拉 / 状态文案 / 拒绝)。
/// 风险映射(`AiUiText.RiskOf` + 四色 + 四个图标)、二次确认防重(`_done`)、`Resolved` 事件、
/// <see cref="AllowAlways(BridgeEvent)"/> 的白名单判据 —— **全部逐字照抄**。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><b>卡片投影</b>:WPF 用 <c>DropShadowEffect</c>(<c>Effect</c> 属性),Avalonia 11 **没有特效管线**
///   ⇒ 用 <c>Border.BoxShadow</c> 表达同一件事:<c>0 4 18 0 #1A000000</c>(= 偏移 4、模糊 18、黑、透明度 0.1),
///   与 Windows 的四个参数逐值对应(**观感一致,机制不同**);</item>
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference</c> → <c>Dyn</c>;颜色键逐值相同;</item>
/// <item><c>ScrollViewer.VerticalScrollBarVisibility/HorizontalScrollBarVisibility</c> 在 Avalonia 里就是
///   <c>ScrollViewer</c> 自己的属性(不需要附加属性),语义相同 ⇒ 照写;</item>
/// <item><c>Ui.Tag(label, fg, bg)</c> / <c>Ui.Btn</c> / <c>Ui.Combo</c> / <c>Ui.G(...).Add(...)</c> 与 Windows 同形;</item>
/// <item>预览框的行着色(删=红底、增=绿底、<c>@@</c>=弱色、其它=软色)与 <c>Alpha=40</c>、<c>Take(40)</c>、
///   「…还有 N 行」、<c>MaxHeight 180</c> **逐值相同</b>。</item>
/// </list>
/// </summary>
internal sealed class ConfirmCard : Border
{
	private readonly Action<bool, bool, string> _answer;

	private bool _done;

	internal AiRisk Risk { get; }

	internal bool Answered => _done;

	internal event Action<bool>? Resolved;

	public ConfirmCard(string label, string detail, bool allowAlways, Action<bool, bool, string> answer)
		: this("", label, detail, new List<string>(), allowAlways, answer)
	{
	}

	public ConfirmCard(string tool, string label, string reason, List<string> preview, bool allowAlways, Action<bool, bool, string> answer)
	{
		ConfirmCard card = this;
		_answer = answer;
		string sideEffect = "";
		try
		{
			if (!string.IsNullOrEmpty(tool) && ToolMeta.Map.TryGetValue(tool, out ToolMetaInfo? info) && info != null)
			{
				sideEffect = info.side_effect;
			}
		}
		catch
		{
		}
		Risk = AiUiText.RiskOf(tool, sideEffect);
		string accent = Risk switch
		{
			AiRisk.Delete => "B.Danger",
			AiRisk.Write => "B.Warn",
			AiRisk.Network => "B.Info",
			_ => "B.InkMuted",
		};
		string soft = Risk switch
		{
			AiRisk.Delete => "B.DangerSoft",
			AiRisk.Write => "B.WarnSoft",
			AiRisk.Network => "B.InfoSoft",
			_ => "B.Hover",
		};
		string iconData = Risk switch
		{
			AiRisk.Delete => AiIcons.Trash,
			AiRisk.Network => AiIcons.Globe,
			_ => tool.StartsWith("write_", StringComparison.Ordinal)
				? AiIcons.Pen
				: (tool.StartsWith("install_", StringComparison.Ordinal) ? AiIcons.Box : AiIcons.Alert),
		};
		Grid title = Ui.G(null, "Auto,*");
		title.Add(AiIcons.Icon(AiIcons.Alert, 16.0, (Risk == AiRisk.Delete) ? "B.Danger" : "B.Warn").VCenter(), 0);
		title.Add(Ui.H(6.0, Ui.Txt(I18n.L("助手要执行一个操作,需要你确认"), 13.0, bold: true, "B.Ink").VCenter()).M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border head = new Border
		{
			Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
			Child = title,
		};
		head.Dyn(Border.BackgroundProperty, (Risk == AiRisk.Delete) ? "B.DangerSoft" : "B.WarnSoft");
		Border badge = Ui.Tag(RiskLabel(Risk), accent, soft);
		TextBlock labelText = Ui.Txt(label, 13.0, bold: true, "B.Ink").Wrap();
		Grid opRow = Ui.G(null, "Auto,*,Auto");
		opRow.Add(AiIcons.Icon(iconData, 15.0, accent).M(0.0, 2.0), 0);
		opRow.Add(labelText.M(9.0, 0.0, 10.0), 0, 1);
		opRow.Add(badge.VCenter(), 0, 2);
		SPanel detail = Ui.V(6.0, opRow);
		if (Risk == AiRisk.Delete)
		{
			detail.Children.Add(Ui.Txt(I18n.L("删除类操作:如果这一轮没有检查点,删掉后无法一键撤回"), 12.0, bold: false, "B.Danger").Wrap().M(24.0, 0.0, 0.0, 0.0));
		}
		List<string> lines = preview.Where((string l) => l != null).ToList();
		if (lines.Count > 0)
		{
			detail.Children.Add(PreviewBox(lines).M(24.0, 2.0));
		}
		if (!string.IsNullOrWhiteSpace(reason))
		{
			detail.Children.Add(Ui.Txt(reason.Trim(), 12.0, bold: false, "B.InkMuted").Wrap().M(24.0, 0.0, 0.0, 0.0));
		}
		Border body = new Border
		{
			Padding = new Thickness(14.0, 11.0, 14.0, 11.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = detail,
		};
		body.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		Button yes = Ui.Btn(I18n.L("允许"), BtnKind.Primary, null, "\ue73e");
		Button always = Ui.Btn(I18n.L("本对话都允许"));
		ComboBox scope = Ui.Combo(new string[2]
		{
			I18n.L("仅当前实例"),
			I18n.L("所有实例"),
		}, null, 110.0);
		scope.SelectedIndex = 0;
		Button no = Ui.Btn(I18n.L("拒绝"), BtnKind.Ghost);
		ToolTip.SetTip(always, I18n.L("同类操作以后不再询问"));
		always.IsVisible = allowAlways;
		scope.IsVisible = allowAlways;
		TextBlock state = Ui.Txt("", 12.0, bold: false, "B.InkMuted");
		Grid actions = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		actions.Add(yes, 0);
		actions.Add(always.M(8.0, 0.0, 0.0, 0.0), 0, 1);
		actions.Add(scope.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		actions.Add(state.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 3);
		actions.Add(no, 0, 4);
		Border footer = new Border
		{
			Padding = new Thickness(14.0, 9.0, 14.0, 10.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = actions,
		};
		footer.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		footer.Dyn(Border.BackgroundProperty, "B.Paper2");
		base.CornerRadius = new CornerRadius(10.0);
		base.BorderThickness = new Thickness(1.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.MaxWidth = 760.0;
		base.ClipToBounds = true;
		this.Dyn(Border.BackgroundProperty, "B.Paper");
		this.Dyn(Border.BorderBrushProperty, (Risk == AiRisk.Delete) ? "B.Danger" : "B.Warn");
		// 【Avalonia 差异】WPF DropShadowEffect(BlurRadius 18 / ShadowDepth 4 / Opacity 0.1 / Black)
		// ⇒ BoxShadow "0 4 18 0 #1A000000"(偏移 4、模糊 18、黑、alpha 0.1),四个参数逐值对应。
		base.BoxShadow = BoxShadows.Parse("0 4 18 0 #1A000000");
		base.Child = Ui.V(0.0, head, body, footer);
		yes.Click += delegate
		{
			Done(ok: true, remember: false);
		};
		always.Click += delegate
		{
			Done(ok: true, remember: true);
		};
		no.Click += delegate
		{
			Done(ok: false, remember: false);
		};
		void Done(bool ok, bool remember)
		{
			if (!card._done)
			{
				card._done = true;
				yes.IsEnabled = false;
				always.IsEnabled = false;
				no.IsEnabled = false;
				scope.IsEnabled = false;
				state.Text = ((!ok) ? I18n.L("已拒绝") : (remember ? I18n.L("已记住并允许") : I18n.L("已允许")));
				card._answer(ok, remember, (scope.SelectedIndex == 1) ? "global" : "instance");
				card.Resolved?.Invoke(ok);
			}
		}
	}

	/// <summary>【Windows 源】<c>:174-182</c>:外部(比如整张对话被撤回时)把这张卡判为拒绝。</summary>
	internal void Reject()
	{
		if (!_done)
		{
			_done = true;
			_answer(arg1: false, arg2: false, "instance");
			this.Resolved?.Invoke(false);
		}
	}

	/// <summary>【Windows 源】<c>:184-193</c>:风险徽章的文案(走 <c>I18n.L</c>)。</summary>
	private static string RiskLabel(AiRisk r)
	{
		return r switch
		{
			AiRisk.Delete => I18n.L("删除"),
			AiRisk.Write => I18n.L("写入"),
			AiRisk.Network => I18n.L("联网"),
			_ => I18n.L("只读"),
		};
	}

	/// <summary>【Windows 源】<c>:195-253</c>:预览框(最多 40 行 + 溢出提示 + 最高 180 的可滚动区)。</summary>
	private static Control PreviewBox(List<string> lines)
	{
		bool diff = AiUiText.LooksLikeDiff(lines);
		SPanel host = Ui.V(0.0);
		foreach (string line in lines.Take(40))
		{
			char kind = (diff ? AiUiText.DiffKind(line) : ' ');
			TextBlock text = new TextBlock
			{
				Text = line,
				FontFamily = MdView.Mono,
				FontSize = 11.5,
				TextWrapping = TextWrapping.Wrap,
				Padding = new Thickness(10.0, 2.0, 10.0, 2.0),
			};
			Border row = new Border
			{
				Child = text,
			};
			switch (kind)
			{
				case '-':
					row.Background = new SolidColorBrush(Color.FromArgb(40, 214, 69, 69));
					text.Dyn(TextBlock.ForegroundProperty, "B.Danger");
					break;
				case '+':
					row.Background = new SolidColorBrush(Color.FromArgb(40, 46, 155, 107));
					text.Dyn(TextBlock.ForegroundProperty, "B.AccentText");
					break;
				case '@':
					text.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
					break;
				default:
					text.Dyn(TextBlock.ForegroundProperty, "B.InkSoft");
					break;
			}
			host.Children.Add(row);
		}
		if (lines.Count > 40)
		{
			host.Children.Add(Ui.Txt(I18n.L("...还有 {0} 行", lines.Count - 40), 11.5, bold: false, "B.InkMuted").M(10.0, 2.0, 0.0, 2.0));
		}
		ScrollViewer scroll = new ScrollViewer
		{
			Content = host,
			MaxHeight = 180.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};
		Border box = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(0.0, 4.0, 0.0, 4.0),
			Child = scroll,
			ClipToBounds = true,
		};
		box.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		box.Dyn(Border.BackgroundProperty, "B.Paper2");
		return box;
	}

	/// <summary>
	/// 【Windows 源】<c>:255-270</c>:事件里说允许就一直允许吗?(`allow_always` 显式为布尔就用它,
	/// 否则按事件名白名单:删除实例 / 删模组 / 计划审批 = 不允许"以后不再问")。
	/// </summary>
	public static bool AllowAlways(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("allow_always", out JsonElement value) &&
			(value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		switch (ev.Name)
		{
			case "delete_instance":
			case "delete_mod":
			case "plan_approval":
				return false;
			default:
				return true;
		}
	}
}

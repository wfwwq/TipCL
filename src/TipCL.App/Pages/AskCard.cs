using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 提问卡(= Windows <c>Pages\AskCard.cs</c>,73 行)—— 标题 + 若干 <see cref="AskBlock"/> + 「确定」/「跳过」。
/// 确定:逐个 <c>Collect</c>,**任一为空 ⇒ 吐「还没选」警告并中止**(不提交);全齐 ⇒ 冻结控件 + 追加「已提交」小字 + 回调字典。
/// 跳过:冻结 + 「已跳过」+ 回调 <c>null</c>。冻结后选项与两个按钮都不可用(Windows <c>Freeze</c> 的语义)。
///
/// 与 Windows 的差异:`Visibility` → `IsVisible`;颜色键(`B.AccentSoft`/`B.Accent`)、圆角 10、内边距
/// (12,10,12,11)、`MaxWidth 760`、左对齐与 Windows **逐值相同**;`Ui`/`AppServices.Toast` 走既有口径。
/// </summary>
internal sealed class AskCard : Border
{
	private readonly List<AskBlock> _blocks = new List<AskBlock>();

	public AskCard(string title, JsonElement questions, Action<object?> submit)
	{
		AskCard card = this;
		SPanel body = Ui.V(10.0);
		if (!string.IsNullOrWhiteSpace(title))
		{
			body.Children.Add(Ui.Txt(title, 13.0, bold: true).Wrap());
		}
		foreach (JsonElement item in questions.EnumerateArray())
		{
			AskBlock block = new AskBlock(item);
			_blocks.Add(block);
			body.Children.Add(block.Root);
		}
		Button ok = Ui.Btn(I18n.L("确定"), BtnKind.Primary, null, "\ue73e");
		Button skip = Ui.Btn(I18n.L("跳过"));
		body.Children.Add(Ui.H(8.0, ok, skip));
		ok.Click += delegate
		{
			Dictionary<string, object> answers = new Dictionary<string, object>();
			foreach (AskBlock block in card._blocks)
			{
				object? value = block.Collect();
				if (value == null)
				{
					AppServices.Toast(I18n.L("还没选"), block.Prompt, ToastKind.Warning);
					return;
				}
				answers[block.Id] = value;
			}
			Freeze(I18n.L("已提交"));
			submit(answers);
		};
		skip.Click += delegate
		{
			Freeze(I18n.L("已跳过"));
			submit(null);
		};
		base.CornerRadius = new CornerRadius(10.0);
		base.Padding = new Thickness(12.0, 10.0, 12.0, 11.0);
		base.BorderThickness = new Thickness(1.0);
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Left;
		base.Child = body;
		this.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		this.Dyn(Border.BorderBrushProperty, "B.Accent");

		void Freeze(string note)
		{
			ok.IsEnabled = false;
			skip.IsEnabled = false;
			foreach (AskBlock block in card._blocks)
			{
				block.Root.IsEnabled = false;
			}
			body.Children.Add(Ui.Small(note));
		}
	}
}

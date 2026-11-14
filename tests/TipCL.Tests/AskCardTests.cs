using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.Pages;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// `AskCard` / `AskBlock` 的用例(`4216793` 落地时只带了草稿:3 过 2 红,那两条因为"找动作按钮 / enabled 语义"
/// 的**测试侧写法**问题被删掉,从未落地;本文件是那两条的重写 —— 按 mac 既有测试基础设施的口径)。
///
/// 重写口径(D-115:驱动内部处理器体、**不模拟输入设备**):
/// <list type="bullet">
/// <item>**找动作按钮**:`Ui.Btn` 给了图标时 `Content` 是图文面板(不是字符串)⇒ 一律走
///   `AutomationProperties.Name`(`Ui.cs:313-318` 那条路径),也就是既有的 <see cref="C3bFiles.ByText"/>;</item>
/// <item>**"已冻结"的证据 = 读 `IsEnabled`**,不是"再点一次没反应":用例的点击走
///   `Button.ClickEvent` 的 `RaiseEvent`(`C3bFiles.Click`),它**绕过** `IsEnabled` 的输入路由 ⇒
///   拿"再点一次没触发"当判据会假绿(这条正是草稿当年踩的坑之一);</item>
/// <item>`Collect` 的形状**逐字照 <see cref="AskBlock"/>**:`{picked:[{id,label}...], other:"..."}`,
///   两边都空 ⇒ `null`(上层的「还没选」提示据此)。</item>
/// </list>
/// </summary>
public class AskCardTests
{
	/// <summary>两个问题:q1 多选(红 / 蓝 / 其它)+ q2 单选(简单 / 困难)—— 与 `AskBlock` 的两个分支同构。</summary>
	private const string TwoQuestions =
		"[" +
		"{\"id\":\"q1\",\"prompt\":\"想穿哪个颜色\",\"allow_multiple\":true," +
		"\"options\":[{\"id\":\"red\",\"label\":\"红\"},{\"id\":\"blue\",\"label\":\"蓝\"},{\"id\":\"other\",\"label\":\"其它\"}]}," +
		"{\"id\":\"q2\",\"prompt\":\"选一个难度\"," +
		"\"options\":[{\"id\":\"easy\",\"label\":\"简单\"},{\"id\":\"hard\",\"label\":\"困难\"}]}" +
		"]";

	private static AskCard Build(out List<object?> submitted)
	{
		List<object?> collected = new List<object?>();
		using JsonDocument doc = JsonDocument.Parse(TwoQuestions);
		AskCard card = new AskCard("选择", doc.RootElement, delegate(object? value)
		{
			collected.Add(value);
		});
		submitted = collected;
		return card;
	}

	/// <summary>动作按钮(「确定」/「跳过」):带图标 ⇒ 按自动化名认。</summary>
	private static Button ActionButton(Control card, string text)
	{
		return C3bFiles.ByText(card, I18n.L(text));
	}

	/// <summary>某个选项开关所在的问题块(最近的 SPanel 祖先 = `AskBlock.Root`)。</summary>
	private static SPanel BlockRoot(Control toggle)
	{
		return toggle.GetLogicalAncestors().OfType<SPanel>().First();
	}

	/// <summary>
	/// **多选 + 收集 + 冻结**(草稿里红掉的那条之一):多选块的开关是 `CheckBox`、单选块是同一组的 `RadioButton`;
	/// 「其它」那个开关负责显隐输入框;**确定** ⇒ 每个问题都 `Collect` 成 `{picked, other}` 交给回调,
	/// 然后整卡冻结(两个按钮 + 每个问题块)+ 追加「已提交」。
	/// </summary>
	[Fact]
	public void Multi_choice_collects_every_pick_and_freezes_the_card()
	{
		AvaloniaSession.Run(() =>
		{
			AskCard card = Build(out List<object?> submitted);
			List<CheckBox> boxes = card.GetLogicalDescendants().OfType<CheckBox>().ToList();
			List<RadioButton> radios = card.GetLogicalDescendants().OfType<RadioButton>().ToList();
			// 推荐控件类型即"多选 / 单选"的判据(AskBlock 就是这么分的)
			Assert.Equal(3, boxes.Count);
			Assert.Equal(2, radios.Count);
			Assert.Single(radios.Select(r => r.GroupName).Distinct());
			Assert.False(string.IsNullOrEmpty(radios[0].GroupName));
			// 多选块的标题带「(可多选)」,单选块不带
			Assert.True(C4Files.HasText(card, "想穿哪个颜色" + I18n.L("(可多选)")));
			Assert.True(C4Files.HasText(card, "选一个难度"));

			// 每个问题块各带一个「其它」输入框(两个问题 ⇒ 两个框),而显隐是**按块**联动的
			List<TextBox> others = card.GetLogicalDescendants().OfType<TextBox>().ToList();
			Assert.Equal(2, others.Count);
			TextBox other = others[0];
			Assert.False(other.IsVisible);
			Assert.False(others[1].IsVisible);

			boxes[0].IsChecked = true;
			boxes[1].IsChecked = true;
			boxes[2].IsChecked = true;
			Assert.True(other.IsVisible);
			Assert.False(others[1].IsVisible, "另一个问题块的输入框不该被带着显出来(SyncOther 是每块自己的)");
			other.Text = "紫色";
			// 取消「其它」⇒ 输入框收回去(SyncOther 是双向的)
			boxes[2].IsChecked = false;
			Assert.False(other.IsVisible);
			boxes[2].IsChecked = true;
			radios[0].IsChecked = true;

			C3bFiles.Click(ActionButton(card, "确定"));

			Assert.Single(submitted);
			Dictionary<string, object> answers = Assert.IsType<Dictionary<string, object>>(submitted[0]);
			Dictionary<string, object> q1 = Assert.IsType<Dictionary<string, object>>(answers["q1"]);
			List<Dictionary<string, object>> picked = Assert.IsType<List<Dictionary<string, object>>>(q1["picked"]);
			Assert.Equal(new[] { "red", "blue", "other" }, picked.Select(p => (string)p["id"]).ToArray());
			Assert.Equal(new[] { "红", "蓝", "其它" }, picked.Select(p => (string)p["label"]).ToArray());
			Assert.Equal("紫色", q1["other"]);
			Dictionary<string, object> q2 = Assert.IsType<Dictionary<string, object>>(answers["q2"]);
			Assert.Equal(new[] { "easy" },
				Assert.IsType<List<Dictionary<string, object>>>(q2["picked"]).Select(p => (string)p["id"]).ToArray());
			Assert.Equal("", q2["other"]);

			// 冻结:两个动作按钮自己不可用(而且对输入路由是**有效不可用**),每个问题块也被关掉,并追加「已提交」
			Button ok = ActionButton(card, "确定");
			Button skip = ActionButton(card, "跳过");
			Assert.False(ok.IsEnabled);
			Assert.False(skip.IsEnabled);
			Assert.False(ok.IsEffectivelyEnabled);
			Assert.False(skip.IsEffectivelyEnabled);
			Assert.True(C4Files.HasText(card, I18n.L("已提交")));
			foreach (SPanel block in card.GetLogicalDescendants().OfType<SPanel>().Where(BlockOfQuestions))
			{
				Assert.False(block.IsEnabled);
			}

			Assert.False(other.IsEffectivelyEnabled);
		});
	}

	/// <summary>
	/// **跳过**(草稿里红掉的另一条):回调收到 `null`、追加「已跳过」(不是「已提交」)、整卡冻结。
	/// </summary>
	[Fact]
	public void Skip_freezes_the_card_and_reports_a_null_answer()
	{
		AvaloniaSession.Run(() =>
		{
			AskCard card = Build(out List<object?> submitted);
			List<CheckBox> boxes = card.GetLogicalDescendants().OfType<CheckBox>().ToList();

			C3bFiles.Click(ActionButton(card, "跳过"));

			Assert.Single(submitted);
			Assert.Null(submitted[0]);
			Assert.True(C4Files.HasText(card, I18n.L("已跳过")));
			Assert.False(C4Files.HasText(card, I18n.L("已提交")));
			Assert.False(ActionButton(card, "确定").IsEnabled);
			Assert.False(ActionButton(card, "跳过").IsEnabled);
			Assert.False(BlockRoot(boxes[0]).IsEnabled);
			Assert.False(BlockRoot(boxes[0]).IsEffectivelyEnabled);
		});
	}

	/// <summary>
	/// **一个都没选就确定** ⇒ 吐「还没选」(正文是那个问题块的 `Prompt`)+ **不提交、不冻结**(整卡还能用)。
	/// </summary>
	[Fact]
	public void Submitting_with_nothing_picked_toasts_and_leaves_the_card_usable()
	{
		AvaloniaSession.Run(() =>
		{
			using C3cToasts toasts = new C3cToasts();
			AskCard card = Build(out List<object?> submitted);

			C3bFiles.Click(ActionButton(card, "确定"));

			Assert.Empty(submitted);
			Assert.Single(toasts.Items);
			Assert.Equal(I18n.L("还没选"), toasts.Items[0].Title);
			// 正文是 AskBlock.Prompt(不带「(可多选)」那个渲染后缀)
			Assert.Equal("想穿哪个颜色", toasts.Items[0].Body);
			Assert.Equal(ToastKind.Warning, toasts.Items[0].Kind);
			Assert.False(C4Files.HasText(card, I18n.L("已提交")));
			Assert.True(ActionButton(card, "确定").IsEnabled);
			Assert.True(ActionButton(card, "跳过").IsEnabled);
		});
	}

	/// <summary>问题块 = 含有选项开关的那个 SPanel(`AskBlock.Root`);卡自己的 body 里没有开关。</summary>
	private static bool BlockOfQuestions(SPanel panel)
	{
		return panel.Children.OfType<Control>().Any(c => c is CheckBox || c is RadioButton);
	}
}

using System;
using TipCL.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C6b:Windows 版 <c>Pages\FeedbackPage.cs</c>(8399 字节 / 264 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「反馈与帮助」页(不是导航页;它是设置页分段 6 内嵌的子页,见 <see cref="C6bSegmentRegistration"/> 的注入缝)。
///
/// 形状(逐条照抄 Windows):左栏 = 提交反馈卡(类型 / 标题 / 正文 / 联系方式 / 附带系统信息 / 附件 / 三个动作)
/// + 「我提交过的」历史卡;右栏(固定 360 宽)= 常见问题卡(搜索框 + 可展开的文章按钮)。
///
/// 数据源与调用名一字未改:<c>feedback_history</c> / <c>help_articles</c> / <c>help_article</c> /
/// <c>submit_feedback</c> / <c>collect_sysinfo</c> / <c>sysinfo_text</c> / <c>stash_upload</c>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>body.Visibility = Collapsed / Visible</c> → <c>body.IsVisible = false / true</c>
///   (判断展开态也读 <c>IsVisible</c>,与 Windows 读 <c>Visibility == Visible</c> 同义);</item>
/// <item><c>HorizontalContentAlignment</c> 在 Avalonia 的同名属性上照写(文章标题按钮要左对齐);</item>
/// <item>附件读取与 base64 编码是 <c>System.IO</c> / <c>Convert</c> 的,两平台同名同义 —— **逐字保留**,
///   而且它只在用户真的选了文件之后跑(离屏/测试里选择器返回 null,进不来);</item>
/// <item>局部变量名照 Windows 的 <c>button2</c> / <c>array3</c> 那套反编译器风格**改成语义名**(判据与顺序一字不改)。</item>
/// </list>
/// </summary>
public sealed class FeedbackPage : PageBase
{
	/// <summary>【Windows 源】<c>:15-21</c>:四个反馈类型(顺序即下拉顺序)。</summary>
	private static readonly string[] Categories = new string[4]
	{
		I18n.L("问题反馈"),
		I18n.L("功能建议"),
		I18n.L("崩溃报告"),
		I18n.L("其它"),
	};

	private readonly ComboBox _cat = Ui.Combo(Categories);

	private readonly TextBox _title = Ui.Input(I18n.L("一句话说清问题"));

	private readonly TextBox _body = Ui.Multi(I18n.L("详细描述：怎么触发的、期望是什么"), "", 130.0);

	private readonly TextBox _contact = Ui.Input(I18n.L("联系方式（可空）"));

	private readonly CheckBox _sys = Ui.Check(I18n.L("附带系统信息（显卡 / 内存 / Java）"), value: true);

	private readonly SPanel _history = Ui.V(6.0);

	private readonly SPanel _articles = Ui.V(6.0);

	private readonly SPanel _attachList = Ui.V(4.0);

	private readonly TextBox _search = Ui.Input(I18n.L("搜索帮助文章"));

	private readonly List<string> _attachments = new List<string>();

	/// <summary>【Windows 源】<c>:43</c>:标题「反馈与帮助」。</summary>
	public override string Title => I18n.L("反馈与帮助");

	/// <summary>【Windows 源】<c>:45-75</c>:左栏两张卡 + 右栏 360 宽的文章卡,两个区块的 <c>BlockHost("feedback")</c>。</summary>
	public FeedbackPage()
	{
		Button submit = Ui.Btn(I18n.L("提交反馈"), BtnKind.Primary, delegate
		{
			PageBase.Run(SubmitAsync);
		}, "\ue724");
		Button sysInfo = Ui.Btn(I18n.L("查看系统信息"), BtnKind.Chip, delegate
		{
			PageBase.Run(SysInfoAsync);
		}, "\ue946");
		Button attach = Ui.Btn(I18n.L("添加附件"), BtnKind.Chip, delegate
		{
			PageBase.Run(AttachAsync, I18n.L("附件没能上传"));
		}, "\ue8b5");
		Border form = Ui.Card(Ui.V(10.0, Ui.Section(I18n.L("提交反馈"), I18n.L("会发到开发者的反馈服务，不含账号密码")), Ui.Field(I18n.L("类型"), _cat, null, 74.0), Ui.Field(I18n.L("标题"), _title, null, 74.0), _body, Ui.Field(I18n.L("联系方式"), _contact, null, 74.0), _sys, _attachList, Ui.H(8.0, submit, attach, sysInfo)));
		Border history = Ui.Card(Ui.V(10.0, Ui.Section(I18n.L("我提交过的")), _history));
		_search.TextChanged += delegate
		{
			PageBase.Run(() => LoadArticlesAsync(_search.Text));
		};
		Border articles = Ui.Card(Ui.V(10.0, Ui.Section(I18n.L("常见问题"), I18n.L("启动、Java、模组、账号、联机的快速说明（点击标题展开）")), _search, _articles));
		// 【C9a】两栏交给共用的 PageLayoutHost(Windows `FeedbackPage.cs:70`:`"feedback"` / `"side"` / `*` + `346`)。
		// 两点如实登记:①**右栏声明值保留 mac 现状 360**(Windows 是 346;C3/C5 期间按观感适配过)——
		// 保留现状才能"默认渲染逐字节不变",346 与 360 的差异登记为 **D2 真机对照项**;
		// ②间距传 0:切前是 `"*,360"`(两列、无间隔带),而右卡自带 14px 左外边距 ⇒ 传 0 保像素。
		PageLayoutHost grid = new PageLayoutHost("feedback", "side", new GridLength(1.0, GridUnitType.Star), new GridLength(360.0), gap: 0.0);
		grid.SetLeft(Ui.V(14.0, form, history));
		grid.SetRight(articles.M(14.0, 0.0, 0.0, 0.0).VTop());
		// 页面布局自定义(WP-U3):左右双栏 Grid 保持一整块(纵向流会拆散两侧布局),默认与旧 ScrollBody 等价
		Content = new BlockHost("feedback", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("说明"), Content = Ui.Section(I18n.L("反馈与帮助"), I18n.L("遇到问题先看帮助，解决不了再提反馈")) },
			new BlockSpec { Id = "content", Title = I18n.L("反馈与帮助"), Content = grid },
		}).Root;
	}

	/// <summary>【Windows 源】<c>:77-81</c>:首屏 = 历史 + 文章(空查询)。</summary>
	protected override async Task LoadAsync()
	{
		await LoadHistoryAsync();
		await LoadArticlesAsync("");
	}

	/// <summary>【Windows 源】<c>:83-99</c>:历史最多 20 条;状态留空就显示「已提交」。</summary>
	private async Task LoadHistoryAsync()
	{
		List<FeedbackRow> rows = (await PageBase.Api.TryCallAsync("feedback_history", null, new List<FeedbackRow>())) ?? new List<FeedbackRow>();
		_history.Children.Clear();
		if (rows.Count == 0)
		{
			_history.Children.Add(Ui.Muted(I18n.L("还没有提交记录")));
			return;
		}

		foreach (FeedbackRow row in rows.Take(20))
		{
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(2.0, Ui.Txt(row.Title, 12.5).Trim(), Ui.Small(row.Category + " · " + row.Time)).VCenter(), 0);
			grid.Add(Ui.Tag(string.IsNullOrWhiteSpace(row.Status) ? I18n.L("已提交") : row.Status).VCenter(), 0, 1);
			_history.Children.Add(grid);
		}
	}

	/// <summary>【Windows 源】<c>:101-127</c>:文章最多 30 条;正文默认收起,点标题才拉全文。</summary>
	private async Task LoadArticlesAsync(string query)
	{
		List<HelpArticle> rows = (await PageBase.Api.TryCallAsync("help_articles", new
		{
			query = (query ?? "")
		}, new List<HelpArticle>())) ?? new List<HelpArticle>();
		_articles.Children.Clear();
		if (rows.Count == 0)
		{
			_articles.Children.Add(Ui.Muted(I18n.L("没有匹配的文章")));
			return;
		}

		foreach (HelpArticle row in rows.Take(30))
		{
			HelpArticle article = row;
			TextBlock body = Ui.Muted("").Wrap().M(8.0, 0.0, 8.0, 4.0);
			body.IsVisible = false;
			Button toggle = Ui.Btn(row.Title, BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ToggleArticleAsync(article, body));
			});
			toggle.HorizontalContentAlignment = HorizontalAlignment.Left;
			toggle.Padding = new Avalonia.Thickness(8.0, 6.0, 8.0, 7.0);
			_articles.Children.Add(Ui.V(2.0, toggle.Stretch(), body));
		}

		Motion.Stagger(_articles, 14.0, 180.0, 6.0);
	}

	/// <summary>【Windows 源】<c>:129-149</c>:再点一次收起;没正文时才去问 <c>help_article</c>。</summary>
	private async Task ToggleArticleAsync(HelpArticle article, TextBlock body)
	{
		if (body.IsVisible)
		{
			body.IsVisible = false;
			return;
		}

		if (string.IsNullOrEmpty(article.Body))
		{
			HelpArticle? full = await PageBase.Api.TryCallAsync<HelpArticle>("help_article", new
			{
				article_id = article.Id
			});
			if (full != null)
			{
				article.Body = full.Body;
			}
		}

		body.Text = (string.IsNullOrEmpty(article.Body) ? I18n.L("暂无内容") : article.Body);
		body.IsVisible = true;
	}

	/// <summary>【Windows 源】<c>:151-163</c>:收集系统信息 → 后端出文案 → 一张 640 宽的提示卡。</summary>
	private async Task SysInfoAsync()
	{
		using (Dlg.Busy(I18n.L("正在收集系统信息...")))
		{
			object? info = await PageBase.Api.TryCallAsync<object>("collect_sysinfo", new
			{
				force = true,
				scan_system_java = true
			});
			string body = (await PageBase.Api.TryCallAsync("sysinfo_text", new { info }, "")) ?? "";
			await Dlg.Alert(I18n.L("系统信息"), body, 640.0);
		}
	}

	/// <summary>【Windows 源】<c>:165-206</c>:附件逐个读成 base64 交给 <c>stash_upload</c>;读不动的文件跳过并提示。</summary>
	private async Task AttachAsync()
	{
		string[]? files = Dlg.PickFiles(I18n.L("截图 / 日志 (*.png;*.jpg;*.log;*.txt)|*.png;*.jpg;*.log;*.txt|全部文件|*.*"), I18n.L("添加附件"));
		if (files == null || files.Length == 0)
		{
			return;
		}

		using (Dlg.Busy(I18n.L("正在上传附件...")))
		{
			foreach (string file in files)
			{
				string name = Path.GetFileName(file);
				string data;
				try
				{
					data = await Task.Run(() => Convert.ToBase64String(File.ReadAllBytes(file)));
				}
				catch (Exception ex)
				{
					PageBase.Toast(I18n.L("读不了这个文件"), I18n.L("{0}：{1}", name, ex.Message), ToastKind.Warning);
					continue;
				}

				if (data.Length == 0)
				{
					PageBase.Toast(I18n.L("跳过空文件"), name, ToastKind.Warning);
					continue;
				}

				string stored = await PageBase.Api.CallAsync<string>("stash_upload", new
				{
					name,
					data
				});
				if (!string.IsNullOrEmpty(stored))
				{
					_attachments.Add(stored);
				}
			}
		}

		RenderAttachments();
	}

	/// <summary>【Windows 源】<c>:208-228</c>:附件列表(「已附 N 个文件」+ 每行文件名与「移除」)。</summary>
	private void RenderAttachments()
	{
		_attachList.Children.Clear();
		if (_attachments.Count == 0)
		{
			return;
		}

		_attachList.Children.Add(Ui.Small(I18n.L("已附 {0} 个文件", _attachments.Count)));
		foreach (string attachment in _attachments)
		{
			string path = attachment;
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.Small(Path.GetFileName(path)).Trim().VCenter(), 0);
			grid.Add(Ui.Btn(I18n.L("移除"), BtnKind.Ghost, delegate
			{
				_attachments.Remove(path);
				RenderAttachments();
			}).VCenter(), 0, 1);
			_attachList.Children.Add(grid);
		}
	}

	/// <summary>【Windows 源】<c>:230-263</c>:标题为空只提示 + 抖一下;成功才清空表单并重拉历史。</summary>
	private async Task SubmitAsync()
	{
		if (string.IsNullOrWhiteSpace(_title.Text))
		{
			PageBase.Toast(I18n.L("标题不能为空"), "", ToastKind.Warning);
			Motion.Shake(_title);
			return;
		}

		string body = _body.Text ?? "";
		if (_attachments.Count > 0)
		{
			body = body.TrimEnd() + I18n.L("\n\n附件:\n") + string.Join("\n", _attachments);
		}

		using (Dlg.Busy(I18n.L("正在提交...")))
		{
			OpResult? result = await PageBase.Api.TryCallAsync<OpResult>("submit_feedback", new
			{
				category = _cat.Str(),
				title = _title.Text.Trim(),
				body,
				contact = (_contact.Text?.Trim() ?? ""),
				include_sysinfo = (_sys.IsChecked == true)
			});
			bool ok = result != null && result.Ok;
			PageBase.Toast(ok ? I18n.L("已提交") : I18n.L("提交失败"), result?.Message ?? "", ok ? ToastKind.Success : ToastKind.Error);
			if (ok)
			{
				_title.Clear();
				_body.Clear();
				_attachments.Clear();
				RenderAttachments();
				await LoadHistoryAsync();
			}
		}
	}
}

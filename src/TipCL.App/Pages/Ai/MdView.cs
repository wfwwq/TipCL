using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.Services;

namespace TipCL.Pages.Ai;

/// <summary>
/// C8a:Markdown 渲染视图(= Windows <c>Pages\Ai\MdView.cs</c>,441 行,<c>: RichTextBox</c>)。
///
/// <b>为什么不能照字面搬</b>:Windows 那份是 WPF 的 <c>RichTextBox</c>,内容建在 <c>FlowDocument</c> 上
/// (<c>Paragraph</c> / <c>Section</c> / <c>Table</c> / <c>Run</c> / <c>Hyperlink</c> / <c>BlockUIContainer</c>),
/// 而 **Avalonia 11 没有 <c>FlowDocument</c> / <c>Paragraph</c> / <c>RichTextBox</c>**。
/// 所以这里按 Lead 批准的方案做**块级渲染器**:把 <see cref="MdParser"/> 已经产出的结构
/// (**八类块** + **七类行内**)逐类画成控件。**只映射解析器已有的结构,不加新语法、不加新功能**。
///
/// 公开面**一字不改**(下游 <c>Bubble</c> / <c>ConfirmCard</c> 的调用点因此不用动):
/// <see cref="Markdown"/>、<see cref="SetMarkdown"/>、<see cref="Mono"/>、以及它是个可放进 <c>Ui.V(...)</c> 的控件。
///
/// 逐类保真度(与 WPF <c>FlowDocument</c> 比;括号内标注**是否用户可见**):
/// <list type="number">
/// <item><b>Paragraph / Heading</b>:<c>TextBlock</c>(<c>TextWrapping=Wrap</c>)+ <c>Inlines</c>;字号 18/16/14.5/13.5、
///   SemiBold、外边距与 Windows 逐值相同(**观感一致**)。</item>
/// <item><b>Bullet / Ordered</b>:每项一行 <c>Grid("Auto,*")</c> —— 左列是标记(<c>•</c> / <c>◦</c> / <c>N.</c>,色 <c>B.InkMuted</c>),
///   右列是内容。<b>差异</b>:WPF 用 <c>TextIndent=-16</c> + <c>Padding=16</c> 做"悬挂标记",这里用两列 Grid 达到**同样的悬挂效果**
///   (机制不同、观感一致);缩进口径 <c>4 + depth*18</c> 与 Windows 相同(**可见但一致**)。</item>
/// <item><b>Quote</b>:左边一条 3px 的 <c>B.Line</c> 竖线 + <c>Padding(10,2,0,2)</c> + 前景 <c>B.InkSoft</c>,内部子块**递归**渲染
///   (**观感一致**)。</item>
/// <item><b>Rule</b>:WPF 是"空段落 + 下边框 + FontSize 2";这里是一条 <b>1px 高</b>的 <c>Border</c>(色 <c>B.Line</c>),
///   外边距 <c>(0,6,0,10)</c> 相同(**观感一致**,机制不同)。</item>
/// <item><b>Code</b>:<see cref="CodeBlock(string,string)"/>(块)+ 行内码用 <c>Run</c>(等宽、底色 <c>B.Hover</c>、字色 <c>B.AccentText</c>)
///   —— 与 Windows 逐值相同(**观感一致**)。</item>
/// <item><b>Table</b>:WPF 的 <c>Table</c>/<c>TableCell</c> 在 Avalonia 没有对等物 ⇒ 用 <c>Grid</c> 拼:列宽 <c>Auto</c>(与 Windows 的
///   <c>TableColumn.Width = Auto</c> 同口径)、每格一个 <c>Border</c>(下边框 1px <c>B.LineSoft</c>、内边距 <c>10,6,14,6</c>)、
///   表头行底色 <c>B.Paper2</c> + SemiBold + <c>B.InkMuted</c>、每格按 <c>Align</c> 左/中/右(**可见且一致**);
///   <b>差异</b>:WPF <c>Table</c> 还支持跨行跨列与边框合并 —— **这套 Markdown 不产出那些**,所以**用户不可见**。</item>
/// <item><b>行内 bold / italic / strike</b>:<c>Bold</c> / <c>Italic</c> / <c>Span + TextDecorations.Strikethrough</c>
///   (**观感一致**)。</item>
/// <item><b>行内 Link</b>:WPF 用 <c>Hyperlink</c>(<c>:365-383</c>,可点、手型、悬停显示 url、点击走
///   <c>Ui.OpenUrl</c> 且**只开 http/https**)。Avalonia 的行内文本不单独收指针事件 ⇒ 这里用
///   <c>InlineUIContainer</c> 包一个可点的小控件(下划线 + <c>B.AccentText</c> + 手型 + ToolTip=url + 同样的
///   <c>Ui.OpenUrl</c> 与 **只开 http/https** 的判据)。**用户可见**:观感与行为一致,机制不同。</item>
/// <item><b>主题切换的"实时性"</b>:WPF 用 <c>SetResourceReference</c> 让文本元素随主题实时变色;Avalonia 的同类机制
///   (<c>Dyn</c>)只作用于 <c>Control</c>,而 <c>Run</c>/<c>Span</c> 不是 <c>Control</c> ⇒ 这里在
///   <b>主题变体变化时重渲染一次</b>达到同样的用户可见效果(**可见但一致**)。</item>
/// <item><b>滚轮</b>:WPF 那段手工转发(<c>:57-69</c>)是把滚轮交给外层的聊天滚动区;Avalonia 的滚轮事件
///   **默认冒泡**到外层 <c>ScrollViewer</c>,所以不需要转发(**观感一致,代码更短**)。</item>
/// <item><b>模板</b>:WPF 的 <c>BuildTemplate()</c>(<c>:72-81</c>)只是把 <c>RichTextBox</c> 换成"不滚动的
///   <c>PART_ContentHost</c>";这里自建视觉树,不需要模板(**用户不可见**)。</item>
/// <item><b>可选中文本 / 富文本编辑</b>:WPF <c>RichTextBox</c> 天生可以选中/复制(只读时也能选),Avalonia 的
///   <c>TextBlock</c> **默认不能选中** —— 这是**唯一一处用户可见的能力差**;代码块的「复制」按钮不受影响
///   (它复制原文),聊天正文如需选中请报我另开一条(涉及 <c>SelectableTextBlock</c>)。</item>
/// </list>
/// </summary>
internal sealed class MdView : Border
{
	/// <summary>
	/// 等宽字体(WPF 是 <c>"Cascadia Mono, Consolas, Microsoft YaHei UI"</c>)。
	/// mac 侧按 GOAL-32 的等宽串取 <c>Menlo, Consolas, monospace</c>(**这是设计差异,不是漏件**)。
	/// </summary>
	internal static readonly FontFamily Mono = new FontFamily("Menlo, Consolas, monospace");

	private readonly SPanel _host = Ui.V(0.0);

	private string _text = "";

	private string _rendered = "";

	private readonly DispatcherTimer _throttle;

	private bool _pendingLive;

	/// <summary>原始 Markdown(Windows 同名属性)。</summary>
	public string Markdown => _text;

	public MdView()
	{
		base.BorderThickness = new Thickness(0.0);
		base.Background = Brushes.Transparent;
		base.Padding = new Thickness(0.0);
		base.Focusable = true;
		base.Child = _host;
		// 打字机式流式输出时的节流(Windows:80ms / Background 优先级)
		_throttle = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(80.0),
		};
		_throttle.Tick += delegate
		{
			_throttle.Stop();
			Render(_pendingLive);
		};
		// 【Avalonia 差异】文本元素不是 Control ⇒ Dyn 用不上;主题变体一变就重渲染一次(用户可见效果等同)
		if (Application.Current != null)
		{
			Application.Current.ActualThemeVariantChanged += delegate
			{
				_rendered = "";
				Render(live: false);
			};
		}
	}

	/// <summary>【Windows 源】<c>:83-99</c>:设置 Markdown(Live 时按 80ms 节流,否则立即渲染)。</summary>
	public void SetMarkdown(string? text, bool live = false)
	{
		_text = text ?? "";
		if (live)
		{
			_pendingLive = true;
			if (!_throttle.IsEnabled)
			{
				_throttle.Start();
			}
		}
		else
		{
			_throttle.Stop();
			Render(live: false);
		}
	}

	/// <summary>【Windows 源】<c>:101-144</c>:把 Markdown 解析成块序列后重建视觉树(解析失败时退回纯段落)。</summary>
	private void Render(bool live)
	{
		if (_rendered == _text)
		{
			return;
		}
		_rendered = _text;
		_host.Children.Clear();
		List<MdBlock> blocks;
		try
		{
			blocks = MdParser.Parse(_text);
		}
		catch
		{
			blocks = new List<MdBlock>
			{
				new MdBlock
				{
					Kind = MdBlockKind.Paragraph,
					Inlines = new List<MdInline>
					{
						new MdInline
						{
							Text = _text,
						},
					},
				},
			};
		}
		for (int i = 0; i < blocks.Count; i++)
		{
			Control? block = BuildBlock(blocks[i], 0);
			if (block != null)
			{
				// 最后一块不要下外边距(与 Windows 同一条规则)
				if (i == blocks.Count - 1)
				{
					block.Margin = new Thickness(block.Margin.Left, block.Margin.Top, block.Margin.Right, 0.0);
				}
				_host.Children.Add(block);
			}
		}
	}

	/// <summary>【Windows 源】<c>:146-242</c>:一个块 → 一个控件(八类逐条对应)。</summary>
	private Control? BuildBlock(MdBlock b, int depth)
	{
		switch (b.Kind)
		{
			case MdBlockKind.Heading:
			{
				TextBlock head = NewText(bold: true);
				head.Margin = new Thickness(0.0, (depth == 0) ? 6 : 2, 0.0, 6.0);
				head.FontWeight = FontWeight.SemiBold;
				head.FontSize = b.Level switch
				{
					1 => 18.0,
					2 => 16.0,
					3 => 14.5,
					_ => 13.5,
				};
				AddInlines(head.Inlines!, b.Inlines);
				return head;
			}
			case MdBlockKind.Paragraph:
			{
				TextBlock para = NewText(bold: false);
				para.Margin = new Thickness(0.0, 0.0, 0.0, 8.0);
				AddInlines(para.Inlines!, b.Inlines);
				return para;
			}
			case MdBlockKind.Bullet:
			case MdBlockKind.Ordered:
			{
				SPanel list = Ui.V(0.0);
				list.Margin = new Thickness(0.0, 0.0, 0.0, 8.0);
				int start = b.Start;
				for (int i = 0; i < b.Items.Count; i++)
				{
					int level = ((i < b.ItemDepth.Count) ? b.ItemDepth[i] : 0);
					string marker = ((b.Kind == MdBlockKind.Ordered && level == 0)
						? (start++ + ".")
						: ((level == 0) ? "•" : "◦")) + " ";
					TextBlock bullet = Ui.Txt(marker, 13.0, bold: false, "B.InkMuted");
					bullet.VerticalAlignment = VerticalAlignment.Top;
					TextBlock content = NewText(bold: false);
					AddInlines(content.Inlines!, b.Items[i]);
					Grid row = Ui.G(null, "Auto,*");
					row.Margin = new Thickness(4 + level * 18, 1.0, 0.0, 1.0);
					row.Add(bullet, 0);
					row.Add(content, 0, 1);
					list.Children.Add(row);
				}
				return list;
			}
			case MdBlockKind.Quote:
			{
				SPanel inner = Ui.V(0.0);
				foreach (MdBlock child in b.Children)
				{
					Control? built = BuildBlock(child, depth + 1);
					if (built != null)
					{
						inner.Children.Add(built);
					}
				}
				Border quote = new Border
				{
					BorderThickness = new Thickness(3.0, 0.0, 0.0, 0.0),
					Padding = new Thickness(10.0, 2.0, 0.0, 2.0),
					Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
					Child = inner,
				};
				quote.Dyn(Border.BorderBrushProperty, "B.Line");
				inner.Dyn(TextBlock.ForegroundProperty, "B.InkSoft");
				return quote;
			}
			case MdBlockKind.Rule:
			{
				Border rule = new Border
				{
					Height = 1.0,
					Margin = new Thickness(0.0, 6.0, 0.0, 10.0),
					HorizontalAlignment = HorizontalAlignment.Stretch,
				};
				rule.Dyn(Border.BackgroundProperty, "B.Line");
				return rule;
			}
			case MdBlockKind.Code:
			{
				Control code = CodeBlock(b.Raw, b.Lang);
				code.Margin = new Thickness(0.0, 2.0, 0.0, 10.0);
				return code;
			}
			case MdBlockKind.Table:
				return BuildTable(b);
			default:
				return null;
		}
	}

	/// <summary>【Windows 源】<c>:244-279</c>:表格(列宽 Auto、表头底色、逐格下边框)。</summary>
	private Grid BuildTable(MdBlock b)
	{
		int columns = Math.Max(1, b.Header.Count);
		Grid table = new Grid
		{
			Margin = new Thickness(0.0, 2.0, 0.0, 10.0),
			ColumnDefinitions = new ColumnDefinitions(string.Join(",", new string[columns].Select(_ => "Auto"))),
			HorizontalAlignment = HorizontalAlignment.Left,
		};
		int rowIndex = 0;
		// 表头行
		for (int j = 0; j < columns; j++)
		{
			Control cell = Cell((j < b.Header.Count) ? b.Header[j] : new List<MdInline>(), header: true, Align(b, j));
			table.Add(cell, rowIndex, j);
		}
		table.Dyn(Border.BackgroundProperty, "B.Paper2");
		rowIndex++;
		foreach (List<List<MdInline>> row in b.Rows)
		{
			for (int k = 0; k < columns; k++)
			{
				Control cell = Cell((k < row.Count) ? row[k] : new List<MdInline>(), header: false, Align(b, k));
				table.Add(cell, rowIndex, k);
			}
			rowIndex++;
		}
		return table;
	}

	/// <summary>【Windows 源】<c>:281-293</c>:该列的对齐(默认左)。</summary>
	private static TextAlignment Align(MdBlock b, int i)
	{
		string align = ((i < b.Align.Count) ? b.Align[i] : "left");
		if (align == "center")
		{
			return TextAlignment.Center;
		}
		if (!(align == "right"))
		{
			return TextAlignment.Left;
		}
		return TextAlignment.Right;
	}

	/// <summary>【Windows 源】<c>:295-313</c>:一个表格单元(内边距 10,6,14,6 + 下边框 1px;表头加粗 + 色)。</summary>
	private Control Cell(List<MdInline> inlines, bool header, TextAlignment align)
	{
		TextBlock text = NewText(bold: header);
		text.TextAlignment = align;
		if (header)
		{
			text.FontWeight = FontWeight.SemiBold;
			text.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		}
		AddInlines(text.Inlines!, inlines);
		Border cell = new Border
		{
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Padding = new Thickness(10.0, 6.0, 14.0, 6.0),
			Child = text,
		};
		cell.Dyn(Border.BorderBrushProperty, "B.LineSoft");
		return cell;
	}

	/// <summary>【Windows 源】<c>:315-325</c>:把一段行内序列加进目标集合。</summary>
	private void AddInlines(InlineCollection target, List<MdInline> inlines)
	{
		foreach (MdInline inline in inlines)
		{
			Inline? built = BuildInline(inline);
			if (built != null)
			{
				target.Add(built);
			}
		}
	}

	/// <summary>【Windows 源】<c>:327-387</c>:一段行内 → 一个 <see cref="Inline"/>(七类逐条对应)。</summary>
	private Inline? BuildInline(MdInline x)
	{
		switch (x.Kind)
		{
			case MdInlineKind.Text:
				return new Run(x.Text);
			case MdInlineKind.Break:
				return new LineBreak();
			case MdInlineKind.Code:
			{
				Run run = new Run(" " + x.Text + " ")
				{
					FontFamily = Mono,
					FontSize = 12.0,
					Background = Res("B.Hover"),
					Foreground = Res("B.AccentText"),
				};
				return run;
			}
			case MdInlineKind.Bold:
			{
				Bold bold = new Bold();
				AddInlines(bold.Inlines, x.Children);
				return bold;
			}
			case MdInlineKind.Italic:
			{
				Italic italic = new Italic();
				AddInlines(italic.Inlines, x.Children);
				return italic;
			}
			case MdInlineKind.Strike:
			{
				Span span = new Span
				{
					TextDecorations = TextDecorations.Strikethrough,
				};
				AddInlines(span.Inlines, x.Children);
				return span;
			}
			case MdInlineKind.Link:
			{
				// 【Avalonia 差异】行内文本不单独收指针事件 ⇒ 用 InlineUIContainer 包一个可点的小控件;
				// 观感(下划线 + 主题色 + 手型 + 悬停显示 url)与点击行为(只开 http/https,走 Ui.OpenUrl)与 Windows 一致。
				string url = x.Url;
				TextBlock label = new TextBlock
				{
					TextDecorations = TextDecorations.Underline,
					Cursor = new Cursor(StandardCursorType.Hand),
					Foreground = Res("B.AccentText"),
				};
				AddInlines(label.Inlines!, x.Children);
				ToolTip.SetTip(label, url);
				label.PointerPressed += delegate
				{
					if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
					{
						Ui.OpenUrl(url);
					}
				};
				return new InlineUIContainer
				{
					Child = label,
				};
			}
			default:
				return null;
		}
	}

	/// <summary>新建一个可换行的 <c>TextBlock</c>(Windows 那边这些位置是 <c>Paragraph</c>)。</summary>
	private static TextBlock NewText(bool bold)
	{
		return new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			FontSize = 13.5,
			FontWeight = (bold ? FontWeight.SemiBold : FontWeight.Normal),
		};
	}

	/// <summary>
	/// 取调色板里的画刷(<c>Run</c>/<c>Span</c> 不是 <c>Control</c>,用不了 <c>Dyn</c>)。
	/// 主题变化时由构造函数里那个订阅重渲染一次,用户可见效果与 WPF 的 <c>SetResourceReference</c> 相同。
	/// </summary>
	private static IBrush Res(string key)
	{
		if (Application.Current != null && Application.Current.TryFindResource(key, out object? value) && value is IBrush brush)
		{
			return brush;
		}
		return Brushes.Transparent;
	}

	/// <summary>【Windows 源】<c>:389-440</c>:代码块(深底卡片 + 语言标签 + 「复制」)。</summary>
	internal static Control CodeBlock(string code, string lang)
	{
		TextBox box = new TextBox
		{
			Text = code,
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			FontFamily = Mono,
			FontSize = 12.0,
			TextWrapping = TextWrapping.NoWrap,
			Foreground = new SolidColorBrush(Color.FromRgb(216, 237, 227)),
			Padding = new Thickness(0.0),
			CaretBrush = Brushes.White,
		};
		// 【Avalonia 差异】WPF 的 TextBox.HorizontalScrollBarVisibility/VerticalScrollBarVisibility 是实例属性;
		// Avalonia 里这两个是 ScrollViewer 的**附加属性**,语义相同(横向出滚动条、纵向不出)。
		ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
		ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
		TextBlock langLabel = Ui.Txt(string.IsNullOrWhiteSpace(lang) ? "code" : lang, 11.0);
		langLabel.Foreground = new SolidColorBrush(Color.FromRgb(140, 176, 160));
		Button copy = new Button
		{
			Content = Ui.H(4.0,
				AiIcons.Path(AiIcons.Copy, 12.0, new SolidColorBrush(Color.FromRgb(156, 191, 174))),
				new TextBlock
				{
					Text = I18n.L("复制"),
					FontSize = 11.0,
					Foreground = new SolidColorBrush(Color.FromRgb(156, 191, 174)),
				}),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Cursor = new Cursor(StandardCursorType.Hand),
			Padding = new Thickness(4.0, 1.0, 4.0, 1.0),
			Focusable = false,
			Template = AiIcons.FlatButtonTemplate(),
		};
		copy.Click += delegate
		{
			TopLevel? top = TopLevel.GetTopLevel(copy);
			if (top?.Clipboard != null)
			{
				_ = CopyAsync(top, code);
			}
		};
		Grid head = Ui.G(null, "*,Auto")
			.Add(langLabel.VCenter(), 0)
			.Add(copy, 0, 1);
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(31, 42, 37)),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 7.0, 12.0, 10.0),
			Child = Ui.V(4.0, head, box),
		};
		return border;
	}

	/// <summary>把代码写进剪贴板(失败只吞掉这一次复制,不影响界面 —— 与 Windows 的 try/catch 同义)。</summary>
	private static async Task CopyAsync(TopLevel top, string code)
	{
		try
		{
			await top.Clipboard!.SetTextAsync(code);
			AppServices.Toast(I18n.L("已复制"), "", ToastKind.Success);
		}
		catch
		{
		}
	}
}

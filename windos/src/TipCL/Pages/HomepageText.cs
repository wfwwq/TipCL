using System.Net;
using System.Text.RegularExpressions;

namespace TipCL.Pages;

/// <summary>
/// 启动页「本地 HTML」主页的文字提取(纯逻辑,禁 WPF,net8 测试宿主可直接加载)。
/// 启动页那一栏只是文字区,不渲染网页:把 HTML 里的可读文字取出来 —— 去掉 head / script / style / 注释,
/// 块级标签换行,其余标签去掉,实体解码,合并多余空白。看着不像 HTML 的内容(纯文本文件)原样返回。
/// </summary>
public static class HomepageText
{
	private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

	private static readonly Regex LooksLikeHtml = new Regex(@"<\s*(!doctype|html|head|body|p|div|br|span|h[1-6]|ul|ol|li|table|a|b|i|strong|em|img|meta|title)\b", Opt);

	private static readonly Regex Hidden = new Regex(@"<!--.*?-->|<\s*(head|script|style|noscript|template)\b[^>]*>.*?<\s*/\s*\1\s*>", Opt | RegexOptions.Singleline);

	private static readonly Regex Breaks = new Regex(@"<\s*br\s*/?\s*>|<\s*/\s*(p|div|li|tr|h[1-6]|ul|ol|table|section|article|header|footer|blockquote|pre)\s*>", Opt);

	private static readonly Regex Tags = new Regex(@"<[^>]*>", RegexOptions.CultureInvariant);

	private static readonly Regex Spaces = new Regex(@"[ \t\f\v\u00a0]+", RegexOptions.CultureInvariant);

	private static readonly Regex BlankLines = new Regex(@"\n{3,}", RegexOptions.CultureInvariant);

	/// <summary>要显示在启动页上的文字:HTML 取可读文字,其它内容原样。</summary>
	public static string ToDisplay(string? content)
	{
		string text = content ?? "";
		if (!LooksLikeHtml.IsMatch(text))
		{
			return text;
		}
		text = Hidden.Replace(text, " ");
		text = Breaks.Replace(text, "\n");
		text = Tags.Replace(text, "");
		text = WebUtility.HtmlDecode(text).Replace("\r\n", "\n").Replace('\r', '\n');
		text = Spaces.Replace(text, " ");
		string[] lines = text.Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			lines[i] = lines[i].Trim();
		}
		return BlankLines.Replace(string.Join("\n", lines), "\n\n").Trim();
	}

	/// <summary>新闻条用的一行摘要:换行并成空格,超长截断加省略号。</summary>
	public static string OneLine(string? text, int max)
	{
		string line = Spaces.Replace((text ?? "").Replace("\r", " ").Replace('\n', ' '), " ").Trim();
		return (line.Length > max) ? (line.Substring(0, max) + "…") : line;
	}
}

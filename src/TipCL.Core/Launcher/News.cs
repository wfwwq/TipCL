// 来源: Windows 版参考仓库 src/TipCL/Core/Launcher/News.cs(R16 搬运)。
// 缓存路径、两条新闻源、行字段、12 条上限、正文 160 字截断、「取不到就回落缓存」全部照搬。
//
// 唯一有意的差异在失败处理:Windows 版在「两条源都失败且缓存为空」时把最后一个异常抛出去
// (News.cs:89-92)。Linux 侧的调用方是启动页那条 fire-and-forget 的新闻行,抛出只会把整页
// 带崩,故这里改成:失败只记日志 + 返回空列表(页面照旧显示「暂无新闻」)。同理,
// _rows_from 遇到结构不对的载荷返回空而不是抛 KeyError / TypeError。
using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher;

public static class News
{
	/// <summary>新闻源(与 Windows 版逐字一致):新版 v2 优先,老版 JSON 兜底。</summary>
	public static readonly string[] URLS = new string[2]
	{
		"https://launchercontent.mojang.com/v2/javaPatchNotes.json",
		"https://launchercontent.mojang.com/javaPatchNotes.json"
	};

	/// <summary>最多取多少条(对齐 Windows 版的 slice(None, 12, None))。</summary>
	public const int MAX_ROWS = 12;

	/// <summary>正文截断阈值(对齐 Windows 版:超过 160 字截到 160 再加省略号)。</summary>
	public const int BODY_LIMIT = 160;

	/// <summary>
	/// 缓存文件路径的**测试覆盖点**(留空 = 用默认的 &lt;ROOT&gt;/cache/news.json)。
	///
	/// 为什么要有这条缝隙:启动页那条「线上新闻」是**后台 fire-and-forget** 地调 `fetch_news`
	/// (LaunchPage.cs:885 → Api.cs:502 → <see cref="Fetch"/>),成功就会 <c>WriteCache</c> 写这个文件;
	/// 而 A 类测试(建启动页的)与缓存类用例跑在**同一个进程、同一个 ROOT** 下,于是「坏缓存必须是空」
	/// 这类断言会被那条后台抓取**偶尔**污染成非空(实测:全量跑多次里红过两次,报 5+ 条真实新闻;
	/// 单独跑缓存用例 5/5 绿)。让用例把自己的缓存指到私有文件上,这条跨用例耦合就断了。
	/// 与 <c>MdView.UrlOpener</c> 同一个套路(生产留默认、测试替换)。
	/// </summary>
	public static string CachePathOverride { get; set; } = "";

	/// <summary>缓存文件:&lt;ROOT&gt;/cache/news.json(对齐 Windows 的 PPath.Join(Utils.ROOT, "cache", "news.json"))。</summary>
	public static string CACHE => CachePathOverride.Length > 0 ? CachePathOverride : PPath.Join(Utils.ROOT, "cache", "news.json");

	/// <summary>
	/// 载荷 → 行列表。行字段与 Windows 版一致:title / body / version / image / date
	/// (启动页的渲染读 title 与 body,其余字段留给公告浮层与后续版本)。
	/// </summary>
	public static List<object> _rows_from(object? payload)
	{
		object raw = new List<object>();
		if (payload is PyDict pyDict)
		{
			raw = Py.Or(pyDict.Get("entries"), pyDict.Get("patchNotes"), new List<object>());
		}
		else if (!(payload is string))
		{
			raw = payload;
		}
		List<object> rows = new List<object>();
		if (raw is string || !(raw is IEnumerable<object>))
		{
			// Windows 版在这里抛(KeyError / TypeError):Linux 侧一律当「没有新闻」
			return rows;
		}
		int index = 0;
		foreach (object item in (IEnumerable<object>)raw)
		{
			if (index++ >= MAX_ROWS)
			{
				break;
			}
			if (!(item is PyDict row))
			{
				continue;
			}
			object title = Py.Or(Py.Or(row.Get("title"), row.Get("version")), row.Get("id"), "");
			object body = Py.Or(Py.Or(row.Get("shortText"), row.Get("body")), row.Get("subtitle"), "");
			if (body is string { Length: >BODY_LIMIT } text)
			{
				body = Py.Slice(text, 0, BODY_LIMIT) + "...";
			}
			object image = "";
			object rawImage = Py.Or(row.Get("image"), row.Get("cardBackground"), new PyDict());
			if (rawImage is PyDict imgDict)
			{
				image = Py.Or(imgDict.Get("url"), "");
			}
			else if (rawImage is string imgUrl)
			{
				image = imgUrl;
			}
			rows.Add(PyDict.Of(
				"title", Py.Str(title),
				"body", Py.Strip(Py.Str(body)),
				"version", Py.Str(Py.Or(row.Get("version"), row.Get("id"), "")),
				"image", image,
				"date", Py.Slice(Py.Str(Py.Or(row.Get("date"), row.Get("updated_at"), "")), 0, 10)));
		}
		return rows;
	}

	/// <summary>
	/// 抓新闻(在 IApi.CallCore 的线程池线程上跑,不占 UI 线程)。
	/// 顺序:两条源依次试 → 谁都拿不到就回落缓存 → 缓存也没有就返回空列表。**绝不抛**。
	/// </summary>
	public static List<object> Fetch(DownloadManager dm = null)
	{
		dm ??= new DownloadManager(2L);
		Exception error = null;
		foreach (string url in URLS)
		{
			try
			{
				List<object> rows = _rows_from(dm.FetchJson(url, 15L));
				if (rows.Count > 0)
				{
					WriteCache(rows);
					return rows;
				}
			}
			catch (Exception ex)
			{
				error = ex;
			}
		}
		List<object> cached = LoadCached();
		if (cached.Count > 0)
		{
			return cached;
		}
		if (error != null)
		{
			// 不抛:新闻行是启动页上的装饰,断网不该影响启动。原因已由下层 DownloadManager.FetchJson 记 WARNING。
			Utils.log.Info("新闻获取失败,本次显示「暂无新闻」: %s", Py.ExcStr(error));
		}
		return new List<object>();
	}

	/// <summary>读缓存;文件不存在 / JSON 坏 / 结构不对一律返回空列表(不抛)。</summary>
	public static List<object> LoadCached()
	{
		object raw;
		try
		{
			raw = Utils.ReadJson(CACHE);
		}
		catch (Exception ex)
		{
			// Utils.ReadJson 对非 UTF-8 内容会原样抛 UnicodeDecodeError
			Utils.log.Warning("新闻缓存读取失败 %s: %s", CACHE, Py.ExcStr(ex));
			return new List<object>();
		}
		if (raw is List<object> rows)
		{
			return rows;
		}
		if (raw is PyDict)
		{
			return _rows_from(raw);
		}
		return new List<object>();
	}

	/// <summary>写缓存失败只记日志:拿不到缓存下次再抓,不该让本次新闻行出错。</summary>
	private static void WriteCache(List<object> rows)
	{
		try
		{
			Utils.WriteJson(CACHE, rows);
		}
		catch (Exception ex)
		{
			Utils.log.Warning("新闻缓存写入失败 %s: %s", CACHE, Py.ExcStr(ex));
		}
	}
}
// 来源: Windows 参考件 windos/src/TipCL/Pages/PageLayoutIO.cs(冻结件 @0a304a7)+
// Mac 移植件 26097e5:src/TipCL.App/Pages/PageLayoutIO.cs(185 行,语义已与 Windows 对齐)。
//
// R32-LIN-IO:本文件原先是 v1 版(导出 = PageLayoutStore.LoadAll() + ExportJson,只有 id/hidden;
// 导入 = PageLayoutStore.ParseImport,硬性 kind=tipcl-page-layout + version=1)⇒
// 导出的文件丢 mode/cols/几何/样式/分栏/方案,导入 Windows/Mac 导出的 v2 文件必报「导入失败」,
// 导入 v1 文件在 ui_layout_v2.json 已存在时又被 v2 真源盖掉(界面零变化)。现按 SPEC §2.3 原地重写:
//   导出:LayoutStoreV2.ExportJson(LayoutStoreV2.Load()) —— 整份 v2 文档(mode/cols/x-y-w-h/
//         style/splits/shell/profiles/widgets 全在),与设置页「导出整机方案(v2)」同一格式;
//   导入:v2 文件按页整条覆盖(连同响应式变体与页里引用到的自建区块定义),旧版 v1 文件只并入
//         顺序与显隐(同一块的其它 v2 字段按 id 保留),两条路都落到 v2 真源再单向写通 v1。
//
// 结构约束(Core 是独立程序集,**不能引用 App**):Windows/Mac 版里的
//   LayoutEngine.ApplyPage / LayoutEngine.MergeV1 / LayoutEngine.WidgetIdOf
// 在这里按「逐字等价」内联成 3 个小私有方法(见文件末尾),不新增任何语义。
//
// 对话框与 Toast 仍走三个静态委托(App 侧 TipCL.App/Pages/Layout/LayoutFx.cs 启动时接线),
// 因此**工具条的两个调用点零改动**。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Layout;

namespace TipCL.Core.Layout;

/// <summary>
/// 文案门面:保留给同程序集内既有的 v1 调用点(<see cref="PageLayoutStore"/> 的两条错误消息),
/// 实现改为真正转调 <see cref="I18nCore.Tr"/> —— 不再是"键即文案"的假门面,英文界面能翻出来。
/// </summary>
public static class LayoutText
{
	public static string L(string key)
	{
		return I18nCore.Tr(key ?? "");
	}

	public static string L(string key, params object[] args)
	{
		string text = I18nCore.Tr(key ?? "");
		return args == null || args.Length == 0 ? text : Py.Fmt(text, args);
	}
}

public enum ToastKind
{
	Success,
	Error,
	Warning,
	Info
}

// 页面布局导入/导出(编辑器工具条 / 设置页方案共用):文件对话框 + Toast 反馈(经委托注入),不依赖具体 UI 框架。
// v2 是唯一真源;导入后由调用方(LayoutEditor.ImportLayout)自己 LoadFromStore + Render,所以「选完文件当场生效」。
public static class PageLayoutIO
{
	/// <summary>自建块的 id 前缀(<c>custom:&lt;widgetId&gt;</c>);与 Pages 的 LayoutEngine.CustomPrefix 逐字相同。</summary>
	private const string CustomBlockPrefix = "custom:";

	/// <summary>保存文件对话框;参数 (filter, suggestedName) → 用户选的路径,取消返回 null。</summary>
	public static Func<string, string, string?>? SaveFileDialog { get; set; }

	/// <summary>打开文件对话框;参数 (filter) → 用户选的路径,取消返回 null。</summary>
	public static Func<string, string?>? PickFileDialog { get; set; }

	/// <summary>Toast 通知;参数 (title, detail, kind)。</summary>
	public static Action<string, string, ToastKind>? Toast { get; set; }

	// 与 Windows/Mac 一致的过滤器写法(实际过滤由 App 侧 Dlg 决定,这里只保持签名与文案)
	public static string Filter => I18nCore.Tr("页面布局 (*.json)|*.json|全部文件|*.*");

	// 导出整份 v2 文档(每页的模式、位置、尺寸、样式、分栏、方案都在)到用户选择的文件;取消不提示。
	// 读的是 v2 真源(LayoutStoreV2),不经过 v1 投影的进程内缓存 ⇒ 不会导出过期内容。
	public static void ExportToFileDialog(string preferredName)
	{
		try
		{
			string? path = SaveFileDialog?.Invoke(Filter, SafeFileName(preferredName) + ".json");
			if (path == null)
			{
				return;
			}
			File.WriteAllText(path, LayoutStoreV2.ExportJson(LayoutStoreV2.Load()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			Toast?.Invoke(I18nCore.Tr("已导出"), path, ToastKind.Success);
		}
		catch (Exception ex)
		{
			Toast?.Invoke(I18nCore.Tr("导出失败"), ex.Message, ToastKind.Error);
		}
	}

	// 选文件 → MergeImport 并进当前 v2 文档 → 落盘(v2 + 写通 v1)→ 清 v1 读取缓存 → 返回导入的页数。
	// 用户取消返回 null、不提示;文件不对或一页都没有也返回 null,各给一句 Toast。
	// 写盘失败时内存里已经是导入后的布局(LayoutStoreV2.Save 的契约),所以照样返回页数,只把提示换成警告。
	public static int? ImportFromFileDialog()
	{
		int count;
		bool saved;
		try
		{
			string? path = PickFileDialog?.Invoke(Filter);
			if (path == null)
			{
				return null;
			}
			LayoutV2 doc = LayoutStoreV2.Load();
			count = MergeImport(doc, File.ReadAllText(path));
			if (count == 0)
			{
				Toast?.Invoke(I18nCore.Tr("文件里没有可导入的页面"), "", ToastKind.Warning);
				return null;
			}
			saved = LayoutStoreV2.Save(doc);
			// 清 v1 读取缓存(与 Windows :61 / Mac :80 同):Save 只写通 v1 文件,不动 PageLayoutStore
			// 的进程内快照;不刷新的话「先导出→再导入→再导出」会读到旧值(PARITY D-02 的另一半)。
			PageLayoutStore.Reload();
		}
		catch (Exception ex)
		{
			Toast?.Invoke(I18nCore.Tr("导入失败"), ex.Message, ToastKind.Error);
			return null;
		}
		if (saved)
		{
			Toast?.Invoke(Py.Fmt(I18nCore.Tr("已导入 {0} 个页面的布局"), count), "", ToastKind.Success);
		}
		else
		{
			Toast?.Invoke(Py.Fmt(I18nCore.Tr("已导入 {0} 个页面的布局"), count), I18nCore.Tr("写入布局文件失败,重启后会丢失"), ToastKind.Warning);
		}
		return count;
	}

	/// <summary>
	/// 把导入文件的内容并进 <paramref name="doc"/>(不落盘),返回并入的页数。
	/// v2 文件:按页整条覆盖(连同这些页的响应式变体);页里引用到、当前文档又没有的自建区块定义一并带上,
	/// 同 id 的保留现有定义。不是 v2 就按旧版 v1 解析(格式不对抛异常,消息是中文):
	/// 只并入顺序与显隐,同一块的其它 v2 字段按 id 保留(<see cref="LayoutStoreV2.MergeV1Order"/>)。
	/// </summary>
	public static int MergeImport(LayoutV2 doc, string text)
	{
		if (doc.Layout == null)
		{
			doc.Layout = new LayoutBody();
		}
		LayoutV2? source = LayoutV2.FromJsonText(text);
		if (source == null)
		{
			int merged = 0;
			foreach (KeyValuePair<string, PageLayoutDoc> kv in PageLayoutStore.ParseImport(text))
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
				{
					continue;
				}
				doc.Layout.Pages.TryGetValue(kv.Key, out PageLayoutV2? existing);
				ApplyPage(doc, kv.Key, LayoutStoreV2.MergeV1Order(existing, ToCoreBlocks(kv.Value)));
				merged++;
			}
			return merged;
		}
		LayoutBody from = source.Layout ?? new LayoutBody();
		LayoutBody to = doc.Layout;
		HashSet<string> widgets = new HashSet<string>(StringComparer.Ordinal);
		int count = 0;
		foreach (KeyValuePair<string, PageLayoutV2> kv in from.Pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			to.Pages[kv.Key] = kv.Value.Clone();
			CollectWidgetIds(kv.Value, widgets);
			foreach (KeyValuePair<string, LayoutBody> variant in from.Variants)
			{
				if (variant.Value?.Pages == null || !variant.Value.Pages.TryGetValue(kv.Key, out PageLayoutV2? page) || page == null)
				{
					continue;
				}
				if (!to.Variants.TryGetValue(variant.Key, out LayoutBody? target) || target == null)
				{
					target = new LayoutBody();
					to.Variants[variant.Key] = target;
				}
				target.Pages[kv.Key] = page.Clone();
				CollectWidgetIds(page, widgets);
			}
			count++;
		}
		foreach (WidgetDef def in from.Widgets)
		{
			if (def != null && widgets.Contains(def.Id) && !to.Widgets.Exists((WidgetDef w) => w != null && w.Id == def.Id))
			{
				to.Widgets.Add(def.Clone());
			}
		}
		return count;
	}

	// ————— 下面三个方法是 Pages 层 LayoutEngine 同义实现的逐字搬运(Core 不能引用 App) —————

	/// <summary>[搬运] <c>LayoutEngine.ApplyPage(doc, pageId, page)</c>:把一页写回 v2 文档(没有该页就新建;不动别的页)。</summary>
	private static void ApplyPage(LayoutV2 doc, string pageId, PageLayoutV2 page)
	{
		if (doc == null || string.IsNullOrEmpty(pageId) || page == null)
		{
			return;
		}
		if (doc.Layout == null)
		{
			doc.Layout = new LayoutBody();
		}
		doc.Layout.Pages[pageId] = page;
	}

	/// <summary>[搬运] <c>LayoutEngine.ToCoreBlocks(doc)</c>:v1 单页文档 → Core 的朴素块列表(过滤空 id,顺序即顺序)。</summary>
	private static List<LayoutV1Block> ToCoreBlocks(PageLayoutDoc? doc)
	{
		List<LayoutV1Block> blocks = new List<LayoutV1Block>();
		if (doc == null || doc.Blocks == null)
		{
			return blocks;
		}
		foreach (BlockState block in doc.Blocks)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id))
			{
				blocks.Add(new LayoutV1Block
				{
					Id = block.Id,
					Hidden = block.Hidden
				});
			}
		}
		return blocks;
	}

	/// <summary>[搬运] <c>LayoutEngine.WidgetIdOf(blockId)</c>:<c>custom:&lt;widgetId&gt;</c> → widgetId;不是自建块返回 null。</summary>
	private static string? WidgetIdOf(string? blockId)
	{
		if (string.IsNullOrEmpty(blockId) || !blockId!.StartsWith(CustomBlockPrefix, StringComparison.Ordinal))
		{
			return null;
		}
		string id = blockId.Substring(CustomBlockPrefix.Length);
		return (id.Length > 0) ? id : null;
	}

	private static void CollectWidgetIds(PageLayoutV2 page, HashSet<string> into)
	{
		if (page.Blocks == null)
		{
			return;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			string? id = WidgetIdOf(block?.Id);
			if (id != null)
			{
				into.Add(id);
			}
		}
	}

	// pageId 里可能有冒号(如 library:main),不能原样当文件名。
	//
	// 这里**不能**用 Path.GetInvalidFileNameChars():它随 OS 变 ——
	// Windows 上是 `" < > | : * ? \ /` 加 1..31 控制符,Unix 上只有 `\0` 和 `/`。
	// 用它,同一个页面 id 在 Windows 上建议 `library-main.json`、在 Linux 上建议
	// `library:main.json`,导出文件名随平台漂(ubuntu CI 就是这么红的:
	// PageLayoutIOV2Tests.ExportWritesWholeV2DocumentWithEveryField 断言 `library-main.json`)。
	// 按 Windows 真源对齐:固定用 Windows 非法字符集 ∪ POSIX 的 `/`,两端逐字一致。
	private static bool IsInvalidFileNameChar(char c)
	{
		if (c < ' ')
		{
			return true; // 0x00–0x1F:Windows 非法;控制符进文件名两端都是坑
		}
		switch (c)
		{
			case '"':
			case '<':
			case '>':
			case '|':
			case ':':
			case '*':
			case '?':
			case '\\':
			case '/':
				return true;
			default:
				return false;
		}
	}

	private static string SafeFileName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "pages";
		}
		StringBuilder sb = new StringBuilder(name!.Length);
		foreach (char c in name!)
		{
			sb.Append(IsInvalidFileNameChar(c) ? '-' : c);
		}
		return sb.ToString();
	}
}
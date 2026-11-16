using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Core.Launcher.Layout;
using TipCL.Services;

namespace TipCL.Pages;

// R32-MWG:Windows 版 Pages\PageLayoutIO.cs(WPF / net48)到 mac(Avalonia 11 / net8.0)的移植。
// 页面布局导入/导出(编辑器工具条、实例库右键菜单共用):文件对话框 + Toast 反馈,不依赖具体页面。
// 导出整份 v2 文档(每页的模式、位置、尺寸、样式都在),与设置页「导出整机方案(v2)」同一格式;
// 导入两种都认:v2 文件里的页按页整条覆盖,旧版 v1 文件只并入顺序与显隐(见 MergeImport)。
// 只改文件里有的页;页签、分栏比例这些整机设置不动,要整套换用设置页的「应用为当前布局」。
//
// Windows → mac 差异(逐条):
// 1) Toast:Windows 是 AppServices.Window?.Toast(...);mac 的 Toast 宿主是 AppServices.Toast(...)
//    (C2b 的 ToastSink,为空时降级成调试输出)⇒ 调用形状换签名,语义不变;
// 2) Dlg.SaveFile / Dlg.PickFile 签名与 Windows 一致(过滤器字符串 + 默认名 + 标题);离屏(--smoke)下
//    mac 的 Dlg 自动取消返回 null,于是走"取消不提示"分支;
// 3) 本文件从 v1-only 升级到 Windows 的 v2 语义:导出走 LayoutStoreV2.ExportJson/Load,导入走
//    MergeImport + LayoutStoreV2.Save + PageLayoutStore.Reload()(= 对齐 Windows 的公开 Reload);
//    旧 mac 版逐页 PageLayoutStore.Save 的行为被 v2 覆盖语义取代(v1 文件仍走 PageLayoutStore.ParseImport,
//    但结果经 LayoutEngine.MergeV1 并入 v2 文档,再单向写通回 v1)。
public static class PageLayoutIO
{
	// 与 ServersPage 等既有对话框一致的过滤器写法,全走 I18n
	private static string Filter => I18n.L("页面布局 (*.json)|*.json|全部文件|*.*");

	// 取消不提示;成功 Toast「已导出」,失败 Toast「导出失败」。
	public static void ExportToFileDialog(string preferredName)
	{
		try
		{
			string? path = Dlg.SaveFile(Filter, SafeFileName(preferredName) + ".json", I18n.L("导出布局"));
			if (path == null)
			{
				return;
			}
			File.WriteAllText(path, LayoutStoreV2.ExportJson(LayoutStoreV2.Load()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			AppServices.Toast(I18n.L("已导出"), path, ToastKind.Success);
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
		}
	}

	// 选文件 → MergeImport 并进当前 v2 文档 → 落盘 → 返回导入的页数。
	// 用户取消返回 null、不提示;文件不对或一页都没有也返回 null,各给一句 Toast。
	// 写盘失败时内存里已经是导入后的布局(LayoutStoreV2.Save 的契约),所以照样返回页数,只把提示换成警告。
	public static int? ImportFromFileDialog()
	{
		int count;
		bool saved;
		try
		{
			string? path = Dlg.PickFile(Filter, I18n.L("导入布局"));
			if (path == null)
			{
				return null;
			}
			LayoutV2 doc = LayoutStoreV2.Load();
			count = MergeImport(doc, File.ReadAllText(path));
			if (count == 0)
			{
				AppServices.Toast(I18n.L("文件里没有可导入的页面"), "", ToastKind.Warning);
				return null;
			}
			saved = LayoutStoreV2.Save(doc);
			PageLayoutStore.Reload();
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("导入失败"), ex.Message, ToastKind.Error);
			return null;
		}
		if (saved)
		{
			AppServices.Toast(I18n.L("已导入 {0} 个页面的布局", count), "", ToastKind.Success);
		}
		else
		{
			AppServices.Toast(I18n.L("已导入 {0} 个页面的布局", count), I18n.L("写入布局文件失败,重启后会丢失"), ToastKind.Warning);
		}
		return count;
	}

	/// <summary>
	/// 把导入文件的内容并进 <paramref name="doc"/>(不落盘),返回并入的页数。
	/// v2 文件:按页整条覆盖(连同这些页的响应式变体);页里引用到、当前文档又没有的自建区块定义一并带上,
	/// 同 id 的保留现有定义。不是 v2 就按旧版 v1 解析(格式不对抛异常,消息是中文):
	/// 只并入顺序与显隐,同一块的其它 v2 字段按 id 保留(<see cref="LayoutEngine.MergeV1"/>)。
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
				LayoutEngine.ApplyPage(doc, kv.Key, LayoutEngine.MergeV1(existing, kv.Value));
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
			if (def != null && widgets.Contains(def.Id) && !to.Widgets.Exists(w => w != null && w.Id == def.Id))
			{
				to.Widgets.Add(def.Clone());
			}
		}
		return count;
	}

	private static void CollectWidgetIds(PageLayoutV2 page, HashSet<string> into)
	{
		if (page.Blocks == null)
		{
			return;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			string? id = LayoutEngine.WidgetIdOf(block?.Id);
			if (id != null)
			{
				into.Add(id);
			}
		}
	}

	// pageId 里可能有冒号(如 library:main),不能原样当文件名
	private static string SafeFileName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return "pages";
		}
		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder sb = new StringBuilder(name!.Length);
		foreach (char c in name!)
		{
			sb.Append((Array.IndexOf(invalid, c) >= 0) ? '-' : c);
		}
		return sb.ToString();
	}
}
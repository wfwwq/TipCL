using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TipCL.Services;

namespace TipCL.Pages;

// 页面布局导入/导出：文件对话框 + Toast 反馈，不依赖具体页面。
// 导入只负责逐页落盘（PageLayoutStore.Save）；调用方拿到返回的页数后自行刷新当前页，
// 其他页面是缓存实例，下次打开时按新文档渲染。
public static class PageLayoutIO
{
	// 与 ServersPage 等既有对话框一致的过滤器写法，全走 I18n
	private static string Filter => I18n.L("页面布局 (*.json)|*.json|全部文件|*.*");

	// 导出全部页面的布局（LoadAll + 确保当前页最新状态在内）到用户选择的文件；
	// 取消不提示；成功 Toast「已导出」，失败 Toast「导出失败」。
	public static void ExportToFileDialog(string preferredName)
	{
		try
		{
			string? path = Dlg.SaveFile(Filter, (string.IsNullOrEmpty(preferredName) ? "pages" : preferredName) + ".json", I18n.L("导出布局"));
			if (path == null)
			{
				return;
			}
			Dictionary<string, PageLayoutDoc> dictionary = new Dictionary<string, PageLayoutDoc>();
			foreach (KeyValuePair<string, PageLayoutDoc> kv in PageLayoutStore.LoadAll())
			{
				dictionary[kv.Key] = kv.Value;
			}
			if (!string.IsNullOrEmpty(preferredName))
			{
				PageLayoutDoc? pageLayoutDoc = PageLayoutStore.Load(preferredName);
				if (pageLayoutDoc != null)
				{
					dictionary[preferredName] = pageLayoutDoc;
				}
			}
			File.WriteAllText(path, PageLayoutStore.ExportJson(dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			AppServices.Window?.Toast(I18n.L("已导出"), path, ToastKind.Success);
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
		}
	}

	// 选文件 → ParseImport（kind/version 不对抛异常 → Toast「导入失败」）→ 逐页 Save → 返回导入页数；
	// 用户取消返回 null，不提示。
	public static int? ImportFromFileDialog()
	{
		IReadOnlyDictionary<string, PageLayoutDoc> dictionary;
		try
		{
			string? path = Dlg.PickFile(Filter, I18n.L("导入布局"));
			if (path == null)
			{
				return null;
			}
			dictionary = PageLayoutStore.ParseImport(File.ReadAllText(path));
		}
		catch (Exception ex)
		{
			AppServices.Window?.Toast(I18n.L("导入失败"), ex.Message, ToastKind.Error);
			return null;
		}
		foreach (KeyValuePair<string, PageLayoutDoc> item in dictionary)
		{
			PageLayoutStore.Save(item.Key, item.Value);
		}
		AppServices.Window?.Toast(I18n.L("已导入 {0} 个页面的布局（下次打开对应页面生效）", dictionary.Count), "", ToastKind.Success);
		return dictionary.Count;
	}
}

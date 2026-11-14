// 来源: Windows 版参考仓库 src/TipCL/Pages/PageLayoutIO.cs + Services/I18n.cs 的 L 语义（M2-B4 搬运改造）。
// Windows 版直接依赖 Dlg / Toast（WPF 服务层）；这里把文件对话框与 Toast 抽成静态委托，
// 保持「模型层零 UI 依赖」：App 侧（TipCL.App/Pages/Layout/LayoutFx.cs）启动时用
// Avalonia StorageProvider / Toast 服务接线，测试环境不设委托则导出/导入对话框直接返回 null。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TipCL.Core.Layout;

/// <summary>极简文案门面：对齐 Windows 版 I18n.L——键即中文文案，zh 环境原样返回（带 {0} 格式化）。</summary>
public static class LayoutText
{
	public static string L(string key)
	{
		return key;
	}

	public static string L(string key, params object?[] args)
	{
		return args == null || args.Length == 0 ? key : string.Format(key, args);
	}
}

public enum ToastKind
{
	Success,
	Error,
	Warning,
	Info
}

// 页面布局导入/导出：文件对话框 + Toast 反馈（经委托注入），不依赖具体 UI 框架。
// 导入只负责逐页落盘（PageLayoutStore.Save）；调用方拿到返回的页数后自行刷新当前页，
// 其他页面是缓存实例，下次打开时按新文档渲染。
public static class PageLayoutIO
{
	/// <summary>保存文件对话框；参数 (filter, suggestedName) → 用户选的路径，取消返回 null。</summary>
	public static Func<string, string, string?>? SaveFileDialog { get; set; }

	/// <summary>打开文件对话框；参数 (filter) → 用户选的路径，取消返回 null。</summary>
	public static Func<string, string?>? PickFileDialog { get; set; }

	/// <summary>Toast 通知；参数 (title, detail, kind)。</summary>
	public static Action<string, string, ToastKind>? Toast { get; set; }

	// 与 Windows 版一致的过滤器写法，全走文案门面
	public static string Filter => LayoutText.L("页面布局 (*.json)|*.json|全部文件|*.*");

	// 导出全部页面的布局（LoadAll + 确保当前页最新状态在内）到用户选择的文件；
	// 取消不提示；成功 Toast「已导出」，失败 Toast「导出失败」。
	public static void ExportToFileDialog(string preferredName)
	{
		try
		{
			string? path = SaveFileDialog?.Invoke(Filter, (string.IsNullOrEmpty(preferredName) ? "pages" : preferredName) + ".json");
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
				PageLayoutDoc? doc = PageLayoutStore.Load(preferredName);
				if (doc != null)
				{
					dictionary[preferredName] = doc;
				}
			}
			File.WriteAllText(path, PageLayoutStore.ExportJson(dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			Toast?.Invoke(LayoutText.L("已导出"), path, ToastKind.Success);
		}
		catch (Exception ex)
		{
			Toast?.Invoke(LayoutText.L("导出失败"), ex.Message, ToastKind.Error);
		}
	}

	// 选文件 → ParseImport（kind/version 不对抛异常 → Toast「导入失败」）→ 逐页 Save → 返回导入页数；
	// 用户取消返回 null，不提示。
	public static int? ImportFromFileDialog()
	{
		try
		{
			string? path = PickFileDialog?.Invoke(Filter);
			if (path == null)
			{
				return null;
			}
			IReadOnlyDictionary<string, PageLayoutDoc> pages = PageLayoutStore.ParseImport(File.ReadAllText(path));
			foreach (KeyValuePair<string, PageLayoutDoc> item in pages)
			{
				PageLayoutStore.Save(item.Key, item.Value);
			}
			Toast?.Invoke(LayoutText.L("已导入 {0} 个页面的布局（下次打开对应页面生效）", pages.Count), "", ToastKind.Success);
			return pages.Count;
		}
		catch (Exception ex)
		{
			Toast?.Invoke(LayoutText.L("导入失败"), ex.Message, ToastKind.Error);
			return null;
		}
	}
}

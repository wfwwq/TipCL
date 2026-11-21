using System;
using System.Collections.Generic;
using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// 「设置 → 布局 → 高级」里的全局开关(v3):目前只有「网页和按钮组件」。
/// 落在 UiPrefs.Expanded(ui_prefs.json 里已有的 bool 字典)的一个专用 key 上 —— 不改 UiPrefs 的结构、
/// 也不进布局文件(导出 / 导入整套布局时不会把这个开关带给别人)。
/// 语义:开 = 编辑态「添加」抽屉里出现网页 / 按钮两种组件;关(缺省)= 不出现。
/// 已经存在于布局里的网页 / 按钮组件不受这个开关影响,仍然按各自的 enabled 设置(默认关闭)渲染。
/// </summary>
public static class LayoutSettingsPrefs
{
	/// <summary>UiPrefs.Expanded 里的 key(带前缀,避免与卡片展开状态的 key 撞名)。</summary>
	public const string DangerWidgetsKey = "layout.v3.danger_widgets";

	// 没有主窗口(单元测试 / 冒烟)时的内存值
	private static bool _fallback;

	/// <summary>开关变化(编辑态「添加」抽屉据此刷新组件列表)。</summary>
	public static event Action? Changed;

	/// <summary>是否允许在编辑态添加网页 / 按钮组件(缺省 false)。</summary>
	public static bool DangerWidgetsEnabled
	{
		get
		{
			try
			{
				MainWindow? win = AppServices.Window;
				Dictionary<string, bool>? map = win?.Prefs?.Expanded;
				if (map != null && map.TryGetValue(DangerWidgetsKey, out bool value))
				{
					return value;
				}
			}
			catch
			{
				// 读偏好失败按缺省(关闭)
			}
			return _fallback;
		}
		set
		{
			_fallback = value;
			try
			{
				MainWindow? win = AppServices.Window;
				if (win != null && win.Prefs != null)
				{
					if (win.Prefs.Expanded == null)
					{
						win.Prefs.Expanded = new Dictionary<string, bool>();
					}
					win.Prefs.Expanded[DangerWidgetsKey] = value;
					win.SaveUiPrefs();
				}
			}
			catch
			{
				// 偏好写不进去只影响下次启动,本次运行仍按内存值
			}
			try
			{
				Changed?.Invoke();
			}
			catch
			{
			}
		}
	}
}

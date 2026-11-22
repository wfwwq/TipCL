using System;

using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// 布局编辑器的全局偏好(所有页面共用),落在 UiPrefs(ui_prefs.json)里。
/// 「自动靠拢」:开 = 旧行为(网格压紧上浮 / 左右互换 / 画布磁性吸附);关 = 区块停在放下的位置。
/// v3(GOAL §5.2):界面上不再提供修改入口,编辑器固定按「开」处理;这里继续能读写(老存档里的值原样保留)。
/// </summary>
public static class LayoutEditPrefs
{
	/// <summary>
	/// 网页 / 按钮组件的全局开关(GOAL §6.4 第 3 条 / D-81 Q6)。R35 合并时两套实现只保留一套:
	/// 真源是 <see cref="LayoutSettingsPrefs.DangerWidgetsEnabled"/>(存在 UiPrefs 里,默认关),这里只是约定名字的别名。
	/// </summary>
	public static bool AllowDangerWidgets
	{
		get
		{
			return LayoutSettingsPrefs.DangerWidgetsEnabled;
		}
		set
		{
			LayoutSettingsPrefs.DangerWidgetsEnabled = value;
		}
	}

	// 没有主窗口(单元测试 / 冒烟)时的内存值
	private static bool _fallback = true;

	public static bool AutoPack
	{
		get
		{
			try
			{
				MainWindow? win = AppServices.Window;
				return (win != null) ? win.Prefs.LayoutAutoPack : _fallback;
			}
			catch
			{
				return _fallback;
			}
		}
		set
		{
			_fallback = value;
			try
			{
				MainWindow? win = AppServices.Window;
				if (win != null)
				{
					win.Prefs.LayoutAutoPack = value;
					win.SaveUiPrefs();
				}
			}
			catch
			{
				// 偏好写不进去不影响本次编辑
			}
		}
	}
}

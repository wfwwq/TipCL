using System;

using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// 布局编辑器的全局偏好(所有页面共用),落在 UiPrefs(ui_prefs.json)里。
/// 目前只有「自动靠拢」:开 = 旧行为(网格压紧上浮 / 左右互换 / 画布磁性吸附);关 = 区块停在放下的位置。
/// </summary>
public static class LayoutEditPrefs
{
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

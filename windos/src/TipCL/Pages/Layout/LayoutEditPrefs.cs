using System;

using TipCL.Services;

namespace TipCL.Pages.Layout;

/// <summary>
/// 布局编辑器的全局偏好(所有页面共用),落在 UiPrefs(ui_prefs.json)里。
///   AutoPack:「自动靠拢」:开 = 旧行为(网格压紧上浮 / 左右互换 / 画布磁性吸附);关 = 区块停在放下的位置。
///             R34 起界面上不再提供修改入口(固定为开),这里继续能读写,老偏好原样生效。
///   AllowDangerWidgets:网页 / 按钮组件的全局开关(R34 Q6,缺省关):只决定这两种组件会不会出现在
///             「添加」抽屉和组件表单里;每个组件自己的 settings.enabled 与导入时强制关掉的规则都不变。
/// </summary>
public static class LayoutEditPrefs
{
	// 没有主窗口(单元测试 / 冒烟)时的内存值
	private static bool _fallback = true;

	private static bool _dangerFallback;

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

	/// <summary>网页 / 按钮组件是否出现在「添加」抽屉和组件表单里(写法与 <see cref="AutoPack"/> 相同,存 UiPrefs.LayoutDangerWidgets)。</summary>
	public static bool AllowDangerWidgets
	{
		get
		{
			try
			{
				MainWindow? win = AppServices.Window;
				return (win != null) ? win.Prefs.LayoutDangerWidgets : _dangerFallback;
			}
			catch
			{
				return _dangerFallback;
			}
		}
		set
		{
			_dangerFallback = value;
			try
			{
				MainWindow? win = AppServices.Window;
				if (win != null)
				{
					win.Prefs.LayoutDangerWidgets = value;
					win.SaveUiPrefs();
				}
			}
			catch
			{
				// 偏好写不进去:本次运行仍按内存值生效
			}
		}
	}
}

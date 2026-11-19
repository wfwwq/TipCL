// 对齐 Windows 2026-11-19 录屏修复 #3(windos/.../Layout/LayoutEditPrefs.cs)。
// Linux 的偏好树在 TipCL.Core.Launcher.UiPrefs(config.json 的 ui_prefs 节点),不是单独的 wpf-ui.json。
using System;

using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Layout;

/// <summary>
/// 布局编辑器的全局偏好(所有页面共用),落在 config.json 的 ui_prefs.layout_auto_pack。
/// 目前只有「自动靠拢」:开 = 旧行为(网格压紧上浮 / 左右互换 / 画布磁性吸附);关 = 区块停在放下的位置。
/// </summary>
public static class LayoutEditPrefs
{
	/// <summary>没有 config(单元测试 / 冒烟宿主)时的缺省值:与旧行为一致。</summary>
	private const bool DefaultAutoPack = true;

	/// <summary>
	/// 现读现写,不在静态字段里留副本(UiPrefs 的三条硬约束之一:否则又变成「进程内记忆」)。
	/// 读失败(无 config / JSON 损坏)→ 缺省开;写失败只当本次不落盘,不影响编辑。
	/// </summary>
	public static bool AutoPack
	{
		get
		{
			try
			{
				return UiPrefs.Load().GetLayoutAutoPack(DefaultAutoPack);
			}
			catch (Exception)
			{
				return DefaultAutoPack;
			}
		}
		set
		{
			try
			{
				UiPrefs prefs = UiPrefs.Load();
				prefs.SetLayoutAutoPack(value);
				prefs.Save();
			}
			catch (Exception)
			{
				// 偏好写不进去不影响本次编辑
			}
		}
	}
}
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C4:Windows 版 <c>Pages\GlobalModsDialog.cs</c>(1956 字节 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「全局模组」对话框(所有实例共享的那份 <c>global_mods</c> 目录)。
///
/// <b>它为什么属于本包</b>:任务书点名的五个文件里,<see cref="ModsDialog"/> 的「全局模组」按钮
/// 直接调 <c>GlobalModsDialog.ShowAsync</c>(Windows <c>ModsDialog.cs:77</c>)。它在 mac 侧**不存在**,
/// 而它只有 1956 字节、只依赖已移植的 <c>Ui</c> / <c>Dlg</c> / <c>SmoothScroll</c> / <c>SPanel</c>
/// 与一个纯 DTO <see cref="GlobalModRow"/>(183 字节)—— 按任务书「小而自包含就一并搬并说明」的规定
/// **一并移植**,不另开工作包、不留静默桩。它是本包唯一一个超出「五个文件」清单的产品文件。
///
/// 形状(逐条照抄 Windows):打开目录 + 刷新两个按钮;列表每条 = 开关 + 文件名 + 大小;
/// 空目录给 <see cref="Ui.Empty"/> 的空态;开关走 <c>set_global_mod_enabled</c>(<c>enabled</c> 真/假两支)。
///
/// Windows → Avalonia 的差异:只有框架类型换名(<c>Button</c> / <c>Grid</c> / <c>CheckBox</c> 在 Avalonia
/// 同名;<c>Visibility</c> → <c>IsVisible</c>)。这里一处都没有用到 <c>Visibility</c>,所以几乎是逐行照抄。
/// </summary>
public static class GlobalModsDialog
{
	/// <summary>
	/// 弹出「全局模组」并按 <c>list_global_mods</c> 填表。
	/// 【Windows 源】<c>GlobalModsDialog.cs:11-63</c>。
	/// </summary>
	public static async Task ShowAsync()
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(6.0);
		SmoothScroll scroll = Ui.Scroll(list);
		// 【Windows 源】:380 固定高 + 520 最小宽(与 ModsDialog 的 BindWindowMaxHeight 不同,这里是裸 MaxHeight)
		scroll.MaxHeight = 380.0;
		scroll.MinWidth = 520.0;
		Button open = Ui.Btn(I18n.L("打开目录"), BtnKind.Soft, delegate
		{
			PageBase.Run(() => api.CallAsync("open_global_mods"));
		}, "\ue8b7");
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(Reload, I18n.L("读取失败"));
		}, "\ue72c");
		Dlg.Panel(I18n.L("全局模组"), Ui.V(12.0, Ui.H(8.0, open, refresh), scroll), 620.0);
		await Reload();
		async Task Reload()
		{
			list.Children.Clear();
			List<GlobalModRow> rows = (await api.TryCallAsync("list_global_mods", null, new List<GlobalModRow>())) ?? new List<GlobalModRow>();
			if (rows.Count == 0)
			{
				list.Children.Add(Ui.Empty("\ue80a", I18n.L("全局模组目录是空的"), I18n.L("放进去的模组会自动注入每个实例")));
			}
			foreach (GlobalModRow row in rows)
			{
				string file = row.Filename;
				CheckBox box = Ui.Switch(row.Enabled);
				// 【Avalonia 差异】Windows 分别接 Checked / Unchecked;Avalonia 11.3 把这两个标成过时(CS0618),
				// 等价物是 IsCheckedChanged + 判 IsChecked(与 Ui.Check/Switch 门面同一口径)。初值先设、订阅在后。
				box.IsCheckedChanged += delegate
				{
					bool on = box.IsChecked == true;
					PageBase.Run(() => api.TryCallAsync<object>("set_global_mod_enabled", new
					{
						filename = file,
						enabled = on
					}));
				};
				Grid grid = Ui.G(null, "Auto,*,Auto");
				grid.Add(box.M(0.0, 0.0, 12.0).VCenter(), 0);
				grid.Add(Ui.Txt(file, 12.5).Trim().VCenter(), 0, 1);
				grid.Add(Ui.Small(row.Size).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(grid, null, 9.0));
			}
		}
	}
}

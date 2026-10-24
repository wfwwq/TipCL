using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public static class GlobalModsDialog
{
	public static async Task ShowAsync()
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(list);
		smoothScroll.MaxHeight = 380.0;
		smoothScroll.MinWidth = 520.0;
		Button button = Ui.Btn(I18n.L("打开目录"), BtnKind.Soft, delegate
		{
			PageBase.Run(() => api.CallAsync("open_global_mods"));
		}, "\ue8b7");
		Button button2 = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(Reload, I18n.L("读取失败"));
		}, "\ue72c");
		Dlg.Panel(I18n.L("全局模组"), Ui.V(12.0, Ui.H(8.0, button, button2), smoothScroll), 620.0);
		await Reload();
		async Task Reload()
		{
			list.Children.Clear();
			List<GlobalModRow> obj = (await api.TryCallAsync("list_global_mods", null, new List<GlobalModRow>())) ?? new List<GlobalModRow>();
			if (obj.Count == 0)
			{
				list.Children.Add(Ui.Empty("\ue80a", I18n.L("全局模组目录是空的"), I18n.L("放进去的模组会自动注入每个实例")));
			}
			foreach (GlobalModRow item in obj)
			{
				string file = item.Filename;
				CheckBox checkBox = Ui.Switch(item.Enabled);
				checkBox.Checked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("set_global_mod_enabled", new
					{
						filename = file,
						enabled = true
					}));
				};
				checkBox.Unchecked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("set_global_mod_enabled", new
					{
						filename = file,
						enabled = false
					}));
				};
				Grid grid = Ui.G(null, "Auto,*,Auto");
				grid.Add(checkBox.M(0.0, 0.0, 12.0).VCenter(), 0);
				grid.Add(Ui.Txt(file, 12.5).Trim().VCenter(), 0, 1);
				grid.Add(Ui.Small(item.Size).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(grid, null, 9.0));
			}
		}
	}
}

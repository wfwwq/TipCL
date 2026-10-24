using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class ServersPage : PageBase
{
	private readonly ComboBox _inst = Ui.Combo();

	private readonly SPanel _list = Ui.V(9.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBlock _lan = Ui.Small("");

	public override string Title => I18n.L("服务器");

	public ServersPage()
	{
		Button button = Ui.Btn(I18n.L("添加服务器"), BtnKind.Primary, delegate
		{
			PageBase.Run(() => EditAsync(null, -1));
		}, "\ue710");
		Button button2 = Ui.Btn(I18n.L("导入"), BtnKind.Chip, delegate
		{
			PageBase.Run(ImportAsync);
		}, "\ue8b5");
		Button button3 = Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(ExportAsync);
		}, "\uede1");
		Grid grid = Ui.Section(I18n.L("服务器"), I18n.L("直接写进实例的 servers.dat，进游戏就能看到"), Ui.H(8.0, _count.VCenter(), button2, button3, button));
		_inst.SelectionChanged += delegate
		{
			PageBase.Run(ReloadAsync);
		};
		Border border = Ui.Card(Ui.H(10.0, Ui.Txt(I18n.L("实例"), 12.0, bold: false, "B.InkMuted").VCenter(), _inst.W(200.0), _lan.VCenter()), 12.0);
		base.Content = PageBase.ScrollBody(grid, border, _list);
	}

	protected override async Task LoadAsync()
	{
		List<InstanceInfo> source = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_inst.Fill(source.Select((InstanceInfo i) => i.Name));
		TextBlock lan = _lan;
		TextBlock textBlock = lan;
		textBlock.Text = (await PageBase.Api.TryCallAsync("lan_hint", new
		{
			port = 25565
		}, "")) ?? "";
		await ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		List<ServerRow> list = (await PageBase.Api.TryCallAsync("list_servers", new
		{
			instance = inst
		}, new List<ServerRow>())) ?? new List<ServerRow>();
		_list.Children.Clear();
		_count.Text = I18n.L("{0} 个", list.Count);
		if (list.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue968", I18n.L("还没有服务器"), I18n.L("点右上角添加，或从 servers.txt 导入")));
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			int idx = i;
			ServerRow r = list[i];
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(Ui.Glyph("\ue968", 16.0, "B.Accent").M(0.0, 0.0, 12.0).VCenter(), 0);
			grid.Add(Ui.V(3.0, Ui.Txt(string.IsNullOrWhiteSpace(r.Name) ? r.Ip : r.Name, 13.5, bold: true).Trim(), Ui.Small($"{r.Ip}:{r.Port}" + (string.IsNullOrWhiteSpace(r.Description) ? "" : ("  ·  " + r.Description)))).VCenter(), 0, 1);
			grid.Add(Ui.H(6.0, Ui.Btn(I18n.L("进入"), BtnKind.Soft, delegate
			{
				PageBase.Run(() => JoinAsync(r));
			}, "\ue768"), Ui.Btn(I18n.L("编辑"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => EditAsync(r, idx));
			}), Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(async delegate
				{
					if (await Dlg.Confirm(I18n.L("删除服务器"), r.Name, I18n.L("删除"), I18n.L("取消"), danger: true))
					{
						await PageBase.Api.CallAsync("delete_server", new
						{
							instance = inst,
							index = idx
						});
						await ReloadAsync();
					}
				});
			})).VCenter(), 0, 2);
			Border border = Ui.Card(grid, 13.0);
			Motion.HoverLift(border, 1.005, 1.0, 16.0);
			_list.Children.Add(border);
		}
		Motion.Stagger(_list, 24.0);
	}

	private async Task JoinAsync(ServerRow row)
	{
		string inst = _inst.Str();
		List<string> list = (await PageBase.Api.TryCallAsync("get_installed_versions", new
		{
			instance = inst
		}, new List<string>())) ?? new List<string>();
		if (list.Count == 0)
		{
			PageBase.Toast(I18n.L("这个实例没有版本"), I18n.L("先去下载页装一个"), ToastKind.Warning);
			return;
		}
		ComboBox pick = Ui.Combo(list);
		if (await Dlg.Ask(I18n.L("进入 {0}", row.Name), Ui.V(8.0, Ui.Muted($"{row.Ip}:{row.Port}"), pick), I18n.L("启动并直连")))
		{
			string text = await AccountGate.EnsureAsync(PageBase.Win?.Prefs.LastUser);
			if (text != null)
			{
				BridgeClient api = PageBase.Api;
				string version = pick.Str();
				await api.StartTaskAsync("launch_game", new
				{
					instance = inst,
					version = version,
					account = text,
					username = text,
					memory_mb = 4096,
					width = 854,
					height = 480,
					java = "自动选择",
					extra_game_args = new string[4]
					{
						"--server",
						row.Ip,
						"--port",
						row.Port.ToString()
					}
				});
				PageBase.Win?.Navigate("launch");
			}
		}
	}

	private async Task EditAsync(ServerRow? row, int index)
	{
		string inst = _inst.Str();
		TextBox name = Ui.Input(I18n.L("显示名"), row?.Name ?? "");
		TextBox ip = Ui.Input("mc.example.com", row?.Ip ?? "");
		TextBox port = Ui.Input("25565", (row?.Port ?? 25565).ToString());
		TextBox desc = Ui.Input(I18n.L("备注（可空）"), row?.Description ?? "");
		SPanel body = Ui.V(8.0, name, ip, port, desc);
		if (await Dlg.Ask((row == null) ? I18n.L("添加服务器") : I18n.L("编辑服务器"), body, I18n.L("保存")))
		{
			int result;
			int port2 = (int.TryParse(port.Text?.Trim(), out result) ? result : 25565);
			if (row != null)
			{
				await PageBase.Api.CallAsync("update_server", new
				{
					instance = inst,
					index = index,
					name = (name.Text?.Trim() ?? ""),
					ip = (ip.Text?.Trim() ?? ""),
					port = port2,
					description = (desc.Text?.Trim() ?? "")
				});
			}
			else
			{
				await PageBase.Api.CallAsync("add_server", new
				{
					instance = inst,
					name = (name.Text?.Trim() ?? ""),
					ip = (ip.Text?.Trim() ?? ""),
					port = port2,
					description = (desc.Text?.Trim() ?? "")
				});
			}
			await ReloadAsync();
		}
	}

	private async Task ImportAsync()
	{
		string file = Dlg.PickFile(I18n.L("服务器列表 (*.txt)|*.txt|全部文件|*.*"), I18n.L("导入服务器"));
		if (file != null)
		{
			string text = await Task.Run(() => File.ReadAllText(file));
			int num = await PageBase.Api.CallAsync<int>("import_servers", new
			{
				instance = _inst.Str(),
				text = text
			});
			PageBase.Toast(I18n.L("已导入"), I18n.L("{0} 个服务器", num), ToastKind.Success);
			await ReloadAsync();
		}
	}

	private async Task ExportAsync()
	{
		string text = (await PageBase.Api.CallAsync<string>("export_servers", new
		{
			instance = _inst.Str()
		})) ?? "";
		string dest = Dlg.SaveFile(I18n.L("服务器列表 (*.txt)|*.txt"), "servers.txt", I18n.L("导出服务器"));
		if (dest != null)
		{
			await Task.Run(delegate
			{
				File.WriteAllText(dest, text);
			});
			PageBase.Toast(I18n.L("已导出"), dest, ToastKind.Success);
		}
	}

	public override async Task RefreshAsync()
	{
		await ReloadAsync();
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C6b:Windows 版 <c>Pages\ServersPage.cs</c>(7256 字节 / 237 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「服务器」页(不是导航页;它是设置页分段 4 内嵌的子页,见 <see cref="C6bSegmentRegistration"/> 的注入缝)。
///
/// 形状(逐条照抄 Windows):概览行(「N 个」+ 导入 / 导出 / 添加服务器)/ 实例行(实例下拉 + LAN 提示)/
/// 服务器列表(每条 = 图标 + 显示名与 <c>ip:port · 备注</c> + 进入 / 编辑 / 删除)。
///
/// 数据源与调用名一字未改:<c>get_instances</c> / <c>lan_hint</c> / <c>list_servers</c> / <c>add_server</c> /
/// <c>update_server</c> / <c>delete_server</c> / <c>import_servers</c> / <c>export_servers</c> /
/// <c>get_installed_versions</c> / <c>launch_game</c>(走 <c>StartTaskAsync</c>)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;本文件只经 <c>Ui</c> 拿控件;</item>
/// <item>导入 / 导出里的 <c>File.ReadAllText</c> / <c>WriteAllText</c> 是 <c>System.IO</c> 的,两个平台同名同义
///   —— **逐字保留**(它们只在用户真的选了文件之后跑,离屏/测试里选择器返回 null,进不来);</item>
/// <item><c>PageBase.Win?.Prefs.LastUser</c>:mac 的 <c>Win</c> 是 <c>MainWindow?</c>(可空),语义不变;</item>
/// <item>局部变量名照 Windows 的 <c>button2</c> / <c>textBlock</c> 那套反编译器风格**改成语义名**。</item>
/// </list>
/// </summary>
public sealed class ServersPage : PageBase
{
	private readonly ComboBox _inst = Ui.Combo();

	private readonly SPanel _list = Ui.V(9.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBlock _lan = Ui.Small("");

	/// <summary>【Windows 源】<c>:21</c>:标题「服务器」。</summary>
	public override string Title => I18n.L("服务器");

	/// <summary>【Windows 源】<c>:23-50</c>:三个动作 + 实例行 + 三个区块的 <c>BlockHost("servers")</c>。</summary>
	public ServersPage()
	{
		Button add = Ui.Btn(I18n.L("添加服务器"), BtnKind.Primary, delegate
		{
			PageBase.Run(() => EditAsync(null, -1));
		}, "\ue710");
		Button import = Ui.Btn(I18n.L("导入"), BtnKind.Chip, delegate
		{
			PageBase.Run(ImportAsync);
		}, "\ue8b5");
		Button export = Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(ExportAsync);
		}, "\uede1");
		Grid header = Ui.Section(I18n.L("服务器"), I18n.L("直接写进实例的 servers.dat,进游戏就能看到"), Ui.H(8.0, _count.VCenter(), import, export, add));
		_inst.SelectionChanged += delegate
		{
			PageBase.Run(ReloadAsync);
		};
		Border instanceRow = Ui.Card(Ui.H(10.0, Ui.Txt(I18n.L("实例"), 12.0, bold: false, "B.InkMuted").VCenter(), _inst.W(200.0), _lan.VCenter()), 12.0);
		// 页面布局自定义(WP-U3):区块 = 概览 / 实例行 / 服务器列表;默认全可见、顺序同旧 ScrollBody 实参
		Content = new BlockHost("servers", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("概览"), Content = header },
			new BlockSpec { Id = "instance", Title = I18n.L("实例行"), Content = instanceRow },
			new BlockSpec { Id = "list", Title = I18n.L("服务器列表"), Content = _list },
		}).Root;
	}

	/// <summary>【Windows 源】<c>:52-63</c>:先拉实例表,再问一句 LAN 提示,最后拉列表。</summary>
	protected override async Task LoadAsync()
	{
		List<InstanceInfo> instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_inst.Fill(instances.Select((InstanceInfo i) => i.Name));
		_lan.Text = (await PageBase.Api.TryCallAsync("lan_hint", new
		{
			port = 25565
		}, "")) ?? "";
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:65-116</c>:没有选中实例就什么都不做;列表空时给空态。</summary>
	private async Task ReloadAsync()
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}

		List<ServerRow> rows = (await PageBase.Api.TryCallAsync("list_servers", new
		{
			instance = inst
		}, new List<ServerRow>())) ?? new List<ServerRow>();
		_list.Children.Clear();
		_count.Text = I18n.L("{0} 个", rows.Count);
		if (rows.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue968", I18n.L("还没有服务器"), I18n.L("点右上角添加,或从 servers.txt 导入")));
			return;
		}

		for (int i = 0; i < rows.Count; i++)
		{
			int index = i;
			ServerRow row = rows[i];
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(Ui.Glyph("\ue968", 16.0, "B.Accent").M(0.0, 0.0, 12.0).VCenter(), 0);
			grid.Add(Ui.V(3.0, Ui.Txt(string.IsNullOrWhiteSpace(row.Name) ? row.Ip : row.Name, 13.5, bold: true).Trim(), Ui.Small($"{row.Ip}:{row.Port}" + (string.IsNullOrWhiteSpace(row.Description) ? "" : ("  ·  " + row.Description)))).VCenter(), 0, 1);
			grid.Add(Ui.H(6.0, Ui.Btn(I18n.L("进入"), BtnKind.Soft, delegate
			{
				PageBase.Run(() => JoinAsync(row));
			}, "\ue768"), Ui.Btn(I18n.L("编辑"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => EditAsync(row, index));
			}), Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(async delegate
				{
					if (await Dlg.Confirm(I18n.L("删除服务器"), row.Name, I18n.L("删除"), I18n.L("取消"), danger: true))
					{
						await PageBase.Api.CallAsync("delete_server", new
						{
							instance = inst,
							index = index
						});
						await ReloadAsync();
					}
				});
			})).VCenter(), 0, 2);
			Border card = Ui.Card(grid, 13.0);
			Motion.HoverLift(card, 1.005, 1.0, 16.0);
			_list.Children.Add(card);
		}

		Motion.Stagger(_list, 24.0);
	}

	/// <summary>【Windows 源】<c>:118-159</c>:选一个已装版本,带上 <c>--server/--port</c> 起 <c>launch_game</c> 并跳启动页。</summary>
	private async Task JoinAsync(ServerRow row)
	{
		string inst = _inst.Str();
		List<string> versions = (await PageBase.Api.TryCallAsync("get_installed_versions", new
		{
			instance = inst
		}, new List<string>())) ?? new List<string>();
		if (versions.Count == 0)
		{
			PageBase.Toast(I18n.L("这个实例没有版本"), I18n.L("先去下载页装一个"), ToastKind.Warning);
			return;
		}

		ComboBox pick = Ui.Combo(versions);
		if (await Dlg.Ask(I18n.L("进入 {0}", row.Name), Ui.V(8.0, Ui.Muted($"{row.Ip}:{row.Port}"), pick), I18n.L("启动并直连")))
		{
			string? account = await AccountGate.EnsureAsync(PageBase.Win?.Prefs.LastUser);
			if (account != null)
			{
				BridgeClient api = PageBase.Api;
				string version = pick.Str();
				await api.StartTaskAsync("launch_game", new
				{
					instance = inst,
					version = version,
					account = account,
					username = account,
					memory_mb = 4096,
					width = 854,
					height = 480,
					java = "自动选择",
					extra_game_args = new string[4]
					{
						"--server",
						row.Ip,
						"--port",
						row.Port.ToString(),
					}
				});
				PageBase.Win?.Navigate("launch");
			}
		}
	}

	/// <summary>【Windows 源】<c>:161-198</c>:添加 / 编辑共用一张卡;端口解析失败回落 25565。</summary>
	private async Task EditAsync(ServerRow? row, int index)
	{
		string inst = _inst.Str();
		TextBox name = Ui.Input(I18n.L("显示名"), row?.Name ?? "");
		TextBox ip = Ui.Input("mc.example.com", row?.Ip ?? "");
		TextBox port = Ui.Input("25565", (row?.Port ?? 25565).ToString());
		TextBox desc = Ui.Input(I18n.L("备注(可空)"), row?.Description ?? "");
		SPanel body = Ui.V(8.0, name, ip, port, desc);
		if (await Dlg.Ask((row == null) ? I18n.L("添加服务器") : I18n.L("编辑服务器"), body, I18n.L("保存")))
		{
			int parsed;
			int portValue = (int.TryParse(port.Text?.Trim(), out parsed) ? parsed : 25565);
			if (row != null)
			{
				await PageBase.Api.CallAsync("update_server", new
				{
					instance = inst,
					index = index,
					name = (name.Text?.Trim() ?? ""),
					ip = (ip.Text?.Trim() ?? ""),
					port = portValue,
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
					port = portValue,
					description = (desc.Text?.Trim() ?? "")
				});
			}

			await ReloadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:200-214</c>:选一个 txt 读进内存(读盘在 <c>Task.Run</c> 里)。</summary>
	private async Task ImportAsync()
	{
		string? file = Dlg.PickFile(I18n.L("服务器列表 (*.txt)|*.txt|全部文件|*.*"), I18n.L("导入服务器"));
		if (file != null)
		{
			string text = await Task.Run(() => File.ReadAllText(file));
			int count = await PageBase.Api.CallAsync<int>("import_servers", new
			{
				instance = _inst.Str(),
				text
			});
			PageBase.Toast(I18n.L("已导入"), I18n.L("{0} 个服务器", count), ToastKind.Success);
			await ReloadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:216-231</c>:后端给文本、用户选去处,落盘在 <c>Task.Run</c> 里。</summary>
	private async Task ExportAsync()
	{
		string text = (await PageBase.Api.CallAsync<string>("export_servers", new
		{
			instance = _inst.Str()
		})) ?? "";
		string? dest = Dlg.SaveFile(I18n.L("服务器列表 (*.txt)|*.txt"), "servers.txt", I18n.L("导出服务器"));
		if (dest != null)
		{
			await Task.Run(delegate
			{
				File.WriteAllText(dest, text);
			});
			PageBase.Toast(I18n.L("已导出"), dest, ToastKind.Success);
		}
	}

	/// <summary>【Windows 源】<c>:233-236</c>:刷新只重拉列表(不重拉实例表)。</summary>
	public override async Task RefreshAsync()
	{
		await ReloadAsync();
	}
}

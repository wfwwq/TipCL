using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C4:Windows 版 <c>Pages\ModsDialog.cs</c>(7694 字节 / 245 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「模组管理 · &lt;实例&gt;」对话框。
///
/// 形状(逐条照抄 Windows):顶部一行「目标」下拉(实例共享 mods 目录 / 每个隔离版本的目录)+ 计数;
/// 下面是可平滑滚动的模组列表,每条 = 开关(启用/停用)+ 名称与文件名 + 删除;
/// 操作条 = 导入 jar / 检查更新 / 打开目录 / 全局模组 / 刷新。切换目标下拉会重新拉一遍列表
/// (<c>sync</c> 标志挡住"程序自己填下拉"触发的那一次)。
///
/// 数据源与调用名一字未改:<c>get_mods_targets</c> / <c>get_installed_mod_entries</c> /
/// <c>install_mod</c>(走 <c>StartTaskAsync</c>)/ <c>enable_mod</c> / <c>disable_mod</c> /
/// <c>delete_mod</c> / <c>open_mods_folder</c> / <c>check_mod_updates</c> / <c>apply_mod_update</c>。
///
/// <b>依赖审计(C4 的三条实测更正)</b>:任务书说这五个文件只差两个对话框,实测还差
/// <b>7 个 Models DTO</b>(本文件用到 <see cref="ModsTarget"/> 与 <see cref="ModUpdateRow"/>)
/// 与 <b>GlobalModsDialog</b>(Windows 1956 字节,本文件第 77 行直接调用它)——
/// 按「小而自包含就一并搬」的规定一起搬来了,见 PROGRESS 的 C4 小节与 D-133。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Visibility.Collapsed</c> → <c>IsVisible = false</c>(本文件只用在 <c>Ui</c> 门面内部);</item>
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>(本文件只经 <c>Ui</c> 拿控件,没有显式转换);</item>
/// <item><c>SetResourceReference</c> 之类一处没用;<c>Ui</c> / <c>Dlg</c> / <c>Motion</c> / <c>Fmt</c> / <c>I18n</c>
///   在 mac 侧已是同名同参的门面(C1c / C2a),所以调用形状一字未改;</item>
/// <item>局部变量名照 Windows 的 <c>list2</c> / <c>item2</c> 那套反编译器风格**改成语义名**
///   (Windows 源是被反编译出来的,<c>array</c> / <c>list2</c> 之类名字没有含义);类型、顺序、判据一字不改;</item>
/// <item>匿名对象参数的**属性名**(线协议字段名)逐字保留,包括 <c>extra.path</c> / <c>extra.instance</c> /
///   <c>extra.name</c> 三个导入载荷字段。</item>
/// </list>
/// </summary>
public static class ModsDialog
{
	/// <summary>
	/// 弹出「模组管理 · &lt;instance&gt;」:先拉目标表、再拉当前目标的模组列表。
	/// 【Windows 源】<c>ModsDialog.cs:14-178</c>。
	/// </summary>
	/// <param name="instance">实例名(所有后端调用的 <c>instance</c> 参数)。</param>
	/// <param name="version">初始目标版本(空 = 实例共享 mods 目录);目标表拉回来后会按它选中。</param>
	public static async Task ShowAsync(string instance, string version = "")
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(6.0);
		SmoothScroll scroll = Ui.Scroll(list);
		// 【Windows 源】:窗口高度的 50%、下限 220(与 SavesDialog 的 0.4/180 不同,别抄错)
		scroll.BindWindowMaxHeight(0.5, 220.0);
		scroll.MinWidth = 620.0;
		TextBlock count = Ui.Muted("");
		ComboBox target = Ui.Combo(null, null, 230.0);
		List<ModsTarget> targets = new List<ModsTarget>();
		// 【Windows 源】:sync 为真时表示"这一轮选中项是程序填的",不重新拉列表(否则拉表会再触发一次选中)
		bool sync = false;
		target.SelectionChanged += delegate
		{
			if (!sync)
			{
				PageBase.Run(Reload, I18n.L("读取失败"));
			}
		};
		SPanel tools = Ui.H(8.0,
			Ui.Btn(I18n.L("导入 jar"), BtnKind.Soft, delegate
			{
				PageBase.Run(async delegate
				{
					string[]? files = Dlg.PickFiles(I18n.L("模组 (*.jar)|*.jar"), I18n.L("导入模组"));
					if (files != null)
					{
						foreach (string path in files)
						{
							await api.StartTaskAsync("install_mod", new
							{
								name = Path.GetFileName(path),
								instance = instance,
								extra = new
								{
									path = path,
									instance = instance,
									name = Path.GetFileName(path)
								}
							});
						}
						AppServices.Toast(I18n.L("已加入队列"), I18n.L("{0} 个模组", files.Length));
					}
				}, I18n.L("导入失败"));
			}, "\ue8b5"),
			Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
			{
				PageBase.Run(async delegate
				{
					await CheckUpdatesAsync(instance);
					await Reload();
				}, I18n.L("检查更新失败"));
			}, "\ue72c"),
			Ui.Btn(I18n.L("打开目录"), BtnKind.Chip, delegate
			{
				PageBase.Run(async delegate
				{
					string? opened = await api.CallAsync<string>("open_mods_folder", new
					{
						instance = instance,
						version = CurrentVersion()
					});
					AppServices.Toast(I18n.L("已打开"), opened ?? "", ToastKind.Success);
				});
			}, "\ue8b7"),
			Ui.Btn(I18n.L("全局模组"), BtnKind.Chip, delegate
			{
				PageBase.Run(GlobalModsDialog.ShowAsync);
			}, "\ue80a"),
			Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
			{
				PageBase.Run(Reload, I18n.L("读取失败"));
			}, "\ue72c"));
		Grid head = Ui.G(null, "Auto,Auto,*");
		head.Add(Ui.Txt(I18n.L("目标"), 12.0, bold: false, "B.InkMuted").VCenter(), 0);
		head.Add(target.M(8.0, 0.0, 12.0).VCenter(), 0, 1);
		head.Add(count.VCenter(), 0, 2);
		Dlg.Panel(I18n.L("模组管理 · {0}", instance), Ui.V(12.0, head, tools, scroll), 800.0);
		await ReloadTargets();
		string CurrentVersion()
		{
			int selected = target.SelectedIndex;
			if (selected < 0 || selected >= targets.Count)
			{
				return version;
			}
			return targets[selected].Value;
		}
		async Task Reload()
		{
			string ver = CurrentVersion();
			list.Children.Clear();
			List<ModEntry> mods = (await api.TryCallAsync("get_installed_mod_entries", new
			{
				instance = instance,
				version = ver
			}, new List<ModEntry>())) ?? new List<ModEntry>();
			count.Text = I18n.L("共 {0} 个模组", mods.Count);
			if (mods.Count == 0)
			{
				list.Children.Add(Ui.Empty("\uea86", I18n.L("还没有模组"), I18n.L("到 Mod 页搜索安装,或直接导入 jar")));
			}
			foreach (ModEntry mod in mods)
			{
				string file = mod.Filename;
				// 闭包里用的 ver 是**本次** Reload 那次的目标版本(Windows 也是这么捕获的)
				CheckBox box = Ui.Switch(mod.Enabled);
				// 【Avalonia 差异】Windows 分别接 Checked / Unchecked;Avalonia 11.3 把这两个标成过时(CS0618),
				// 等价物是 IsCheckedChanged + 判 IsChecked(与 Ui.Check/Switch 门面同一口径)。初值先设、订阅在后,
				// 所以不会多触发一次。
				box.IsCheckedChanged += delegate
				{
					if (box.IsChecked == true)
					{
						PageBase.Run(() => api.TryCallAsync<object>("enable_mod", new
						{
							instance = instance,
							filename = file,
							version = ver
						}));
					}
					else
					{
						PageBase.Run(() => api.TryCallAsync<object>("disable_mod", new
						{
							instance = instance,
							filename = file,
							version = ver
						}));
					}
				};
				Grid row = Ui.G(null, "Auto,*,Auto");
				row.Add(box.M(0.0, 0.0, 12.0).VCenter(), 0);
				row.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(mod.Name) ? file : mod.Name).Trim(), Ui.Small(file)).VCenter(), 0, 1);
				row.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除模组"), file, I18n.L("删除"), I18n.L("取消"), danger: true))
						{
							await api.CallAsync("delete_mod", new
							{
								instance = instance,
								filename = file,
								version = ver
							});
							await Reload();
						}
					}, I18n.L("删除失败"));
				}).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(row, null, 10.0));
			}
			Motion.Stagger(list, 16.0);
		}
		async Task ReloadTargets()
		{
			targets = (await api.TryCallAsync("get_mods_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
			if (targets.Count == 0)
			{
				// 【Windows 源】:目标表为空时兜一个「实例共享 mods 目录」(Value 为空),不是空下拉
				targets.Add(new ModsTarget
				{
					Label = I18n.L("实例共享 mods 目录"),
					Value = ""
				});
			}
			sync = true;
			int selected = Math.Max(0, targets.FindIndex(t => t.Value == version));
			target.Items.Clear();
			foreach (ModsTarget item in targets)
			{
				target.Items.Add(item.Label);
			}
			target.SelectedIndex = selected;
			sync = false;
			await Reload();
		}
	}

	/// <summary>
	/// 「检查更新」:先问 <c>check_mod_updates</c>,一条都没有就 Toast「都是最新的」;
	/// 有的话弹一张勾选卡,点了「更新所选」才逐个 <c>apply_mod_update</c>,最后汇总成功/失败。
	/// 【Windows 源】<c>ModsDialog.cs:180-244</c>。
	/// </summary>
	public static async Task CheckUpdatesAsync(string instance)
	{
		BridgeClient api = AppServices.Client;
		List<ModUpdateRow> updates;
		// 【Windows 源】:整段比对包在 Dlg.Busy 里(离屏/测试里 Busy 也是正常的一层浮层)
		using (Dlg.Busy(I18n.L("正在比对模组版本...")))
		{
			updates = (await api.TryCallAsync("check_mod_updates", new { instance }, new List<ModUpdateRow>())) ?? new List<ModUpdateRow>();
		}
		if (updates.Count == 0)
		{
			AppServices.Toast(I18n.L("都是最新的"), I18n.L("没有找到可更新的模组"), ToastKind.Success);
			return;
		}
		SPanel list = Ui.V(6.0);
		SmoothScroll scroll = Ui.Scroll(list);
		// 【Windows 源】:这里的比例/下限与主列表不同(0.45 / 200 / 600)
		scroll.BindWindowMaxHeight(0.45, 200.0);
		scroll.MinWidth = 600.0;
		List<(CheckBox Box, ModUpdateRow Row)> picks = new List<(CheckBox, ModUpdateRow)>();
		foreach (ModUpdateRow row in updates)
		{
			CheckBox box = Ui.Check("", value: true);
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(box.M(0.0, 0.0, 10.0).VCenter(), 0);
			grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(row.Name) ? row.Filename : row.Name, 13.0, bold: true).Trim(), Ui.Small(row.Current + " → " + row.Latest + "  ·  " + row.Source + "  ·  " + Fmt.Size(row.Size)).Wrap()).VCenter(), 0, 1);
			grid.Add(Ui.Tag(string.IsNullOrWhiteSpace(row.McVersion) ? "—" : row.McVersion).VCenter(), 0, 2);
			list.Children.Add(Ui.RowCard(grid, null, 10.0));
			picks.Add((box, row));
		}
		if (!await Dlg.Ask(I18n.L("{0} 个模组有更新", updates.Count), Ui.V(10.0, Ui.Muted(I18n.L("勾上要更新的,旧文件会在新包校验通过后才删除。")), scroll), I18n.L("更新所选")))
		{
			return;
		}
		List<ModUpdateRow> chosen = (from p in picks
			where p.Box.IsChecked == true
			select p.Row).ToList();
		if (chosen.Count == 0)
		{
			return;
		}
		int done = 0;
		List<string> failed = new List<string>();
		using (Dlg.Busy(I18n.L("正在更新 {0} 个模组...", chosen.Count)))
		{
			foreach (ModUpdateRow row in chosen)
			{
				try
				{
					await api.CallAsync<string>("apply_mod_update", new { instance, row });
					done++;
				}
				catch (Exception ex)
				{
					failed.Add(I18n.L("{0}:{1}", row.Filename, ex.Message));
				}
			}
		}
		if (failed.Count == 0)
		{
			AppServices.Toast(I18n.L("已更新"), I18n.L("{0} 个模组", done), ToastKind.Success);
		}
		else
		{
			await Dlg.Alert(I18n.L("部分模组没能更新"), I18n.L("成功 {0} 个,失败 {1} 个:\n\n", done, failed.Count) + string.Join("\n", failed));
		}
	}
}

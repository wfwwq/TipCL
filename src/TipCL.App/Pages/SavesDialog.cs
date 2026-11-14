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
/// C4:Windows 版 <c>Pages\SavesDialog.cs</c>(8351 字节 / 226 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「存档管理 · &lt;实例&gt;」对话框。
///
/// 形状(逐条照抄 Windows):一张竖排卡片里从上到下 = 存档列表(窗口高度 40%、下限 180)/ 分隔线 /
/// 备份列表(裸 MaxHeight 150)/ 分隔线 / 一行「截图 / 日志」下拉 + 文件数 / 媒体列表(裸 MaxHeight 170)。
/// 存档每条 = 名称 + 「大小 · 上次游玩 · 模式」副行 + 备份/导出/打开/装数据包/删除;
/// 备份最多列 20 条 = 名称 + 「大小 · 日期」+ 还原/删除;媒体最多列 60 条 = 名称 + 大小 + 打开/导出。
///
/// 数据源与调用名一字未改:<c>list_saves</c> / <c>list_save_backups</c> / <c>list_media</c> /
/// <c>backup_save</c>(走 <c>StartTaskAsync</c>)/ <c>export_save</c> / <c>open_save</c> /
/// <c>delete_save</c> / <c>restore_save_backup</c> / <c>delete_save_backup</c> /
/// <c>open_media</c> / <c>get_saves_targets</c> / <c>get_installed_datapacks</c> /
/// <c>install_datapack_into_save</c>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>(本文件只经 <c>Ui</c> 拿控件);</item>
/// <item>媒体「导出」按钮里的 <c>File.Copy(path, dest, overwrite: true)</c> 是 <c>System.IO</c> 的,
///   两个平台同名同义 —— <b>逐字保留</b>(所以这条路径真的会写文件;单测里把它指向 <c>TestHome.Root</c>
///   之下的临时目录,见 PROGRESS 的 C4 小节);</item>
/// <item>「导出」按钮的点击处理器在 Windows 里是**同步** lambda(没有 <c>PageBase.Run</c> 包)。
///   逐字保留 —— 它自己 try/catch 并 Toast,不会把异常抛到外面;</item>
/// <item>局部变量名照 Windows 的 <c>list2</c> / <c>text2</c> 那套反编译器风格**改成语义名**
///   (类型、顺序、判据、闭包捕获的时机一字不改 —— 每条循环都先把 <c>item.Name</c> / <c>i.Path</c>
///   抄进一个局部变量再进闭包,这正是 Windows 的写法,也是"循环变量捕获"不出错的原因)。</item>
/// </list>
/// </summary>
public static class SavesDialog
{
	/// <summary>
	/// 弹出「存档管理 · &lt;instance&gt;[/&lt;version&gt;]」:先拉存档与备份,再拉媒体那一栏。
	/// 【Windows 源】<c>SavesDialog.cs:14-186</c>。
	/// </summary>
	/// <param name="instance">实例名(所有后端调用的 <c>instance</c> 参数)。</param>
	/// <param name="version">版本 id(<c>list_saves</c> / <c>list_media</c> 等都要它;空串合法)。</param>
	public static async Task ShowAsync(string instance, string version)
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(8.0);
		SPanel backups = Ui.V(6.0);
		SmoothScroll scroll = Ui.Scroll(list);
		// 【Windows 源】:存档列表按窗口高度的 40%、下限 180
		scroll.BindWindowMaxHeight(0.4, 180.0);
		scroll.MinWidth = 620.0;
		SmoothScroll scrollBackups = Ui.Scroll(backups);
		// 【Windows 源】:备份与媒体两栏是裸 MaxHeight(150 / 170),不是 BindWindowMaxHeight
		scrollBackups.MaxHeight = 150.0;
		SPanel media = Ui.V(6.0);
		SmoothScroll scrollMedia = Ui.Scroll(media);
		scrollMedia.MaxHeight = 170.0;
		ComboBox kind = Ui.Combo(new string[3]
		{
			I18n.L("截图"),
			I18n.L("崩溃报告"),
			I18n.L("日志"),
		}, null, 130.0);
		TextBlock mediaCount = Ui.Small("");
		kind.SelectionChanged += delegate
		{
			PageBase.Run(ReloadMedia, I18n.L("读取失败"));
		};
		Grid head = Ui.G(null, "Auto,Auto,*");
		head.Add(Ui.Txt(I18n.L("截图 / 日志"), 12.5, bold: true).VCenter(), 0);
		head.Add(kind.M(10.0, 0.0, 10.0).VCenter(), 0, 1);
		head.Add(mediaCount.VCenter(), 0, 2);
		SPanel body = Ui.V(12.0, scroll, Ui.Sep(), scrollBackups, Ui.Sep(), head, scrollMedia);
		// 【Windows 源】:标题里版本为空就不拼 " / <version>"
		Dlg.Panel(I18n.L("存档管理 · {0}", instance) + (string.IsNullOrEmpty(version) ? "" : (" / " + version)), body, 780.0);
		await Reload();
		await ReloadMedia();
		async Task Reload()
		{
			list.Children.Clear();
			backups.Children.Clear();
			List<SaveRow> saves = (await api.TryCallAsync("list_saves", new { instance, version }, new List<SaveRow>())) ?? new List<SaveRow>();
			if (saves.Count == 0)
			{
				list.Children.Add(Ui.Empty("\ue74e", I18n.L("这个版本还没有存档"), I18n.L("进游戏创建世界后会出现在这里")));
			}
			foreach (SaveRow save in saves)
			{
				string name = save.Name;
				SPanel mid = Ui.V(2.0, Ui.Txt(name, 13.0, bold: true).Trim(), Ui.Small(string.Join(" · ", new string[3] { save.Size, save.LastPlayed, save.Mode }.Where((string x) => !string.IsNullOrWhiteSpace(x)))));
				SPanel tail = Ui.H(6.0,
					Ui.Btn(I18n.L("备份"), BtnKind.Chip, delegate
					{
						PageBase.Run(async delegate
						{
							await api.StartTaskAsync("backup_save", new { instance, name, version });
							AppServices.Toast(I18n.L("已开始备份"), name);
						}, I18n.L("备份失败"));
					}),
					Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
					{
						PageBase.Run(async delegate
						{
							string? dest = Dlg.SaveFile(I18n.L("压缩包 (*.zip)|*.zip"), name + ".zip", I18n.L("导出存档"));
							if (dest != null)
							{
								await api.CallAsync("export_save", new { instance, name, dest, version });
								AppServices.Toast(I18n.L("已导出"), dest, ToastKind.Success);
							}
						}, I18n.L("导出失败"));
					}),
					Ui.Btn(I18n.L("打开"), BtnKind.Chip, delegate
					{
						PageBase.Run(() => api.CallAsync("open_save", new { instance, name, version }));
					}),
					Ui.Btn(I18n.L("装数据包"), BtnKind.Chip, delegate
					{
						PageBase.Run(() => InstallDatapackAsync(instance, version, name), I18n.L("安装失败"));
					}),
					Ui.Btn(I18n.L("删除"), BtnKind.Danger, delegate
					{
						PageBase.Run(async delegate
						{
							if (await Dlg.Confirm(I18n.L("删除存档"), I18n.L("「{0}」会被永久删除，其中的建筑与进度都无法恢复。", name), I18n.L("永久删除"), I18n.L("取消"), danger: true))
							{
								await api.CallAsync("delete_save", new { instance, name, version });
								await Reload();
							}
						}, I18n.L("删除失败"));
					}));
				Grid grid = Ui.G(null, "*,Auto");
				grid.Add(mid.VCenter(), 0);
				grid.Add(tail.VCenter(), 0, 1);
				list.Children.Add(Ui.RowCard(grid));
			}
			Motion.Stagger(list);
			List<BackupRow> backupRows = (await api.TryCallAsync("list_save_backups", new { instance, version }, new List<BackupRow>())) ?? new List<BackupRow>();
			backups.Children.Add(Ui.Txt(I18n.L("备份（{0}）", backupRows.Count), 12.5, bold: true));
			// 【Windows 源】:备份只列前 20 条(Take(20),不是分页)
			foreach (BackupRow backup in backupRows.Take(20))
			{
				string bn = backup.Name;
				Grid grid = Ui.G(null, "*,Auto");
				grid.Add(Ui.V(1.0, Ui.Txt(bn, 12.0).Trim(), Ui.Small(backup.Size + " · " + backup.Date)).VCenter(), 0);
				grid.Add(Ui.H(6.0,
					Ui.Btn(I18n.L("还原"), BtnKind.Chip, delegate
					{
						PageBase.Run(async delegate
						{
							if (await Dlg.Confirm(I18n.L("还原备份"), I18n.L("用「{0}」覆盖现有存档？", bn), I18n.L("还原")))
							{
								await api.CallAsync("restore_save_backup", new
								{
									instance = instance,
									backup_name = bn,
									version = version,
									overwrite = true
								});
								AppServices.Toast(I18n.L("已还原"), bn, ToastKind.Success);
								await Reload();
							}
						}, I18n.L("还原失败"));
					}),
					Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
					{
						PageBase.Run(async delegate
						{
							await api.CallAsync("delete_save_backup", new
							{
								instance = instance,
								backup_name = bn,
								version = version
							});
							await Reload();
						}, I18n.L("删除失败"));
					})).VCenter(), 0, 1);
				backups.Children.Add(grid);
			}
		}
		async Task ReloadMedia()
		{
			media.Children.Clear();
			string label = kind.Str();
			// 【Windows 源】:下拉文案 → 后端 kind 键;三个都没匹配上就当截图(与 Windows 的最后一个分支同)
			string kindKey = ((label == I18n.L("崩溃报告")) ? "crash-reports" : ((label == I18n.L("日志")) ? "logs" : "screenshots"));
			List<MediaRow> rows = (await api.TryCallAsync("list_media", new
			{
				instance = instance,
				kind = kindKey,
				version = version
			}, new List<MediaRow>())) ?? new List<MediaRow>();
			mediaCount.Text = I18n.L("{0} 个文件", rows.Count);
			if (rows.Count == 0)
			{
				media.Children.Add(Ui.Muted(I18n.L("这个目录还是空的")));
				return;
			}
			// 【Windows 源】:媒体只列前 60 条(Take(60))
			foreach (MediaRow row in rows.Take(60))
			{
				string path = row.Path;
				Grid grid = Ui.G(null, "*,Auto,Auto");
				grid.Add(Ui.V(1.0, Ui.Txt(row.Name, 12.5).Trim(), Ui.Small(Fmt.Size(row.Bytes))).VCenter(), 0);
				grid.Add(Ui.Btn(I18n.L("打开"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => api.TryCallAsync("open_media", new { path }, fallback: false));
				}).VCenter(), 0, 1);
				grid.Add(Ui.Btn(I18n.L("导出"), BtnKind.Ghost, delegate
				{
					string? dest = Dlg.SaveFile(I18n.L("全部文件|*.*"), row.Name, I18n.L("导出文件"));
					if (dest == null)
					{
						return;
					}
					try
					{
						// 【Windows 源】:这里是**真的复制文件**(不经后端),所以本机/真机都成立
						File.Copy(path, dest, overwrite: true);
						AppServices.Toast(I18n.L("已导出"), dest, ToastKind.Success);
					}
					catch (Exception ex)
					{
						AppServices.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
					}
				}).VCenter().M(6.0, 0.0, 0.0, 0.0), 0, 2);
				media.Children.Add(grid);
			}
		}
	}

	/// <summary>
	/// 问一句"装到哪个实例的存档目录":目标表只有 1 项就直接用、0 项返回空串、多项才弹下拉。
	/// 【Windows 源】<c>SavesDialog.cs:188-202</c>。返回值 <c>null</c> = 用户取消。
	/// </summary>
	public static async Task<string?> PickSaveTargetAsync(string instance, string title)
	{
		List<ModsTarget> rows = (await AppServices.Client.TryCallAsync("get_saves_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
		if (rows.Count <= 1)
		{
			return (rows.Count == 1) ? rows[0].Value : "";
		}
		ComboBox pick = Ui.Combo(rows.Select((ModsTarget r) => r.Label));
		if (!await Dlg.Ask(title, Ui.V(8.0, Ui.Muted(I18n.L("装进哪个版本的存档目录？")), pick), I18n.L("继续")))
		{
			return null;
		}
		// 【Windows 源】:越界一律夹到 0(取不到就第 0 项),不是 -1 / 不抛
		int index = Math.Max(0, pick.SelectedIndex);
		return (index < rows.Count) ? rows[index].Value : "";
	}

	/// <summary>
	/// 「装数据包」:先看有没有装过数据包(没有就弹一句提示),再选一个装进这个世界。
	/// 【Windows 源】<c>SavesDialog.cs:204-225</c>。
	/// </summary>
	private static async Task InstallDatapackAsync(string instance, string version, string save)
	{
		BridgeClient api = AppServices.Client;
		List<string> packs = (await api.TryCallAsync("get_installed_datapacks", new { instance }, new List<string>())) ?? new List<string>();
		if (packs.Count == 0)
		{
			await Dlg.Alert(I18n.L("没有数据包"), I18n.L("先到「数据包」页装一个，再回来装进存档。"));
			return;
		}
		ComboBox pick = Ui.Combo(packs);
		if (await Dlg.Ask(I18n.L("装进存档 · {0}", save), Ui.V(8.0, Ui.Muted(I18n.L("选择要装进这个世界的数据包")), pick), I18n.L("安装")))
		{
			string? name = await api.CallAsync<string>("install_datapack_into_save", new
			{
				instance = instance,
				filename = pick.Str(),
				save_name = save,
				version = version
			});
			AppServices.Toast(I18n.L("已安装"), string.IsNullOrWhiteSpace(name) ? pick.Str() : name, ToastKind.Success);
		}
	}
}

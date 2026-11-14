using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Pages;

namespace TipCL.Services;

/// <summary>
/// 拖放:把拖进来的文件按 <see cref="FileKinds"/> 认出的种类分发下去
/// (= Windows <c>Services\FileDrop.cs</c>,逐行等价搬迁)。
///
/// 公开面与 Windows 一一对应:<c>DropContext</c>(<c>None</c> / <c>Instance</c> / <c>Version</c> /
/// <c>PreferKind</c>)、<c>HandleAsync</c>、<c>ImportModpackAsync</c>;私有的
/// <c>AskKindAsync</c> / <c>ApplyAsync</c> / <c>SuggestName</c> / <c>SetWallpaperAsync</c> /
/// <c>SetSkinAsync</c> / <c>CurrentInstanceAsync</c> 也逐个搬过来。
///
/// **本包新增的那一半**(Windows 长在 <c>MainWindow</c> 里,属"拖放浮层"):
/// <c>Attach</c>/<c>Detach</c>、<c>DragEnter</c>/<c>DragOver</c>/<c>DragLeave</c>/<c>Drop</c>、
/// <c>Veil</c>/<c>VeilVisible</c>/<c>Dragging</c>、总闸 <c>Enabled</c> 与 <c>ReadyGate</c>。
/// 这一半照 Windows <c>MainWindow.xaml.cs:238-256</c>(PreviewDragEnter/Over/Leave/Drop → ShowDropVeil)
/// 与 <c>:1133-1177</c>(<c>ShowDropVeil</c>/<c>OnDragOver</c>/<c>OnDrop</c>)的语义写:
/// <list type="bullet">
/// <item>拖进来的东西带文件 → 出浮层;离开 / 落点 → 收浮层;</item>
/// <item>后端没连上(<c>!_bridgeReady</c>)→ 既不出浮层也不接文件(Windows 同);</item>
/// <item>落点交给"当前页"处理:页面调过 <c>PageBase.EnableDrop</c> 就用它的上下文(HUD 里的实例 / 版本),
///   否则用 <see cref="DropContext.None"/>(Windows 靠 WPF 的路由:页面 <c>PreviewDrop</c> 先于窗口
///   <c>Drop</c>,处理了就 <c>Handled</c>;mac 侧改成显式问当前页,结果等价)。</item>
/// </list>
///
/// 三处 mac 侧差异(逐条记在 PROGRESS.md 的 C2c2 差异表):
/// <list type="number">
/// <item>类型是 <c>internal</c>(Windows 是 <c>public</c>):<c>HandleAsync</c> 要收 <c>MainWindow</c>,
///   而 mac 的 MainWindow 是 internal(C2b 的自由),public 成员不能暴露 internal 类型 + <c>CS0051</c>;</item>
/// <item>皮肤走 <see cref="SkinApplier"/> 注入缝(Windows 直接调 <c>AccountPage.EditSkinExternalAsync</c>,
///   那是页面,本包不许搬页面);没接的时候抛「账号页还没移植」,与 Windows"没离线账号"那条一样是
///   <c>InvalidOperationException</c>,由调用方的 <c>Run</c> 兜住;</item>
/// <item>截图路径 <c>pack://...</c> 换成 <c>avares://...</c> 由 <see cref="Wallpaper"/> 负责。</item>
/// </list>
/// </summary>
internal static class FileDrop
{
	/// <summary>拖放的上下文(Windows 同名嵌套类,逐字一致)。</summary>
	public sealed class DropContext
	{
		/// <summary>什么都不带(窗口级拖放的默认上下文)。</summary>
		public static readonly DropContext None = new DropContext();

		public string Instance { get; init; } = "";

		public string Version { get; init; } = "";

		public string PreferKind { get; init; } = "";
	}

	/// <summary>落点的投递口(注入缝):测试拿它接住"页面收到了什么",默认走 <see cref="HandleAsync"/>。</summary>
	internal delegate Task DropSink(IReadOnlyList<string> paths, DropContext? context);

	private static readonly Dictionary<string, string> Installers = new Dictionary<string, string>
	{
		["mod"] = "install_mod",
		["resourcepack"] = "install_resourcepack",
		["shaderpack"] = "install_shader",
		["datapack"] = "install_datapack",
		["world"] = "install_world"
	};

	private static MainWindow? _window;

	private static DropVeil? _veil;

	private static bool _cancelledAfterLeave;

	/// <summary>拖放浮层(还没 <see cref="Attach"/> 时是 null)。</summary>
	internal static DropVeil? Veil => _veil;

	/// <summary>浮层此刻对外的可见标志。</summary>
	internal static bool VeilVisible => _veil?.IsShown ?? false;

	/// <summary>正在被拖拽(拖进来之后、离开或落点之前)。</summary>
	internal static bool Dragging { get; private set; }

	/// <summary>
	/// 这一轮拖拽已经"离开过窗口"了吗。离开之后再落点等于"拖走了":Windows 靠事件路由
	/// (拖到窗口外松手时窗口收不到 <c>Drop</c>),mac 侧显式记一笔,于是
	/// "拖出去 = 取消"这条语义可以确定性地验;重新拖进来(<see cref="DragEnter"/>)会清掉。
	/// </summary>
	internal static bool DragCancelled => _cancelledAfterLeave;

	/// <summary>拖放总闸:false 时既不出浮层也不接文件(测试 / 离屏要整关时用它)。</summary>
	internal static bool Enabled { get; set; } = true;

	/// <summary>后端有没有连上(Windows 的 <c>_bridgeReady</c>;默认看 <see cref="AppServices.Ready"/>)。</summary>
	internal static Func<bool> ReadyGate { get; set; } = DefaultReady;

	/// <summary>落点的投递口(注入缝;null = 走默认的 <see cref="HandleAsync"/>)。</summary>
	internal static DropSink? Sink { get; set; }

	/// <summary>皮肤落点的注入缝(Windows 是 <c>AccountPage.EditSkinExternalAsync</c>,属页面)。</summary>
	internal static Func<AccountRow, string, Task<string>>? SkinApplier { get; set; }

	/// <summary>此刻能不能接拖放(总闸 + 后端)。</summary>
	internal static bool CanDrop => Enabled && ReadyGate();

	/// <summary>外壳构造时把浮层挂上来(= Windows 的 <c>PreviewDrag*</c> 那几行接线)。</summary>
	internal static void Attach(MainWindow window, DropVeil veil)
	{
		_window = window;
		_veil = veil;
		ResetDragState();
	}

	/// <summary>外壳关窗时摘掉(静态引用不留活窗口)。</summary>
	internal static void Detach(MainWindow window)
	{
		if (ReferenceEquals(_window, window))
		{
			_window = null;
			_veil = null;
			ResetDragState();
		}
	}

	/// <summary>
	/// 拖进 / 拖过(= Windows <c>PreviewDragEnter</c> / <c>PreviewDragOver</c> → <c>ShowDropVeil</c>)。
	/// <paramref name="paths"/> 是这次拖拽携带的文件(Windows 是 <c>e.Data.GetDataPresent(DataFormats.FileDrop)</c>)。
	/// </summary>
	internal static bool DragEnter(IReadOnlyList<string>? paths)
	{
		bool hasFiles = paths is { Count: > 0 };
		if (hasFiles && CanDrop)
		{
			Dragging = true;
			_cancelledAfterLeave = false;
		}

		return SetVeil(hasFiles);
	}

	/// <summary>拖过(与 <see cref="DragEnter"/> 同一条语义;Windows 两处都调 <c>ShowDropVeil</c>)。</summary>
	internal static bool DragOver(IReadOnlyList<string>? paths)
	{
		return DragEnter(paths);
	}

	/// <summary>拖走了(= Windows <c>PreviewDragLeave</c> → <c>ShowDropVeil(false)</c>)。</summary>
	internal static bool DragLeave()
	{
		Dragging = false;
		_cancelledAfterLeave = true;
		return SetVeil(false);
	}

	/// <summary>把这一轮的拖拽状态清干净(挂新窗口 / 测试收尾用;不碰口子与总闸)。</summary>
	internal static void ResetDragState()
	{
		Dragging = false;
		_cancelledAfterLeave = false;
		_veil?.Show(false);
	}

	/// <summary>
	/// 落点(= Windows <c>PreviewDrop</c> 收浮层 + <c>Drop</c> 分发)。返回"这次真的接了没有"。
	/// 后端没连上、没有文件、或没有可用的落点 → 什么都不做(浮层照 Windows 一样先收掉)。
	/// </summary>
	internal static bool Drop(IReadOnlyList<string>? paths)
	{
		// 拖出去松手 = 取消:Windows 那边窗口根本收不到这次 Drop(指针已经不在窗口上),
		// mac 侧用离开标记把同一条语义钉住。
		bool cancelled = _cancelledAfterLeave;
		Dragging = false;
		_cancelledAfterLeave = false;
		_veil?.Show(false);
		if (cancelled || !CanDrop)
		{
			return false;
		}

		List<string> list = (paths ?? Array.Empty<string>()).Where((string p) => !string.IsNullOrWhiteSpace(p)).ToList();
		if (list.Count == 0)
		{
			return false;
		}

		// Windows:页面自己的 PreviewDrop 先跑(页面调过 EnableDrop 就用页面的上下文),否则窗口
		// 那个 Drop 用 FileDrop.HandleAsync(this, paths)(上下文 = None)。
		DropContext context = _window?.CurrentPage?.DropContextNow() ?? DropContext.None;
		DropSink sink = Sink ?? DefaultSink;
		PageBase.Run(() => sink(list, context), I18n.L("拖放处理失败"));
		return true;
	}

	/// <summary>把拖进来的路径认一遍种类(Windows 在 <c>HandleAsync</c> 里内联的那句)。</summary>
	internal static List<FileKinds.Verdict> IdentifyPaths(IReadOnlyList<string> paths)
	{
		return paths.Where((string p) => !string.IsNullOrWhiteSpace(p)).Select(FileKinds.Identify).ToList();
	}

	public static async Task HandleAsync(MainWindow window, IReadOnlyList<string> paths, DropContext? context = null)
	{
		DropContext ctx = context ?? DropContext.None;
		if (paths.Count == 0)
		{
			return;
		}

		List<FileKinds.Verdict> list = await Task.Run(() => IdentifyPaths(paths));
		if (list.Count == 0)
		{
			return;
		}

		Dictionary<string, List<string>> buckets = new Dictionary<string, List<string>>();
		List<FileKinds.Verdict> unsure = new List<FileKinds.Verdict>();
		foreach (FileKinds.Verdict item in list)
		{
			string kind = ((item.Sure && item.Kinds.Count == 1)
				? item.Kinds[0]
				: ((ctx.PreferKind.Length > 0 && item.Kinds.Contains(ctx.PreferKind)) ? ctx.PreferKind : ""));
			if (kind.Length > 0)
			{
				if (!buckets.TryGetValue(kind, out List<string>? group))
				{
					group = new List<string>();
					buckets[kind] = group;
				}

				group.Add(item.Path);
			}
			else
			{
				unsure.Add(item);
			}
		}

		if (unsure.Count > 0)
		{
			FileKinds.Verdict head = unsure[0];
			List<FileKinds.Verdict> same = unsure.Where((FileKinds.Verdict i) => i.KindSig == head.KindSig).ToList();
			string picked = await AskKindAsync(head, same.Count - 1);
			if (!string.IsNullOrEmpty(picked))
			{
				if (!buckets.TryGetValue(picked, out List<string>? group))
				{
					group = new List<string>();
					buckets[picked] = group;
				}

				group.AddRange(same.Select((FileKinds.Verdict i) => i.Path));
			}

			List<FileKinds.Verdict> rest = unsure.Except(same).ToList();
			if (rest.Count > 0)
			{
				AppServices.Toast(I18n.L("有文件没处理"), I18n.L("这些认不出来，可以单独拖进来再选：")
					+ string.Join(I18n.L("、"), rest.Take(3).Select((FileKinds.Verdict i) => i.Name)));
			}
		}

		foreach (KeyValuePair<string, List<string>> bucket in buckets)
		{
			string kind = bucket.Key;
			List<string> group = bucket.Value;
			try
			{
				string message = await ApplyAsync(window, kind, group, ctx);
				if (!string.IsNullOrEmpty(message))
				{
					AppServices.Toast(FileKinds.Label(kind), message, ToastKind.Success);
				}
			}
			catch (Exception ex)
			{
				AppServices.Toast(I18n.L("没能处理"), Path.GetFileName(group[0]) + " — " + ex.Message, ToastKind.Error);
			}
		}
	}

	public static async Task ImportModpackAsync(MainWindow window, string path)
	{
		FileKinds.ModpackProbe.Info? info = await Task.Run(() => FileKinds.ModpackProbe.Probe(path));
		if (info is null)
		{
			AppServices.Toast(I18n.L("这不是整合包"), Path.GetFileName(path), ToastKind.Warning);
			return;
		}

		TextBox name = Ui.Input(I18n.L("新版本名称"), SuggestName(info));
		SPanel body = Ui.V(
			8.0,
			Ui.Txt(string.IsNullOrEmpty(info.Name) ? Path.GetFileName(path) : info.Name, 14.0, bold: true).Wrap(),
			Ui.Muted(info.Format).Wrap(),
			Ui.Small(string.Join(" · ", new string[3]
			{
				string.IsNullOrEmpty(info.McVersion) ? "" : ("Minecraft " + info.McVersion),
				FileKinds.ModpackProbe.LoaderLabel(info.Loader, info.LoaderVersion),
				(info.Files > 0) ? I18n.L("{0} 个文件", info.Files) : ""
			}.Where((string x) => !string.IsNullOrEmpty(x)))).Wrap(),
			Ui.Field(I18n.L("装成版本"), name, I18n.L("会建成一个新版本，原有版本不受影响"), 78.0));
		if (await Dlg.Ask(I18n.L("安装整合包"), body, I18n.L("开始安装")))
		{
			string target = name.Text?.Trim() ?? "";
			if (string.IsNullOrEmpty(target))
			{
				target = SuggestName(info);
			}

			await AppServices.Client.StartTaskAsync("install_modpack", new
			{
				name = target,
				source = "本地",
				extra = new
				{
					path = path,
					name = target,
					source = "本地",
					kind = "modpack"
				}
			});
			window.Navigate("tasks");
			AppServices.Toast(I18n.L("已加入队列"), I18n.L("正在安装「{0}」", target), ToastKind.Success);
		}
	}

	private static bool DefaultReady()
	{
		return AppServices.Ready;
	}

	private static Task DefaultSink(IReadOnlyList<string> paths, DropContext? context)
	{
		MainWindow? window = _window;
		return (window is null) ? Task.CompletedTask : HandleAsync(window, paths, context);
	}

	private static bool SetVeil(bool on)
	{
		// Windows ShowDropVeil 的头一句:后端没连上就一律收起来。
		if (!CanDrop)
		{
			on = false;
		}

		return _veil?.Show(on) ?? false;
	}

	private static async Task<string> AskKindAsync(FileKinds.Verdict info, int others)
	{
		List<string> kinds = ((info.Kinds.Count > 0) ? info.Kinds : FileKinds.AllKinds.ToList());
		SPanel panel = Ui.V(
			8.0,
			Ui.Txt(info.Name, 13.5, bold: true).Wrap(),
			string.IsNullOrWhiteSpace(info.Detail) ? null : Ui.Muted(info.Detail).Wrap());
		List<(RadioButton Btn, string Kind)> picks = new List<(RadioButton, string)>();
		SPanel rows = Ui.V(6.0);
		foreach (string kind in kinds)
		{
			RadioButton radio = new RadioButton
			{
				Content = FileKinds.Label(kind) + " — " + FileKinds.Action(kind),
				IsChecked = (picks.Count == 0),
				Margin = new Avalonia.Thickness(0.0, 2.0, 0.0, 2.0)
			};
			picks.Add((radio, kind));
			rows.Children.Add(radio);
		}

		panel.Children.Add(rows);
		if (others > 0)
		{
			panel.Children.Add(Ui.Small(I18n.L("这一批里还有 {0} 个同样认不准的文件，会照同一个选择处理。", others)).Wrap());
		}

		if (!await Dlg.Ask(I18n.L("这个文件放哪儿？"), panel, I18n.L("就这么放"), I18n.L("算了"), danger: false, 460.0))
		{
			return "";
		}

		return picks.FirstOrDefault(((RadioButton Btn, string Kind) p) => p.Btn.IsChecked == true).Kind ?? "";
	}

	private static async Task<string> ApplyAsync(MainWindow window, string kind, List<string> paths, DropContext ctx)
	{
		switch (kind)
		{
		case "modpack":
			await ImportModpackAsync(window, paths[0]);
			return "";
		case "wallpaper":
			return await SetWallpaperAsync(paths[0]);
		case "skin":
			return await SetSkinAsync(paths[0]);
		case "plugin":
			// 插件 zip 拖进窗口任意位置:识别为 TipCL 插件后本地安装;同 id 已装时后端按更新替换处理
			foreach (string pluginPath in paths)
			{
				await AppServices.Client.CallAsync<object>("plugins_install_local", new { path = pluginPath, dev = false });
			}

			return I18n.L("正在安装插件，进度见插件页");
		default:
		{
			if (!Installers.TryGetValue(kind, out string? method))
			{
				throw new InvalidOperationException(I18n.L("还不支持这种文件"));
			}

			string instance = (ctx.Instance.Length <= 0) ? await CurrentInstanceAsync() : ctx.Instance;
			foreach (string path in paths)
			{
				await AppServices.Client.StartTaskAsync(method, new
				{
					name = Path.GetFileName(path),
					instance = instance,
					extra = new
					{
						path = path,
						instance = instance,
						version = ctx.Version,
						source = "本地"
					}
				});
			}

			window.Navigate("tasks");
			string where = ((ctx.Instance.Length > 0) ? I18n.L("实例「{0}」", instance) : I18n.L("默认实例"));
			return I18n.L("已加入下载任务，装进{0}的{1}", where, FileKinds.Label(kind));
		}
		}
	}

	private static string SuggestName(FileKinds.ModpackProbe.Info info)
	{
		string text = string.Join(" ", new string[2] { info.Name, info.Version }
			.Where((string x) => !string.IsNullOrWhiteSpace(x))).Trim();
		if (text.Length > 0)
		{
			return text;
		}

		if (!string.IsNullOrEmpty(info.McVersion))
		{
			return I18n.L("整合包 ") + info.McVersion;
		}

		return I18n.L("整合包");
	}

	private static async Task<string> SetWallpaperAsync(string path)
	{
		string folder = (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "ui_background_folder",
			@default = ""
		}, "")) ?? "";
		bool hadFolder = !string.IsNullOrWhiteSpace(folder);
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new
			{
				ui_background = path,
				ui_background_folder = ""
			}
		});
		await Wallpaper.ReloadAsync();

		// mac 侧唯一的收严:管线读不出来(视频壁纸本版不支持、图片坏掉、文件不在)时,
		// 报后端给出/管线记下的原因,而不是照样说"已设为背景图"(见 DECISIONS 的 C2c2 条目)。
		if (!string.IsNullOrEmpty(Wallpaper.Error))
		{
			return Wallpaper.Error;
		}

		string what = (FileKinds.IsVideo(path) ? I18n.L("动态壁纸") : I18n.L("背景图"));
		return hadFolder ? I18n.L("已设为{0}，文件夹轮播一并停掉了", what) : I18n.L("已设为{0}", what);
	}

	private static async Task<string> SetSkinAsync(string path)
	{
		List<AccountRow> rows = ((await AppServices.Client.TryCallAsync("get_account_rows", null, new List<AccountRow>())) ?? new List<AccountRow>())
			.Where((AccountRow r) => r.Type == "offline").ToList();
		if (rows.Count == 0)
		{
			throw new InvalidOperationException(I18n.L("还没有离线账号；先到「账号」页建一个，再把皮肤拖进来"));
		}

		Func<AccountRow, string, Task<string>>? applier = SkinApplier;
		if (applier is null)
		{
			throw new InvalidOperationException(I18n.L("「账号」页还没移植,皮肤拖放暂时用不了"));
		}

		return await applier(rows.FirstOrDefault((AccountRow r) => r.Active) ?? rows[0], path);
	}

	private static async Task<string> CurrentInstanceAsync()
	{
		string name = (AppServices.Window as MainWindow)?.Prefs.CatalogInstance ?? "";
		if (!string.IsNullOrWhiteSpace(name))
		{
			return name;
		}

		string fallback = (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "")) ?? "";
		if (!string.IsNullOrWhiteSpace(fallback))
		{
			return fallback;
		}

		return ((await AppServices.Client.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>())
			.FirstOrDefault()?.Name ?? "default";
	}
}

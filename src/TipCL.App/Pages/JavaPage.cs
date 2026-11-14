using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C6b:Windows 版 <c>Pages\JavaPage.cs</c>(12320 字节 / 413 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「Java」页(不是导航页;它是设置页**分段 2** 内嵌的子页,见 <see cref="C6bSegmentRegistration"/> 的注入缝)。
///
/// 形状(逐条照抄 Windows):概览行(共 N 个 / 刷新 / 扫描系统 Java)/ 下载新运行时卡(发行版下拉 + 五个大版本卡)/
/// 实例默认卡(实例 / 默认 Java / 自定义路径 / 当前记录)/ 已检测列表(每条 = 大版本方块 + 名称与路径 + 三个动作)。
///
/// 数据源与调用名一字未改:<c>get_instances</c> / <c>java_vendor_list</c> / <c>java_vendor_label</c> /
/// <c>java_combo_options</c> / <c>java_combo_label_for</c> / <c>get_instance_java</c> / <c>instance_java_label</c> /
/// <c>set_instance_java</c> / <c>normalize_java_pref</c> / <c>get_java_list</c> / <c>download_java</c> /
/// <c>install_java</c>(后两个走 <c>StartTaskAsync</c>)/ <c>save_settings</c> / <c>open_media</c>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Border.SetResourceReference(BackgroundProperty, "B.AccentSoft")</c> → <c>Dyn(...)</c>;</item>
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;</item>
/// <item><b>"打开所在目录"的最后那一步换平台口径(Lead 在 C6b 裁决里点的名)</b>:Windows 是
///   <c>Process.Start("explorer.exe", "/select,…")</c> —— 那是 Windows 专有,不许照搬。这里
///   ① 先走**既有**口径:后端 <c>open_media</c>(与 Windows 同序,一字未改);
///   ② 后端不成时走 <see cref="FolderRevealer"/> 注入缝(与 <c>CrashUi.ClipboardWriter</c> /
///     <c>CatalogPage.ClipboardReader</c> 同一模式,**测试靠它拦截,绝不真起进程**);
///   ③ 缝没接就退回**既有**的 <c>Crash.ShellOpen</c>(B1b 抽出来的那条:Windows 走 explorer、
///     mac 走 <c>open</c>、Linux 走 <c>xdg-open</c>,而且它自己认 <c>Crash.ShellOpenHook</c>)——
///     如实**降级为"只打开目录、不选中文件"**,不假装选中(记在 DECISIONS D-147)。</item>
/// <item>局部变量名照 Windows 的 <c>button2</c> / <c>textBlock</c> 那套反编译器风格**改成语义名**。</item>
/// </list>
/// </summary>
public sealed class JavaPage : PageBase
{
	/// <summary>【Windows 源】<c>:16-23</c>:五个大版本与它们的适用说明(顺序即卡片顺序)。</summary>
	private static readonly (string Major, string Note)[] Majors = new(string, string)[5]
	{
		("8", I18n.L("1.16 及以下旧版本")),
		("11", I18n.L("部分旧模组环境")),
		("17", I18n.L("1.18 – 1.20.4 推荐")),
		("21", I18n.L("1.20.5 – 1.21.11")),
		("25", I18n.L("26.1+ 新版本")),
	};

	/// <summary>
	/// 【注入缝】"在文件管理器里揭示并选中某个路径"。
	/// 未接时退回 <c>Crash.ShellOpen</c>(只打开目录、不选中);测试里**必须**接缝或设
	/// <c>Crash.ShellOpenHook</c>,绝不真起进程。见类注释第 3 条与 DECISIONS D-147。
	/// </summary>
	internal static Action<string>? FolderRevealer { get; set; }

	private readonly SPanel _list = Ui.V(8.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly ComboBox _inst = Ui.Combo(null, null, 170.0);

	private readonly ComboBox _java = Ui.Combo();

	private readonly ComboBox _vendor = Ui.Combo(null, null, 170.0);

	private readonly TextBlock _instJava = Ui.Small("");

	private readonly TextBox _manual = Ui.Input(I18n.L("手动填 java.exe 路径（可空）"));

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<JavaOption> _javaOpts = new List<JavaOption>();

	private List<string> _vendors = new List<string>();

	private bool _sync;

	/// <summary>【Windows 源】<c>:47</c>:标题 "Java"。</summary>
	public override string Title => "Java";

	/// <summary>【Windows 源】<c>:49-115</c>:四个区块的 <c>BlockHost("java")</c>。</summary>
	public JavaPage()
	{
		Button scan = Ui.Btn(I18n.L("扫描系统 Java"), BtnKind.Soft, null, "\ue721");
		scan.Click += delegate
		{
			PageBase.Run(() => ReloadListAsync(scan: true), I18n.L("扫描失败"));
		};
		Button refresh = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
		{
			PageBase.Run(LoadAsync);
		});
		Grid header = Ui.Section("Java", I18n.L("游戏按版本挑 Java：26.1+ 要 25，1.20.5+ 要 21，1.18+ 要 17，老版本用 8"), Ui.H(8.0, _count.VCenter(), refresh, scan));
		WrapPanel wrap = new WrapPanel();
		foreach ((string major, string note) in Majors)
		{
			string key = major;
			Button download = Ui.Btn(I18n.L("下载"), BtnKind.Chip, null, "\ue896");
			download.Click += delegate
			{
				PageBase.Run(() => DownloadAsync(key, download), I18n.L("下载失败"));
			};
			Border card = Ui.Card(Ui.V(6.0, Ui.Txt("Java " + major, 15.0, bold: true), Ui.Small(note).Wrap(), download.Stretch()), 13.0);
			card.Width = 158.0;
			card.Margin = new Thickness(0.0, 0.0, 10.0, 10.0);
			Motion.HoverLift(card, 1.01, 2.0, 18.0);
			wrap.Children.Add(card);
		}

		Grid vendorRow = Ui.G(null, "*,Auto,Auto");
		vendorRow.Add(Ui.Txt(I18n.L("下载新运行时"), 13.0, bold: true).VCenter(), 0);
		vendorRow.Add(Ui.Txt(I18n.L("发行版"), 12.0, bold: false, "B.InkMuted").M(0.0, 0.0, 8.0).VCenter(), 0, 1);
		vendorRow.Add(_vendor.VCenter(), 0, 2);
		Border installCard = Ui.Card(Ui.V(10.0, vendorRow, wrap), 13.0);
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(ReloadJavaOptionsAsync);
			}
		};
		_java.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SaveJavaAsync, I18n.L("保存失败"));
			}
		};
		Button applyManual = Ui.Btn(I18n.L("用这个路径"), BtnKind.Chip, delegate
		{
			PageBase.Run(ApplyManualAsync, I18n.L("保存失败"));
		}, "\ue73e");
		Grid manualRow = Ui.G(null, "*,Auto");
		manualRow.Add(_manual, 0);
		manualRow.Add(applyManual.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border defaultsCard = Ui.Card(Ui.V(4.0, Ui.Field(I18n.L("实例"), _inst), Ui.Field(I18n.L("默认 Java"), _java, I18n.L("「自动选择」由启动器按游戏版本匹配")), Ui.Field(I18n.L("自定义路径"), manualRow, I18n.L("填绝对路径后点右边按钮，会先做一次规范化")), Ui.Field("", _instJava)), 13.0);
		// 页面布局自定义(WP-U3):区块 = 说明 / 安装方式 / 实例默认 / 已检测列表;默认全可见、顺序同旧 ScrollBody 实参
		Content = new BlockHost("java", new List<BlockSpec>
		{
			new BlockSpec { Id = "intro", Title = I18n.L("说明"), Content = header },
			new BlockSpec { Id = "install", Title = I18n.L("安装方式"), Content = installCard },
			new BlockSpec { Id = "defaults", Title = I18n.L("实例默认"), Content = defaultsCard },
			new BlockSpec { Id = "list", Title = I18n.L("已检测列表"), Content = _list },
		}).Root;
	}

	/// <summary>【Windows 源】<c>:117-128</c>:实例表 → 发行版表(带 await)→ 当前实例的 Java → 已检测列表。</summary>
	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(_instances.Select((InstanceInfo i) => i.Name));
		_sync = false;
		Task vendors = ReloadVendorsAsync();
		Task list = ReloadListAsync(scan: false);
		await vendors;
		await ReloadJavaOptionsAsync();
		await list;
	}

	/// <summary>【Windows 源】<c>:130-157</c>:发行版表为空时兜一个 adoptium;标签拿不到就用键名。</summary>
	private async Task ReloadVendorsAsync()
	{
		_vendors = (await PageBase.Api.TryCallAsync("java_vendor_list", null, new List<string>())) ?? new List<string>();
		if (_vendors.Count == 0)
		{
			_vendors = new List<string> { "adoptium" };
		}

		List<string> labels = (await Task.WhenAll(_vendors.Select((string v) => PageBase.Api.TryCallAsync("java_vendor_label", new
		{
			vendor = v
		}, v)))).ToList();
		for (int i = 0; i < labels.Count; i++)
		{
			if (string.IsNullOrEmpty(labels[i]))
			{
				labels[i] = _vendors[i];
			}
		}

		_sync = true;
		_vendor.Items.Clear();
		foreach (string label in labels)
		{
			_vendor.Items.Add(label);
		}

		int adoptium = _vendors.IndexOf("adoptium");
		_vendor.SelectedIndex = ((adoptium >= 0) ? adoptium : 0);
		_sync = false;
	}

	/// <summary>【Windows 源】<c>:159-167</c>:越界一律回落到 adoptium。</summary>
	private string SelectedVendor()
	{
		int selected = _vendor.SelectedIndex;
		if (selected < 0 || selected >= _vendors.Count)
		{
			return "adoptium";
		}

		return _vendors[selected];
	}

	/// <summary>【Windows 源】<c>:169-194</c>:拉当前实例可选的 Java,选中后端说的那一个。</summary>
	private async Task ReloadJavaOptionsAsync()
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}

		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = inst,
			scan_system = false
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count != 0)
		{
			string keep = await PageBase.Api.TryCallAsync("java_combo_label_for", new
			{
				instance = inst,
				options = opts
			}, "自动选择");
			_sync = true;
			_javaOpts = opts;
			_java.Fill(opts.Select((JavaOption o) => o.Label), keep);
			_sync = false;
			await ShowInstanceJavaAsync(inst);
		}
	}

	/// <summary>【Windows 源】<c>:196-207</c>:「当前记录:…」那一行。</summary>
	private async Task ShowInstanceJavaAsync(string inst)
	{
		string stored = (await PageBase.Api.TryCallAsync("get_instance_java", new
		{
			name = inst
		}, "")) ?? "";
		string label = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = inst
		}, "")) ?? "";
		_instJava.Text = (string.IsNullOrEmpty(stored) ? "" : (I18n.L("当前记录：{0}", label) + ((stored == label) ? "" : I18n.L("（{0}）", stored))));
	}

	/// <summary>【Windows 源】<c>:209-224</c>:把下拉选中的标签翻回后端值再写。</summary>
	private async Task SaveJavaAsync()
	{
		string inst = _inst.Str();
		if (!string.IsNullOrEmpty(inst))
		{
			string label = _java.Str();
			string java = _javaOpts.FirstOrDefault((JavaOption o) => o.Label == label)?.Value ?? (string.IsNullOrEmpty(label) ? "自动选择" : label);
			await PageBase.Api.CallAsync<object>("set_instance_java", new
			{
				name = inst,
				java
			});
			await ShowInstanceJavaAsync(inst);
			PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, label), ToastKind.Success);
		}
	}

	/// <summary>【Windows 源】<c>:226-252</c>:手填路径先规范化一次再写;路径为空只提示 + 抖一下。</summary>
	private async Task ApplyManualAsync()
	{
		string inst = _inst.Str();
		string raw = _manual.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(inst) || raw.Length == 0)
		{
			PageBase.Toast(I18n.L("先填路径"), I18n.L("填一个 java.exe 的绝对路径"), ToastKind.Warning);
			Motion.Shake(_manual);
			return;
		}

		string normalized = (await PageBase.Api.CallAsync<string>("normalize_java_pref", new
		{
			java = raw
		})) ?? raw;
		if (normalized != raw)
		{
			PageBase.Toast(I18n.L("已规范化"), raw + "\n→ " + normalized);
		}

		await PageBase.Api.CallAsync<object>("set_instance_java", new
		{
			name = inst,
			java = normalized
		});
		_manual.Clear();
		await ReloadJavaOptionsAsync();
		PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, normalized), ToastKind.Success);
	}

	/// <summary>【Windows 源】<c>:254-289</c>:先摆两个骨架条,再按 <c>scan</c> 决定要不要真扫系统。</summary>
	private async Task ReloadListAsync(bool scan)
	{
		_list.Children.Clear();
		_list.Children.Add(Ui.Skeleton(52.0));
		_list.Children.Add(Ui.Skeleton(52.0));
		List<JavaInfo> list;
		if (scan)
		{
			using (Dlg.Busy(I18n.L("正在扫描系统 Java...")))
			{
				list = (await PageBase.Api.TryCallAsync("get_java_list", new
				{
					scan_system = true
				}, new List<JavaInfo>())) ?? new List<JavaInfo>();
			}
		}
		else
		{
			list = (await PageBase.Api.TryCallAsync("get_java_list", new
			{
				scan_system = false
			}, new List<JavaInfo>())) ?? new List<JavaInfo>();
		}

		_list.Children.Clear();
		_count.Text = I18n.L("共 {0} 个", list.Count);
		if (list.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\uec32", I18n.L("没有找到 Java"), I18n.L("点上面「下载」装一个，或扫描系统已有安装")));
			return;
		}

		foreach (JavaInfo item in list)
		{
			_list.Children.Add(Row(item));
		}

		Motion.Stagger(_list, 18.0, 190.0, 8.0);
	}

	/// <summary>【Windows 源】<c>:291-342</c>:一行 = 大版本方块 + 名称与路径 + 设为默认 / 给本实例 / 打开所在目录。</summary>
	private Control Row(JavaInfo java)
	{
		Border badge = new Border
		{
			Width = 42.0,
			Height = 42.0,
			CornerRadius = new CornerRadius(8.0),
			Child = Ui.Txt(java.Major, 14.0, bold: true, "B.AccentText").Center(),
		};
		badge.Dyn(Border.BackgroundProperty, "B.AccentSoft");
		badge.VCenter();
		Button makeDefault = Ui.Btn(I18n.L("设为默认"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync<object>("save_settings", new
				{
					data = new
					{
						default_java = java.Path
					}
				});
				PageBase.Toast(I18n.L("已设为默认"), java.Name, ToastKind.Success);
			}, I18n.L("保存失败"));
		});
		Button forInstance = Ui.Btn(I18n.L("给本实例"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				string inst = _inst.Str();
				if (!string.IsNullOrEmpty(inst))
				{
					await PageBase.Api.CallAsync<object>("set_instance_java", new
					{
						name = inst,
						java = java.Path
					});
					await ReloadJavaOptionsAsync();
					PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, java.Name), ToastKind.Success);
				}
			}, I18n.L("保存失败"));
		});
		Button openFolder = Ui.IconBtn("\ue8b7", I18n.L("打开所在目录"), delegate
		{
			PageBase.Run(() => OpenFolderAsync(java.Path), I18n.L("打开失败"));
		});
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(badge.M(0.0, 0.0, 12.0), 0);
		grid.Add(Ui.V(2.0, Ui.Txt(java.Name, 13.0, bold: true).Trim(), Ui.Mono(java.Path).Trim()).VCenter(), 0, 1);
		grid.Add(Ui.H(6.0, makeDefault, forInstance, openFolder).VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 2);
		return Ui.RowCard(grid, null, 11.0);
	}

	/// <summary>
	/// 【Windows 源】<c>:344-373</c> 的最后一步换了平台口径(类注释第 3 条,Lead 在 C6b 裁决里点的名):
	/// 后端 <c>open_media</c> 先走(与 Windows 同序);不成则走注入缝;再没有就 <c>Crash.ShellOpen</c>
	/// 只把目录打开(**不选中文件**,如实降级,记 D-147)。**绝不在测试里真起进程**。
	/// </summary>
	private async Task OpenFolderAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		bool isFile = File.Exists(path);
		string target = (isFile ? (Path.GetDirectoryName(path) ?? path) : path);
		if (await PageBase.Api.TryCallAsync("open_media", new
		{
			path = target
		}, fallback: false))
		{
			return;
		}

		// ① 注入缝:接了就由它决定"揭示并选中"(mac 的等价物是 `open -R <path>`)
		Action<string>? reveal = FolderRevealer;
		if (reveal != null)
		{
			reveal(path);
			return;
		}

		// ② 没接缝:退回既有口径,只打开目录(不选中)。Crash.ShellOpen 自己认平台分支
		//    (Windows explorer / mac open / Linux xdg-open),并且认 Crash.ShellOpenHook —— 测试靠它拦截。
		try
		{
			TipCL.Core.Launcher.Crash.ShellOpen(target);
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("打开失败"), ex.Message, ToastKind.Warning);
		}
	}

	/// <summary>【Windows 源】<c>:375-395</c>:adoptium 走 <c>download_java</c>,别的发行版走 <c>install_java</c>。</summary>
	private async Task DownloadAsync(string major, Control anchor)
	{
		string vendor = SelectedVendor();
		if (!(vendor == "adoptium"))
		{
			await PageBase.Api.StartTaskAsync("install_java", new
			{
				major = int.Parse(major),
				vendor
			});
		}
		else
		{
			await PageBase.Api.StartTaskAsync("download_java", new { major, vendor });
		}

		if (SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, "Java " + major);
		}

		PageBase.Toast(I18n.L("已加入队列"), I18n.L("正在下载 {0} Java {1}，进度看下载任务", vendor, major));
	}

	/// <summary>【Windows 源】<c>:397-412</c>:某个任务跑完且标题里有 Java ⇒ 重拉列表与下拉。</summary>
	public override void OnEvent(BridgeEvent ev)
	{
		if (!(ev.Event == "finished") || !ev.Success)
		{
			return;
		}

		TaskRow? row = TaskStore.Get(ev.TaskId);
		if (row != null && row.Title.Contains("Java", StringComparison.OrdinalIgnoreCase))
		{
			PageBase.Run(async delegate
			{
				await ReloadListAsync(scan: false);
				await ReloadJavaOptionsAsync();
			});
		}
	}
}

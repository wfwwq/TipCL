using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C3b:Windows 版 <c>Pages\VersionSetupDialog.cs</c>(WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「版本设置」对话框(单个实例下、单个版本的私有设置)。
///
/// 形状:先拉 <c>get_version_settings</c>(缺口用 <see cref="VersionSettingsDto"/> 的默认值兜)、再拉
/// <c>get_accounts</c>(列表头部插入「(不绑定)」),把 14 个字段排成一张 <c>Ui.Scroll</c> 卡片
/// (版本隔离 / 独占内存 / JVM 参数 / 垃圾回收器 / 直连服务器 / 端口 / 启动前命令 + 等待 /
/// 退出后命令 / 窗口标题 / 窗口模式 / 绑定账号 / 统一通行证 / 在版本列表中隐藏),用 <see cref="Dlg.Ask"/>
/// 弹出来;点「保存」才把整份设置字典写回 <c>save_version_settings</c>,否则什么都不做。
///
/// 调用形状与 Windows 一字不差:<c>await VersionSetupDialog.ShowAsync(instance, version)</c>
/// (<c>LaunchPage:1635</c> / <c>LibraryPage:717</c> / <c>VersionManagePage:562</c> 都是这么调的),
/// 返回 <c>Task</c>(没有返回值,结果只经 Toast 呈现)。
///
/// Windows → Avalonia 的差异:只有一个 —— <c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>。
/// 其余(下拉框的取值口径、<c>Ui.Field</c> 的三参形状、<c>SmoothScroll.BindWindowMaxHeight</c> 的
/// 0.55 / 260、<c>MinWidth = 560</c>、<c>Dlg.Ask</c> 的「保存」)在 C1c / C2a 的门面里已经是同名同参,
/// 所以这一份几乎是逐行照抄。
///
/// 忠实保留的两处 Windows 写法:
/// <list type="number">
/// <item><see cref="IsoLabels"/> / <see cref="GcLabels"/> 是 <c>static readonly</c>,文案在**类型初始化**
///   那一刻就经 <c>I18n.L</c> 定死了(Windows 也是这样)。运行中切语言不会重算这两张表 —— 逐字保留,别"顺手"
///   改成每次现算;</item>
/// <item>越界索引一律用 <c>Math.Max(0, ...)</c> 夹住(取不到 → 第 0 项「跟随全局」/「自动」),
///   所以"数据里的值不在候选表里"退化成默认项,而不是 -1 / 抛异常。</item>
/// </list>
/// Windows 的 <c>dictionary2</c> / <c>dictionary3</c> 只是同一个字典的别名,这里直接写
/// <c>payload</c>(赋值顺序、键名、类型与来源一字不差)。
/// </summary>
public static class VersionSetupDialog
{
	/// <summary>【Windows 源】<c>VersionSetupDialog.cs:12-18</c>:版本隔离的 4 个显示文案。</summary>
	private static readonly string[] IsoLabels = new string[4]
	{
		I18n.L("跟随全局"),
		I18n.L("关闭(共用实例目录)"),
		I18n.L("隔离存档"),
		I18n.L("隔离全部"),
	};

	/// <summary>【Windows 源】:版本隔离的 4 个取值(与 <see cref="IsoLabels"/> 按下标一一对应)。</summary>
	private static readonly string[] IsoValues = new string[4] { "", "none", "saves", "all" };

	/// <summary>【Windows 源】<c>:22-29</c>:垃圾回收器的 5 个显示文案。</summary>
	private static readonly string[] GcLabels = new string[5]
	{
		I18n.L("自动"),
		I18n.L("G1(通用)"),
		I18n.L("ZGC(大内存)"),
		I18n.L("Serial(老电脑)"),
		I18n.L("不指定"),
	};

	/// <summary>【Windows 源】:垃圾回收器的 5 个取值(与 <see cref="GcLabels"/> 按下标一一对应)。</summary>
	private static readonly string[] GcValues = new string[5] { "auto", "g1", "zgc", "serial", "none" };

	/// <summary>
	/// 弹出「版本设置 · &lt;version&gt;」并(点了保存才)写回后端。
	/// 【Windows 源】<c>VersionSetupDialog.cs:33-108</c>。
	/// </summary>
	/// <param name="instance">实例名(写进 <c>get_/save_version_settings</c> 的 <c>instance</c> 参数)。</param>
	/// <param name="version">版本 id(同上;<c>Title</c> 里也用它,形如「版本设置 · 1.20.1」)。</param>
	public static async Task ShowAsync(string instance, string version)
	{
		BridgeClient api = AppServices.Client;
		VersionSettingsDto data = (await api.TryCallAsync("get_version_settings", new
		{
			instance,
			version,
		}, new VersionSettingsDto())) ?? new VersionSettingsDto();
		List<string> accounts = (await api.TryCallAsync("get_accounts", null, new List<string>())) ?? new List<string>();
		// Windows 也是**插到最前**:第 0 项是「(不绑定)」,所以下标 0 = 不绑定、下标 N = 账号 N-1。
		accounts.Insert(0, I18n.L("(不绑定)"));

		ComboBox iso = Ui.Combo(IsoLabels, IsoLabels[Math.Max(0, Array.IndexOf(IsoValues, data.Isolation ?? ""))]);
		TextBox mem = Ui.Input(I18n.L("留空则用全局设置"), data.MemoryMb?.ToString() ?? "");
		TextBox jvm = Ui.Input("-XX:+UseG1GC ...", data.JvmArgs ?? "");
		ComboBox gc = Ui.Combo(GcLabels, GcLabels[Math.Max(0, Array.IndexOf(GcValues, string.IsNullOrEmpty(data.Gc) ? "auto" : data.Gc))]);
		TextBox server = Ui.Input(I18n.L("进游戏直连的服务器"), data.Server ?? "");
		TextBox port = Ui.Input("25565", data.Port ?? "");
		TextBox pre = Ui.Input(I18n.L("启动前执行的命令"), data.PreLaunch ?? "");
		CheckBox preWait = Ui.Check(I18n.L("等待启动前命令结束"), data.PreLaunchWait);
		TextBox post = Ui.Input(I18n.L("退出后执行的命令"), data.PostLaunch ?? "");
		TextBox title = Ui.Input(I18n.L("自定义游戏窗口标题"), data.WindowTitle ?? "");
		ComboBox winMode = Ui.Combo(new string[3]
		{
			I18n.L("窗口"),
			I18n.L("全屏"),
			I18n.L("最大化"),
		}, (data.WindowMode == "fullscreen") ? I18n.L("全屏") : ((data.WindowMode == "maximized") ? I18n.L("最大化") : I18n.L("窗口")));
		ComboBox acc = Ui.Combo(accounts, string.IsNullOrEmpty(data.LoginAccount) ? I18n.L("(不绑定)") : data.LoginAccount);
		TextBox nide8 = Ui.Input(I18n.L("统一通行证 ServerId(可空)"), data.Nide8Id ?? "");
		CheckBox hidden = Ui.Check(I18n.L("在版本列表中隐藏"), data.Hidden);

		SmoothScroll scroll = Ui.Scroll(Ui.V(4.0,
			Ui.Field(I18n.L("版本隔离"), iso, I18n.L("存档/配置是否与实例其它版本分开")),
			Ui.Field(I18n.L("独占内存"), mem, I18n.L("单位 MB")),
			Ui.Field(I18n.L("JVM 参数"), jvm),
			Ui.Field(I18n.L("垃圾回收器"), gc),
			Ui.Field(I18n.L("直连服务器"), server),
			Ui.Field(I18n.L("端口"), port),
			Ui.Field(I18n.L("启动前命令"), pre),
			Ui.Field("", preWait),
			Ui.Field(I18n.L("退出后命令"), post),
			Ui.Field(I18n.L("窗口标题"), title),
			Ui.Field(I18n.L("窗口模式"), winMode),
			Ui.Field(I18n.L("绑定账号"), acc, I18n.L("启动这个版本时强制用该账号")),
			Ui.Field(I18n.L("统一通行证"), nide8),
			Ui.Field("", hidden)));
		scroll.BindWindowMaxHeight(0.55, 260.0);
		scroll.MinWidth = 560.0;

		if (!await Dlg.Ask(I18n.L("版本设置 · ") + version, scroll, I18n.L("保存")))
		{
			return;
		}

		Dictionary<string, object?> payload = new Dictionary<string, object?>
		{
			["isolation"] = IsoValues[Math.Max(0, iso.SelectedIndex)],
			["jvm_args"] = jvm.Text ?? "",
			["gc"] = GcValues[Math.Max(0, gc.SelectedIndex)],
			["server"] = server.Text ?? "",
			["port"] = port.Text ?? "",
			["pre_launch"] = pre.Text ?? "",
			["pre_launch_wait"] = preWait.IsChecked == true,
			["post_launch"] = post.Text ?? "",
			["window_title"] = title.Text ?? "",
			["window_mode"] = winMode.SelectedIndex switch
			{
				1 => "fullscreen",
				2 => "maximized",
				_ => "window",
			},
			["login_account"] = (acc.SelectedIndex <= 0) ? "" : acc.Str(),
			["nide8_id"] = nide8.Text ?? "",
			["hidden"] = hidden.IsChecked == true,
		};
		if (int.TryParse(mem.Text?.Trim(), out int memory) && memory > 0)
		{
			payload["memory_mb"] = memory;
		}
		else
		{
			// 【Windows 源】<c>:86-93</c>:空 / 非数字 / 非正数一律写 null(= 用全局内存),不是 0。
			payload["memory_mb"] = null;
		}

		try
		{
			await api.CallAsync("save_version_settings", new
			{
				instance,
				version,
				data = payload,
			});
			AppServices.Toast(I18n.L("已保存"), I18n.L("版本设置已写入"), ToastKind.Success);
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("保存失败"), ex.Message, ToastKind.Error);
		}
	}
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Models;

namespace TipCL.Services;

/// <summary>
/// 账号守卫(= Windows <c>Services\AccountGate.cs</c>,逐行等价搬迁):列出可用的账号名、
/// 保证有一个能用的账号(没有就现开一个离线用户)。启动页与联机页在"要启动游戏 / 要进服务器"
/// 之前问它一句。
///
/// **任务书把它描述成"路由到账号页 / 提示登录"的守卫 —— 实测 Windows 源码不是这样**:
/// 它不导航、不开账号页,而是 (1) <c>IsSentinel</c> 判"这个名字算不算没登录",
/// (2) <c>ListAsync</c> 列出真账号,(3) <c>EnsureAsync</c> 挑一个 / 或者
/// <c>CreateOfflineAsync</c> 弹「创建离线用户」对话框再调后端 <c>add_offline_account</c>。
/// 本包照**源码**搬(与 C2c1 处理"文件名与描述不符"同一条纪律)。
///
/// 公开面与 Windows 一致:<c>IsSentinel</c> / <c>ListAsync</c> / <c>EnsureAsync</c> /
/// <c>CreateOfflineAsync</c> / <c>ValidName</c>(整个类型是 <c>internal</c>,Windows 亦然)。
///
/// mac 侧差异:
/// <list type="number">
/// <item><c>AppServices.Window?.Prefs.LastUser</c> → <c>(AppServices.Window as MainWindow)?.Prefs.LastUser</c>
///   (mac 的 Window 是 Avalonia 的 <c>Window</c>,见 AppServices 的注释);</item>
/// <item><c>AppServices.Window?.Toast(...)</c> → <see cref="AppServices.Toast"/>(C2a 起的统一口径);</item>
/// <item><c>Ui.Field(label, ctl, hint, 78.0)</c> 与 <c>Dlg.Ask</c> 的调用形状与 Windows 一模一样;</item>
/// <item>后端调用收进两个注入缝 <see cref="RowSource"/> / <see cref="AccountCreator"/> —— 红线不许在
///   测试里造 <c>BackendApi</c> / <c>BridgeServer</c>,没有注入缝这条逻辑就一条都测不了。</item>
/// </list>
/// </summary>
internal static class AccountGate
{
	/// <summary>账号行从哪儿来(注入缝;默认 = 后端 <c>get_account_rows</c>)。</summary>
	internal static Func<Task<List<AccountRow>>> RowSource { get; set; } = DefaultRows;

	/// <summary>建离线账号的口子(注入缝):(用户名, 皮肤 id)→ 后端返回的名字。默认 = 后端 <c>add_offline_account</c>。</summary>
	internal static Func<string, string, Task<string>> AccountCreator { get; set; } = DefaultCreator;

	/// <summary>【Windows 源】<c>AccountGate.cs:12-20</c> —— 空名字 / 「离线模式」都算"还没登录"。</summary>
	public static bool IsSentinel(string? name)
	{
		string text = name?.Trim() ?? "";
		if (text.Length != 0 && text != "离线模式")
		{
			return text == I18n.L("离线模式");
		}

		return true;
	}

	/// <summary>【Windows 源】<c>AccountGate.cs:22-28</c> —— 真账号名(去重、激活的在前)。</summary>
	public static async Task<List<string>> ListAsync()
	{
		List<AccountRow> rows = (await RowSource()) ?? new List<AccountRow>();
		return rows.Where((AccountRow r) => !IsSentinel(r.Name))
			.OrderByDescending((AccountRow r) => r.Active)
			.Select((AccountRow r) => r.Name)
			.Distinct()
			.ToList();
	}

	/// <summary>【Windows 源】<c>AccountGate.cs:30-42</c> —— 优先用 <paramref name="preferred"/>,
	/// 否则列表第一个,再没有就现开一个(取消对话框 → null)。</summary>
	public static async Task<string?> EnsureAsync(string? preferred = null)
	{
		List<string> names = await ListAsync();
		if (!IsSentinel(preferred) && names.Contains(preferred!.Trim()))
		{
			return preferred.Trim();
		}

		if (names.Count > 0)
		{
			return names[0];
		}

		return await CreateOfflineAsync();
	}

	/// <summary>【Windows 源】<c>AccountGate.cs:44-90</c> —— 弹「创建离线用户」,名字合法才建,
	/// 建完 Toast 一句并返回后端给的名字。</summary>
	public static async Task<string?> CreateOfflineAsync()
	{
		string typed = (AppServices.Window as MainWindow)?.Prefs.LastUser?.Trim() ?? "";
		if (IsSentinel(typed) || typed == "Player")
		{
			typed = "";
		}

		int skinIndex = 0;
		while (true)
		{
			TextBox name = Ui.Input(I18n.L("例如 Steve"));
			name.Text = typed;
			ComboBox skin = Ui.Combo(new string[3]
			{
				I18n.L("默认"),
				"Steve",
				"Alex"
			});
			skin.SelectedIndex = skinIndex;
			SPanel body = Ui.V(
				8.0,
				Ui.Muted(I18n.L("启动游戏前需要先创建一个离线用户(无正版验证)。")),
				name,
				Ui.Field(I18n.L("默认皮肤"), skin, null, 78.0));
			if (!await Dlg.Ask(I18n.L("创建离线用户"), body, I18n.L("创建并继续")))
			{
				return null;
			}

			typed = name.Text?.Trim() ?? "";
			skinIndex = Math.Max(0, skin.SelectedIndex);
			if (ValidName(typed))
			{
				break;
			}

			AppServices.Toast(I18n.L("用户名不可用"), I18n.L("1–16 个字符,不能含空格"), ToastKind.Warning);
		}

		string created = await AccountCreator(typed, skinIndex switch
		{
			1 => "steve",
			2 => "alex",
			_ => "default",
		});
		created = (string.IsNullOrWhiteSpace(created) ? typed : created);
		AppServices.Toast(I18n.L("已创建离线用户"), created, ToastKind.Success);
		return created;
	}

	/// <summary>【Windows 源】<c>AccountGate.cs:92-99</c> —— 1-16 个字符、不能有空格、不能是哨兵名。</summary>
	public static bool ValidName(string name)
	{
		if (name.Length >= 1 && name.Length <= 16 && !name.Any(char.IsWhiteSpace))
		{
			return !IsSentinel(name);
		}

		return false;
	}

	private static async Task<List<AccountRow>> DefaultRows()
	{
		return (await AppServices.Client.TryCallAsync("get_account_rows", null, new List<AccountRow>())) ?? new List<AccountRow>();
	}

	private static async Task<string> DefaultCreator(string username, string skin)
	{
		return await AppServices.Client.CallAsync<string>("add_offline_account", new { username, skin });
	}
}

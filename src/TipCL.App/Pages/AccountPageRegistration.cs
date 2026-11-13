using System.Runtime.CompilerServices;

namespace TipCL.Pages;

/// <summary>
/// C5b:把「账号」页注册进 <see cref="PageRegistry"/>(页面 id = <c>account</c>,见 <c>MainWindow.NavDefs</c> 里
/// <c>new NavDef("account", I18n.L("账号"), "\ue77b", …)</c> 那一项;mac 外壳的工厂是 <c>null</c>,只认注册表)。
///
/// 与 <see cref="LaunchPageRegistration"/> / <c>InstancePageRegistration</c> / <c>LibraryPageRegistration</c>
/// 同一套 <c>[ModuleInitializer]</c> 口径(D-125),理由见 <see cref="TasksPageRegistration"/>。
///
/// 注意:mac 外壳**没有**顶栏那个账号圆片(C2b 故意删的,见 <c>MainWindow.cs:40</c>),
/// 所以 <c>AccountPage</c> 里 Windows 那句 <c>SyncAccountChipAsync</c> 是有意不搬的(见 DECISIONS 的登记)。
/// </summary>
internal static class AccountPageRegistration
{
	/// <summary>页面 id:与 <c>MainWindow.NavDefs</c> 的 <c>"account"</c> / <c>NavModel.PageForNavKey["account"]</c> 相同。</summary>
	internal const string PageId = "account";

	/// <summary>注册工厂(幂等:<c>Register</c> 对同一 id 是覆盖,重复调用不会抛)。</summary>
	[ModuleInitializer]
	internal static void Register()
	{
		PageRegistry.Register(PageId, () => new AccountPage());
	}
}

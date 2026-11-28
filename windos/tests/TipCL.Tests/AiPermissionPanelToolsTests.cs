using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Ai;
using TipCL.Pages;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-D2 的守卫(2026-11-28):权限面板的「工具」下拉原来是手抄的 34 条,而工具注册表
/// (ToolMeta.ALL + ExtraTools.Meta)已经有 56 条 —— 少掉的那些在面板里根本选不到,
/// 想给它们加规则只能手改 ai_permissions.json。现在改成直接从 ToolMeta.Map 生成。
///
/// 面板本体是 WPF(net8 测试宿主实例化不了控件),所以「面板条目集合 == 注册表工具集合」这条
/// 用两层钉住:1) 清单本身走真实调用(AiPermissionTools.Names());2) 面板的取清单方式是源码文本断言。
/// </summary>
public class AiPermissionPanelToolsTests
{
	[Fact]
	public void PanelToolListEqualsRegistry()
	{
		string[] names = AiPermissionTools.Names();
		Assert.Equal(new HashSet<string>(ToolMeta.Map.Keys), new HashSet<string>(names));
		Assert.Equal(ToolMeta.Map.Count, names.Length);
		Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
		// 下拉顺序要稳定(按名称排序),不然每次启动面板里工具的位置都在跳
		Assert.Equal(names.OrderBy((string n) => n, StringComparer.Ordinal).ToArray(), names);
	}

	[Fact]
	public void PanelToolListCoversTheOldHardcodedThirtyFour()
	{
		// 修之前的硬编码表:这 34 条本来就该在面板里能选到,多出来的 22 条(extra 工具等)是这次补上的
		string[] old = new string[34]
		{
			"ask_user", "create_instance", "delete_instance", "delete_mod", "diagnose_launch", "disable_mod", "download_java", "enable_mod", "get_crash_report", "get_java_list",
			"get_latest_log", "get_launcher_state", "inspect_mod", "install_datapack", "install_game", "install_mod", "install_modpack", "install_resourcepack", "install_shader", "install_world",
			"launch_game", "list_installed_versions", "list_instances", "list_mod_configs", "list_mods", "read_artifact", "read_mod_config", "scan_mod_conflicts", "search_content", "search_modpacks",
			"search_mods", "search_versions", "search_worlds", "write_mod_config"
		};
		string[] names = AiPermissionTools.Names();
		Assert.All(old, (string n) => Assert.Contains(n, names));
		// 注册表 = 38 个基础 + 18 个 extra;只挡变小(以后加工具不用改这里)
		Assert.True(names.Length >= 56, "面板工具数比注册表基线少了:" + names.Length);
	}

	[Fact]
	public void PanelToolListIncludesPreviouslyMissingExtraTools()
	{
		string[] names = AiPermissionTools.Names();
		Assert.Contains("fs_write", names);
		Assert.Contains("tune_jvm", names);
		Assert.Contains("layout_apply", names);
		Assert.Contains("dispatch_subagent", names);
		Assert.Contains("remember", names);
	}

	[Fact]
	public void PanelSourceDerivesToolsFromRegistry()
	{
		string src = RepoFiles.Src("src/TipCL/Pages/AiPermissionPanel.cs");
		Assert.Contains("AiPermissionTools.Names()", src);
		Assert.DoesNotContain("new string[34]", src);
		// 手抄的工具名清单不该再留在面板里(注册表提供什么就是什么)
		Assert.DoesNotContain("\"search_versions\"", src);
		Assert.DoesNotContain("\"write_mod_config\"", src);
	}
}
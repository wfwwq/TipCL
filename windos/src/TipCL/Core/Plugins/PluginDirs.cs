using System;
using System.IO;
using TipCL.Core.Launcher;

namespace TipCL.Core.Plugins;

/// <summary>
/// 插件系统用到的目录（DESIGN §1）。根目录默认是 <see cref="Utils.ROOT"/>（%TIPCL_HOME%），测试可以传别的根。
/// </summary>
public sealed class PluginDirs
{
	public string Home { get; }

	public PluginDirs(string home = null)
	{
		Home = Path.GetFullPath(home ?? Utils.ROOT);
	}

	/// <summary>已安装插件：plugins/&lt;id&gt;/</summary>
	public string PluginsRoot => Path.Combine(Home, "plugins");

	/// <summary>安装中转：plugins/.staging/&lt;id&gt;</summary>
	public string StagingRoot => Path.Combine(PluginsRoot, ".staging");

	/// <summary>更新时旧版本暂存（失败回滚用）：plugins/.backup/&lt;id&gt;</summary>
	public string BackupRoot => Path.Combine(PluginsRoot, ".backup");

	public string DataRoot => Path.Combine(Home, "plugin-data");

	public string LogsRoot => Path.Combine(Home, "plugin-logs");

	public string SecretsRoot => Path.Combine(Home, "plugin-secrets");

	public string StateFile => Path.Combine(Home, "plugins.json");

	public string RuntimeRoot => Path.Combine(Home, "runtime");

	public string NodeDir => Path.Combine(RuntimeRoot, "node");

	public string HostDir => Path.Combine(RuntimeRoot, "plugin-host");

	public string PluginDir(string id)
	{
		return Path.Combine(PluginsRoot, CheckId(id));
	}

	public string DataDir(string id)
	{
		return Path.Combine(DataRoot, CheckId(id));
	}

	public string CacheDir(string id)
	{
		return Path.Combine(DataDir(id), "cache");
	}

	public string AttachmentsDir(string id)
	{
		return Path.Combine(CacheDir(id), "attachments");
	}

	public string LogFile(string id)
	{
		return Path.Combine(LogsRoot, CheckId(id) + ".log");
	}

	public string SecretsFile(string id)
	{
		return Path.Combine(SecretsRoot, CheckId(id) + ".bin");
	}

	/// <summary>宿主级（不属于某个插件）的密钥，例如额外模型供应商的 API Key。</summary>
	public string HostSecretsFile => Path.Combine(SecretsRoot, "_host.bin");

	/// <summary>id 必须先过 <see cref="PluginManifest.IsValidId"/>，防止拼出 ../ 这种路径。</summary>
	private static string CheckId(string id)
	{
		if (!PluginManifest.IsValidId(id))
		{
			throw new ValueError("插件 id 不合法：" + id);
		}
		return id;
	}
}

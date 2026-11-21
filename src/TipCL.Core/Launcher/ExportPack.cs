using System;
using System.Collections.Generic;

namespace TipCL.Core.Launcher;

/// <summary>
/// 界面「导出为整合包」的入口(Api.cs 的 export_modpack 任务调这里)。
/// AI2-modpack:导出逻辑统一收进 <see cref="ModpackExport"/>,界面与 AI 工具 export_modpack 走同一个 Core 函数;
/// 这里保持原签名与原行为(按 sha1 查 Modrinth,找到写 files,找不到打进 overrides;四个覆盖目录全带;覆盖用户选好的文件)。
/// </summary>
public static class ExportPack
{
	public const string MODRINTH_HASH = "https://api.modrinth.com/v2/version_file/{hash}";

	public static readonly string[] OVERRIDE_DIRS = new string[4] { "config", "resourcepacks", "shaderpacks", "datapacks" };

	public static string ExportMrpack(Instance inst, string dest, DownloadManager dm = null, Action<string, long, long> on_note = null)
	{
		PyDict result = ModpackExport.Export(inst, dest, ModpackExportOptions.Legacy(), dm ?? new DownloadManager(4L), on_note);
		return Py.S(result.Get("path"));
	}
}

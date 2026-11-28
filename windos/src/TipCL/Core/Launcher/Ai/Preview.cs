using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

public static class Preview
{
	public const int MAX_PREVIEW_LINES = 200;

	private static string _fmt_bytes(long n)
	{
		if (n >= 1048576)
		{
			return Py.RoundHalfEven((double)n / 1024.0 / 1024.0, 1) + " MB";
		}
		if (n >= 1024)
		{
			return Py.RoundHalfEven((double)n / 1024.0, 1) + " KB";
		}
		return n + " B";
	}

	private static string _instance_dir(BackendApi backend, PyDict args)
	{
		try
		{
			return Tools._inst(backend, args).path;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string _config_target(string instDir, PyDict args)
	{
		if (instDir == null)
		{
			return null;
		}
		string text = Py.LStrip(Py.S(args.Get("path")).Replace("\\", "/"), "/");
		if (text.Length == 0 || new PPath(text).Parts.Contains(".."))
		{
			return null;
		}
		return PPath.Join(instDir, "config", text);
	}

	private static (long n, long total, bool trunc) _dir_stats(string path, int cap = 5000)
	{
		long num = 0L;
		long num2 = 0L;
		bool item = false;
		try
		{
			foreach (string item2 in LocalFs.RglobFiles(path))
			{
				num++;
				try
				{
					num2 += new FileInfo(PPath.Abs(item2)).Length;
				}
				catch (Exception)
				{
				}
				if (num >= cap)
				{
					item = true;
					break;
				}
			}
		}
		catch (Exception)
		{
		}
		return (n: num, total: num2, trunc: item);
	}

	private static PyDict R(string kind, string head, List<string> lines, bool tooLarge)
	{
		return PyDict.Of("kind", kind, "head", head, "lines", lines.Cast<object>().ToList(), "too_large", tooLarge);
	}

	/// <summary>
	/// 确认卡的「改前改后」预览。除了各工具自己的那一段(AI5-D4 补的:fs_write / tune_jvm /
	/// restore_save_backup / layout_apply),还给**写盘但做不了快照**的工具补一句「这一步无法回退:…」
	/// (AI5-D3:账号、联机、备份存档这类工具 AffectedPaths 返回空清单,撤回时还原不了任何东西)。
	/// </summary>
	public static PyDict ChangePreview(BackendApi backend, string name, PyDict args)
	{
		args = args ?? new PyDict();
		PyDict row = _change_preview(backend, name, args);
		string note = Tools.NoRollbackMessage(name);
		if (note.Length == 0)
		{
			return row;
		}
		List<string> list = new List<string>();
		if (row != null)
		{
			foreach (object item in Py.ListOr(row.Get("lines")))
			{
				list.Add(Py.S(item));
			}
		}
		list.Add(note);
		if (row == null)
		{
			return R("info", I18nCore.Tr("将要执行:") + Tools.ConfirmLabel(name, args), list, tooLarge: false);
		}
		return R(Py.S(row.Get("kind")), Py.S(row.Get("head")), list, Py.Truthy(row.Get("too_large")));
	}

	private static PyDict _change_preview(BackendApi backend, string name, PyDict args)
	{
		List<string> list = new List<string>();
		switch (name)
		{
		// AI5-D4:fs_write 的逐行 diff(路径守卫与执行时同一套:ExtraTools.FsResolve)
		case "fs_write":
			return FsWritePreview(backend, args);

		// AI5-D4:tune_jvm 将要写进全局设置的那两项(照 ExtraTools.TuneJvm 的口径算,只算不写)
		case "tune_jvm":
			return TunePreview(backend, args);

		// AI5-D4:回滚存档会把哪个存档目录改成什么样(目标路径与检查点同源:Tools.AffectedPaths)
		case "restore_save_backup":
			return RestorePreview(backend, args);

		// AI5-D4:layout_apply 的布局文档逐行 diff(干跑同一份 ApplyOps,不落盘)
		case LayoutTools.ApplyName:
			return LayoutPreview(args);

		case "write_mod_config":
		{
			string head4 = Tr("将要写入的变更：");
			string text6 = _config_target(_instance_dir(backend, args), args);
			if (text6 == null)
			{
				return null;
			}
			string s = "";
			bool flag = LocalFs.IsFile(text6);
			if (flag)
			{
				try
				{
					s = AiFs.ReadText(text6, replace: true);
				}
				catch (Exception)
				{
					s = "";
				}
			}
			string s2 = Py.S(args.Get("content"));
			string text7 = Py.Str(Py.Or(args.Get("path"), "config"));
			List<string> list2 = UnifiedDiff(Py.SplitLines(s), Py.SplitLines(s2), "a/" + text7, "b/" + text7);
			if (!flag)
			{
				list.Add(Tr("（新建文件）"));
			}
			bool tooLarge = false;
			if (list2.Count > 200)
			{
				tooLarge = true;
				string text8 = Artifacts.Store(string.Join("\n", list2), name);
				list.AddRange(list2.Take(200));
				if (text8.Length > 0)
				{
					list.Add(Tr("变更过大，完整内容见 {path}").Replace("{path}", text8));
				}
				else
				{
					list.Add(Tr("变更过大，仅显示前 {n} 行").Replace("{n}", 200.ToString()));
				}
			}
			else
			{
				list.AddRange(list2);
			}
			return R("diff", head4, list, tooLarge);
		}
		case "delete_mod":
		case "disable_mod":
		case "enable_mod":
		{
			string text9 = _instance_dir(backend, args);
			if (text9 == null)
			{
				return null;
			}
			string text10 = Py.S(args.Get("filename"));
			string text11 = PPath.Join(text9, "mods");
			string head5;
			List<string> list4;
			if (name == "delete_mod")
			{
				head5 = Tr("将被删除：");
				List<string> list3 = new List<string>();
				list3.Add(PPath.Join(text11, text10));
				list3.Add(PPath.Join(text11, text10 + ".disabled"));
				list4 = list3;
			}
			else if (name == "disable_mod")
			{
				head5 = Tr("将被改名：");
				List<string> list5 = new List<string>();
				list5.Add(PPath.Join(text11, text10));
				list4 = list5;
			}
			else
			{
				head5 = Tr("将被改名：");
				List<string> list6 = new List<string>();
				list6.Add(PPath.Join(text11, text10 + ".disabled"));
				list4 = list6;
			}
			long num = 0L;
			long num2 = 0L;
			foreach (string item in list4)
			{
				if (LocalFs.IsFile(item))
				{
					long num3;
					try
					{
						num3 = LocalFs.Size(item);
					}
					catch (Exception)
					{
						num3 = 0L;
					}
					num += num3;
					num2++;
					string name2 = new PPath(item).Name;
					if (name == "delete_mod")
					{
						list.Add("- mods/" + name2 + "（" + _fmt_bytes(num3) + "）");
					}
					else
					{
						string text12 = ((name == "disable_mod") ? (name2 + ".disabled") : (name2.EndsWith(".disabled", StringComparison.Ordinal) ? name2.Substring(0, name2.Length - 9) : name2));
						list.Add("- mods/" + name2 + " → mods/" + text12);
					}
				}
			}
			if (num2 == 0L)
			{
				list.Add(Tr("（没找到这个文件，执行时可能报「不存在」）"));
			}
			else
			{
				list.Add(Tr("共 {n} 个文件，{size}").Replace("{n}", num2.ToString()).Replace("{size}", _fmt_bytes(num)));
			}
			return R("summary", head5, list, tooLarge: false);
		}
		case "delete_instance":
		{
			string text13 = _instance_dir(backend, PyDict.Of("instance", args.Get("name")));
			if (text13 == null)
			{
				return null;
			}
			string head6 = Tr("将被删除：");
			var (num4, n, flag2) = _dir_stats(text13);
			list.Add("- " + new PPath(text13).Name + "/");
			list.Add(Tr("共 {n} 个文件，{size}（不可恢复）").Replace("{n}", (flag2 ? ">" : "") + num4).Replace("{size}", _fmt_bytes(n)));
			return R("summary", head6, list, tooLarge: false);
		}
		default:
			if (new string[8] { "install_mod", "install_modpack", "install_shader", "install_resourcepack", "install_datapack", "install_world", "install_game", "download_java" }.Contains<string>(name))
			{
				string head = Tr("安装目标：");
				string text = _instance_dir(backend, args);
				string text2 = name switch
				{
					"install_world" => "saves", 
					"install_datapack" => "datapacks", 
					"install_resourcepack" => "resourcepacks", 
					"install_shader" => "shaderpacks", 
					"install_mod" => "mods", 
					_ => null, 
				};
				if (name == "download_java")
				{
					list.Add(Tr("Java 运行时目录（由启动器管理）"));
					list.Add(Tr("预计新增文件数：多个（安装时确定）"));
				}
				else
				{
					if (text == null)
					{
						return null;
					}
					list.Add((text2 != null) ? PPath.Join(text, text2) : text);
					switch (name)
					{
					case "install_mod":
					case "install_shader":
					case "install_resourcepack":
					case "install_datapack":
						list.Add(Tr("预计新增文件数：{n}").Replace("{n}", "1"));
						break;
					case "install_world":
						list.Add(Tr("预计新增文件数：存档目录（含 level.dat 等多个文件）"));
						break;
					default:
						list.Add(Tr("预计新增文件数：安装时确定（多于 1 个）"));
						break;
					}
				}
				return R("summary", head, list, tooLarge: false);
			}
			if (name == "create_instance")
			{
				string head2 = Tr("安装目标：");
				string text3 = _instance_dir(backend, new PyDict());
				if (text3 != null)
				{
					list.Add(PPath.Join(new PPath(text3).Parent.ToString(), Py.S(args.Get("name"))));
				}
				list.Add(Tr("预计新增文件数：安装时确定（多于 1 个）"));
				return R("summary", head2, list, tooLarge: false);
			}
			if (name == "launch_game")
			{
				string head3 = Tr("启动目标：");
				string text4 = Py.S(args.Get("instance"));
				string text5 = Py.S(args.Get("version"));
				if (text4.Length > 0)
				{
					list.Add(Tr("实例：{name}").Replace("{name}", text4));
				}
				if (text5.Length > 0)
				{
					list.Add(Tr("版本：{ver}").Replace("{ver}", text5));
				}
				return R("info", head3, list, tooLarge: false);
			}
			return null;
		}
		static string Tr(string key)
		{
			return I18nCore.Tr(key);
		}
	}

	// ───────────────────── 预览覆盖(AI5-D4)─────────────────────

	/// <summary>有专属预览分支的工具(参数不足以定位目标时仍可能返回 null)。</summary>
	private static readonly HashSet<string> _PREVIEW_TOOLS = new HashSet<string>(StringComparer.Ordinal)
	{
		"write_mod_config", "delete_mod", "disable_mod", "enable_mod", "delete_instance", "create_instance",
		"install_mod", "install_modpack", "install_shader", "install_resourcepack", "install_datapack",
		"install_world", "install_game", "download_java", "launch_game",
		// AI5-D4 补齐的四个
		"fs_write", "tune_jvm", "restore_save_backup", LayoutTools.ApplyName
	};

	/// <summary>
	/// 写盘、但没有专属预览的工具 → 理由(AI5-D4 的显式豁免表)。
	/// 这些工具的确认卡不是空的:ChangePreview 会给它们补上 Tools.NoRollbackMessage 那一行
	/// (「这一步无法回退:…」),再由 PreviewCoverageTests 按 ToolMeta 的写工具集合锁住 ——
	/// 新增写工具既没有预览分支、又没登记在这里,用例就红。
	/// </summary>
	private static readonly Dictionary<string, string> _NO_PREVIEW = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "backup_save", "只新增一个 zip:先扫一遍存档体积才谈得上预览,收益不抵开销;卡上有「无法回退」那一行" },
		{ "multiplayer", "开房 / 加入房间是起进程、连房间,不是文件改动,没有可比的东西" },
		{ "add_offline_account", "账号改动由账号面板承接:预览只有「新增一个账号」一句话,增量信息为零" },
		{ "switch_account", "切换账号只改当前选中项,确认卡标题里已经写着切到哪个账号" },
		{ "set_skin", "皮肤是外部 png 写进账号文件,diff 只会显示一段不可读的凭据" },
		{ "remember", "写记忆由记忆面板承接,逐条增删没有可预览的文件改动" },
		{ "forget", "删记忆由记忆面板承接,逐条增删没有可预览的文件改动" }
	};

	/// <summary>这个工具是否有专属预览分支(逐行 diff / 目标清单)。</summary>
	public static bool HasBuiltinPreview(string name)
	{
		return _PREVIEW_TOOLS.Contains(name ?? "");
	}

	/// <summary>没有专属预览的工具的理由(显式豁免表);有预览或不是写工具时返回空串。</summary>
	public static string NoPreviewReason(string name)
	{
		return _NO_PREVIEW.TryGetValue(name ?? "", out string value) ? value : "";
	}

	/// <summary>
	/// fs_write 的逐行 diff(AI5-D4;照 Linux 的 AiFsTools.WritePreview)。
	/// 目标路径与执行时走同一套守卫(ExtraTools.FsResolve):解析不出来就返回 null(执行时也会被拒)。
	/// </summary>
	private static PyDict FsWritePreview(BackendApi backend, PyDict args)
	{
		Instance instance = null;
		try
		{
			instance = Tools._inst(backend, args);
		}
		catch (Exception)
		{
			instance = null;
		}
		string baseDir = ((instance == null) ? null : instance.path);
		if (string.IsNullOrEmpty(baseDir))
		{
			baseDir = Utils.ROOT;
		}
		string target;
		try
		{
			target = ExtraTools.FsResolve(Py.Str(Py.Or(args.Get("path"), "")), ExtraTools.FsRoots(instance), baseDir).Abs();
		}
		catch (Exception)
		{
			return null;
		}
		bool existed = LocalFs.IsFile(target);
		string old = "";
		if (existed)
		{
			try
			{
				old = AiFs.ReadText(target, replace: true);
			}
			catch (Exception)
			{
				old = "";
			}
		}
		string content = Py.S(args.Get("content"));
		bool append = Py.Truthy(args.Get("append"));
		string after = (append ? (old + content) : content);
		string label = Py.Str(Py.Or(args.Get("path"), "file")).Replace('\\', '/');
		List<string> diff = UnifiedDiff(Py.SplitLines(old), Py.SplitLines(after), "a/" + label, "b/" + label);
		List<string> lines = new List<string>();
		if (!existed)
		{
			lines.Add(I18nCore.Tr("(新建文件)"));
		}
		else if (append)
		{
			lines.Add(I18nCore.Tr("(追加到文件末尾)"));
		}
		lines.Add(target);
		bool tooLarge = false;
		if (diff.Count > MAX_PREVIEW_LINES)
		{
			tooLarge = true;
			string stored = Artifacts.Store(string.Join("\n", diff), "fs_write");
			lines.AddRange(diff.Take(MAX_PREVIEW_LINES));
			lines.Add((stored.Length > 0) ? I18nCore.Tr("变更过大,完整内容见 {path}").Replace("{path}", stored) : I18nCore.Tr("变更过大,仅显示前 {n} 行").Replace("{n}", MAX_PREVIEW_LINES.ToString()));
		}
		else
		{
			lines.AddRange(diff);
		}
		return R("diff", I18nCore.Tr("将要写入的变更:"), lines, tooLarge);
	}

	/// <summary>
	/// tune_jvm 将要写进**全局设置**的那两项(AI5-D4;照 ExtraTools.TuneJvm 的口径算,只算不写)。
	/// apply=false 不落盘 → 说清「只给建议」;内存超上限 / gc 名字不合法 → 说清「这次不会执行」。
	/// </summary>
	private static PyDict TunePreview(BackendApi backend, PyDict args)
	{
		Instance instance = null;
		try
		{
			instance = Tools._inst(backend, args);
		}
		catch (Exception)
		{
			instance = null;
		}
		List<string> lines = new List<string>();
		long totalMb = 0L;
		try
		{
			totalMb = Py.Int(Py.Or(Sysinfo.MemoryInfo().Get("total_mb"), 0L));
		}
		catch (Exception)
		{
			totalMb = 0L;
		}
		int mods = ModJarCount(instance);
		long cur = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L));
		string curGc = Py.S(Py.Or(Config.CONFIG.Get("gc_preset"), "auto"));
		long mem = (Py.Truthy(args.Get("memory_mb")) ? Py.Int(args.Get("memory_mb")) : ExtraTools.RecommendMemory(totalMb, mods));
		string gc = (Py.Truthy(args.Get("gc")) ? Py.S(args.Get("gc")) : ExtraTools.RecommendGc(mem, 17));
		if (Py.Truthy(args.Get("gc")) && !Py.In(gc, Gc.LABELS.Keys.Cast<object>().ToList()))
		{
			lines.Add(I18nCore.Tr("gc 只能是 auto / g1 / g1_tuned / zgc / none"));
			return R("info", I18nCore.Tr("这次调优不会执行:"), lines, tooLarge: false);
		}
		if (totalMb > 0 && mem > totalMb - 1024)
		{
			lines.Add(I18nCore.Tr("电脑只有 {total} MB 内存,分配 {want} MB 会让系统卡死,最多建议 {max} MB。")
				.Replace("{total}", totalMb.ToString())
				.Replace("{want}", mem.ToString())
				.Replace("{max}", Math.Max(1024L, totalMb * 7 / 10).ToString()));
			return R("info", I18nCore.Tr("这次调优不会执行:"), lines, tooLarge: false);
		}
		string global = I18nCore.Tr("全局默认");
		if (!Py.Truthy(args.Get("apply")))
		{
			lines.Add(MemLine(mem, global));
			lines.Add(GcLine(gc));
			return R("info", I18nCore.Tr("只给建议,不会改动设置:"), lines, tooLarge: false);
		}
		lines.Add("- " + MemLine(cur, global));
		lines.Add("+ " + MemLine(mem, global));
		lines.Add("- " + GcLine(curGc));
		lines.Add("+ " + GcLine(gc));
		lines.Add(I18nCore.Tr("写入:{path}").Replace("{path}", Config.CONFIG_FILE));
		lines.Add(I18nCore.Tr("单独设置过内存或 GC 的版本仍以它自己的设置为准。"));
		return R("diff", I18nCore.Tr("将要修改的启动设置:"), lines, tooLarge: false);
	}

	private static string MemLine(object mb, string src)
	{
		return I18nCore.Tr("内存:{mb} MB({src})").Replace("{mb}", Py.Str(mb)).Replace("{src}", src);
	}

	private static string GcLine(string key)
	{
		return I18nCore.Tr("GC:{label}").Replace("{label}", I18nCore.Tr(Py.S(Py.Or(Gc.LABELS.Get(key), key))));
	}

	/// <summary>模组数量(与 ExtraTools.ModFiles 同一口径:mods 下的 .jar;那边是 private,这里照数一遍)。</summary>
	private static int ModJarCount(Instance instance)
	{
		try
		{
			if (instance == null)
			{
				return 0;
			}
			string dir = PPath.Join(instance.path, "mods");
			if (!LocalFs.IsDir(dir))
			{
				return 0;
			}
			return LocalFs.Children(dir).Count((string f) => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception)
		{
			return 0;
		}
	}

	/// <summary>
	/// restore_save_backup 会把哪个存档目录改成什么样(AI5-D4)。
	/// 目标路径与检查点同源(Tools.AffectedPaths):覆盖 = 同名存档;不覆盖 = 执行时真正会新建的那个名字。
	/// </summary>
	private static PyDict RestorePreview(BackendApi backend, PyDict args)
	{
		Instance instance;
		try
		{
			instance = Tools._inst(backend, args);
		}
		catch (Exception)
		{
			return null;
		}
		string backup = Py.S(args.Get("backup_name"));
		List<string> targets = Tools.AffectedPaths(instance, "restore_save_backup", args);
		if (backup.Length == 0 || targets.Count == 0)
		{
			return null;
		}
		string version = Tools.VerArg(args);
		string world = new PPath(targets[0]).Name;
		List<string> lines = new List<string>();
		lines.Add(I18nCore.Tr("备份文件:{name}").Replace("{name}", backup));
		try
		{
			if (!LocalFs.IsFile(PPath.Join(Saves.BackupsDir(instance, version), backup)))
			{
				lines.Add(I18nCore.Tr("(没找到这个备份,执行时会报「备份不存在」)"));
			}
		}
		catch (Exception)
		{
		}
		lines.Add(I18nCore.Tr("目标存档:{name}").Replace("{name}", world));
		bool overwrite = Py.Truthy(args.Get("overwrite"));
		lines.Add(overwrite ? I18nCore.Tr("覆盖现有存档") : I18nCore.Tr("恢复成新的存档文件夹,不动现有存档"));
		if (overwrite)
		{
			try
			{
				if (Saves.ListSaves(instance, version).OfType<PyDict>().Any((PyDict s) => Py.S(s.Get("name")) == world))
				{
					lines.Add(I18nCore.Tr("回滚前先把当前存档「{name}」备份成 zip").Replace("{name}", world));
				}
			}
			catch (Exception)
			{
			}
		}
		return R("summary", I18nCore.Tr("将要从备份回滚存档:"), lines, tooLarge: false);
	}

	/// <summary>
	/// layout_apply 的布局文档逐行 diff(AI5-D4):在副本上干跑同一份 ApplyOps(LayoutTools.PreviewPlan),
	/// 拿改前 / 改后的整份 v2 文档做 diff —— 算不出来就说清这次为什么改不了,不落盘。
	/// </summary>
	private static PyDict LayoutPreview(PyDict args)
	{
		PyDict plan = LayoutTools.PreviewPlan(args);
		List<string> lines = new List<string>();
		string error = Py.S(plan.Get("error"));
		List<object> changes = Py.ListOr(plan.Get("changes"));
		if (error.Length > 0 || changes.Count == 0)
		{
			// 干跑认不出任何改动 / 参数用不了:照 LayoutTools.Apply 的原话显示,不要给一份空 diff
			lines.Add((error.Length > 0) ? error : I18nCore.Tr("没有识别到要修改的内容:请给 page+blocks(改某一页)、profile(应用方案)、json(整份文档)、widgets 或 shell_tabs 中的一个。"));
			return R("info", I18nCore.Tr("这次改动用不了:"), lines, tooLarge: false);
		}
		foreach (object item in changes)
		{
			lines.Add("- " + Py.S(item));
		}
		lines.Add(I18nCore.Tr("改动前会自动备份为方案「AI 备份 <时间>」"));
		List<string> diff = UnifiedDiff(Py.SplitLines(Py.S(plan.Get("before"))), Py.SplitLines(Py.S(plan.Get("after"))), "a/ui_layout_v2.json", "b/ui_layout_v2.json");
		bool tooLarge = diff.Count > MAX_PREVIEW_LINES;
		lines.AddRange(diff.Take(MAX_PREVIEW_LINES));
		if (tooLarge)
		{
			lines.Add(I18nCore.Tr("变更过大,仅显示前 {n} 行").Replace("{n}", MAX_PREVIEW_LINES.ToString()));
		}
		return R("diff", I18nCore.Tr("将要调整的布局:"), lines, tooLarge);
	}

	private static List<int[]> MatchingBlocks(List<string> a, List<string> b)
	{
		int count = a.Count;
		int count2 = b.Count;
		Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>(StringComparer.Ordinal);
		for (int i = 0; i < count2; i++)
		{
			if (!dictionary.TryGetValue(b[i], out var value))
			{
				List<int> list = (dictionary[b[i]] = new List<int>());
				value = list;
			}
			value.Add(i);
		}
		if (count2 >= 200)
		{
			int ntest = count2 / 100 + 1;
			foreach (string item in dictionary.Where(delegate(KeyValuePair<string, List<int>> kv)
			{
				KeyValuePair<string, List<int>> keyValuePair = kv;
				return keyValuePair.Value.Count > ntest;
			}).Select(delegate(KeyValuePair<string, List<int>> kv)
			{
				KeyValuePair<string, List<int>> keyValuePair = kv;
				return keyValuePair.Key;
			}).ToList())
			{
				dictionary.Remove(item);
			}
		}
		List<int> list3 = new List<int>();
		List<int[]> list4 = new List<int[]>();
		list4.Add(new int[4] { 0, count, 0, count2 });
		List<int[]> list5 = list4;
		List<int[]> list6 = new List<int[]>();
		while (list5.Count > 0)
		{
			int[] array = list5[list5.Count - 1];
			list5.RemoveAt(list5.Count - 1);
			int num = array[0];
			int num2 = array[1];
			int num3 = array[2];
			int num4 = array[3];
			int num5 = num;
			int num6 = num3;
			int num7 = 0;
			Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
			for (int num8 = num; num8 < num2; num8++)
			{
				Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
				if (!dictionary.TryGetValue(a[num8], out var value2))
				{
					value2 = list3;
				}
				foreach (int item2 in value2)
				{
					if (item2 >= num3)
					{
						if (item2 >= num4)
						{
							break;
						}
						dictionary2.TryGetValue(item2 - 1, out var value3);
						int num9 = (dictionary3[item2] = value3 + 1);
						int num11 = num9;
						if (num11 > num7)
						{
							num5 = num8 - num11 + 1;
							num6 = item2 - num11 + 1;
							num7 = num11;
						}
					}
				}
				dictionary2 = dictionary3;
			}
			while (num5 > num && num6 > num3 && a[num5 - 1] == b[num6 - 1])
			{
				num5--;
				num6--;
				num7++;
			}
			for (; num5 + num7 < num2 && num6 + num7 < num4 && a[num5 + num7] == b[num6 + num7]; num7++)
			{
			}
			if (num7 > 0)
			{
				list6.Add(new int[3] { num5, num6, num7 });
				if (num < num5 && num3 < num6)
				{
					list5.Add(new int[4] { num, num5, num3, num6 });
				}
				if (num5 + num7 < num2 && num6 + num7 < num4)
				{
					list5.Add(new int[4]
					{
						num5 + num7,
						num2,
						num6 + num7,
						num4
					});
				}
			}
		}
		list6.Sort(delegate(int[] x, int[] y)
		{
			if (x[0] != y[0])
			{
				return x[0].CompareTo(y[0]);
			}
			return (x[1] != y[1]) ? x[1].CompareTo(y[1]) : x[2].CompareTo(y[2]);
		});
		int num12 = 0;
		int num13 = 0;
		int num14 = 0;
		List<int[]> list7 = new List<int[]>();
		foreach (int[] item3 in list6)
		{
			if (num12 + num14 == item3[0] && num13 + num14 == item3[1])
			{
				num14 += item3[2];
				continue;
			}
			if (num14 > 0)
			{
				list7.Add(new int[3] { num12, num13, num14 });
			}
			num12 = item3[0];
			num13 = item3[1];
			num14 = item3[2];
		}
		if (num14 > 0)
		{
			list7.Add(new int[3] { num12, num13, num14 });
		}
		list7.Add(new int[3] { count, count2, 0 });
		return list7;
	}

	private static List<(string tag, int i1, int i2, int j1, int j2)> Opcodes(List<string> a, List<string> b)
	{
		int num = 0;
		int num2 = 0;
		List<(string, int, int, int, int)> list = new List<(string, int, int, int, int)>();
		foreach (int[] item in MatchingBlocks(a, b))
		{
			int num3 = item[0];
			int num4 = item[1];
			int num5 = item[2];
			string text = ((num < num3 && num2 < num4) ? "replace" : ((num < num3) ? "delete" : ((num2 < num4) ? "insert" : "")));
			if (text.Length > 0)
			{
				list.Add((text, num, num3, num2, num4));
			}
			num = num3 + num5;
			num2 = num4 + num5;
			if (num5 > 0)
			{
				list.Add(("equal", num3, num, num4, num2));
			}
		}
		return list;
	}

	private static string _range(int start, int stop)
	{
		int num = start + 1;
		int num2 = stop - start;
		switch (num2)
		{
		case 1:
			return num.ToString();
		case 0:
			num--;
			break;
		}
		return num + "," + num2;
	}

	public static List<string> UnifiedDiff(List<string> a, List<string> b, string fromfile, string tofile, int n = 3)
	{
		List<(string, int, int, int, int)> list = Opcodes(a, b);
		if (list.Count == 0)
		{
			list.Add(("equal", 0, 1, 0, 1));
		}
		if (list[0].Item1 == "equal")
		{
			(string, int, int, int, int) tuple = list[0];
			list[0] = (tuple.Item1, Math.Max(tuple.Item2, tuple.Item3 - n), tuple.Item3, Math.Max(tuple.Item4, tuple.Item5 - n), tuple.Item5);
		}
		if (list[list.Count - 1].Item1 == "equal")
		{
			(string, int, int, int, int) tuple2 = list[list.Count - 1];
			list[list.Count - 1] = (tuple2.Item1, tuple2.Item2, Math.Min(tuple2.Item3, tuple2.Item2 + n), tuple2.Item4, Math.Min(tuple2.Item5, tuple2.Item4 + n));
		}
		int num = n + n;
		List<List<(string, int, int, int, int)>> list2 = new List<List<(string, int, int, int, int)>>();
		List<(string, int, int, int, int)> list3 = new List<(string, int, int, int, int)>();
		foreach (var item in list)
		{
			var (text, num2, num3, num4, num5) = item;
			if (text == "equal" && num3 - num2 > num)
			{
				list3.Add((text, num2, Math.Min(num3, num2 + n), num4, Math.Min(num5, num4 + n)));
				list2.Add(list3);
				list3 = new List<(string, int, int, int, int)>();
				num2 = Math.Max(num2, num3 - n);
				num4 = Math.Max(num4, num5 - n);
			}
			list3.Add((text, num2, num3, num4, num5));
		}
		if (list3.Count > 0 && (list3.Count != 1 || !(list3[0].Item1 == "equal")))
		{
			list2.Add(list3);
		}
		List<string> list4 = new List<string>();
		bool flag = false;
		foreach (List<(string, int, int, int, int)> item2 in list2)
		{
			if (!flag)
			{
				flag = true;
				list4.Add("--- " + fromfile);
				list4.Add("+++ " + tofile);
			}
			list4.Add("@@ -" + _range(item2[0].Item2, item2[item2.Count - 1].Item3) + " +" + _range(item2[0].Item4, item2[item2.Count - 1].Item5) + " @@");
			foreach (var (text2, num6, num7, num8, num9) in item2)
			{
				switch (text2)
				{
				case "equal":
				{
					for (int j = num6; j < num7; j++)
					{
						list4.Add(" " + a[j]);
					}
					continue;
				}
				case "replace":
				case "delete":
				{
					for (int i = num6; i < num7; i++)
					{
						list4.Add("-" + a[i]);
					}
					break;
				}
				}
				if (text2 == "replace" || text2 == "insert")
				{
					for (int k = num8; k < num9; k++)
					{
						list4.Add("+" + b[k]);
					}
				}
			}
		}
		return list4;
	}
}

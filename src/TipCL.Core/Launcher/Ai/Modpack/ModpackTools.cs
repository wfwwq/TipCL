// i18n:ignore-file —— 本文件的中文都是给模型看的:工具 schema 的 description、工具结果里的 message / reason / next。
// 界面上能看到的标签、确认卡预览、计划卡步骤在 ModpackPreview.cs,那边一律走 I18nCore.Tr。
//
// AI2-modpack:整合包制作这一包工具的**表数据与参数解析**(schema、名单、检查点路径、导出选项、批量安装)。
//   export_modpack            写本地文件(medium,long_running;不给「本对话都允许」)
//   check_modpack_distribution 只读 + 联网
//   plan_modpack              只读 + 联网(出候选清单 + 计划卡)
//   install_mods_batch        写本地文件(high,long_running;整批确认一次,不给「本对话都允许」)
// 执行分发在 ToolRunner.Modpack.cs(partial);共享文件(Tools / ToolMeta / ToolRunner / Preview / AiData)里只各留一行转发。
using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>整合包制作工具的静态表与辅助逻辑。</summary>
public static class ModpackTools
{
    /// <summary>本包负责的全部工具名(声明顺序 = schema 顺序)。</summary>
    public static readonly string[] Names = { "check_modpack_distribution", "export_modpack", "plan_modpack", "install_mods_batch" };

    /// <summary>一次批量安装最多这么多个模组。</summary>
    public const int MAX_BATCH = 60;

    private const string SchemasJson = """
        [
        {"type":"function","function":{"name":"check_modpack_distribution","description":"导出整合包前检查每个模组能不能分发(只读,联网):Modrinth 按 sha1 找得到的写成下载链接;CurseForge 查作者是否允许第三方分发(allowModDistribution);两边都找不到下载源的本地 jar 只能打进包里(可能侵权)。返回 可链接(linkable)/ 需打包(bundle)/ 禁止分发(forbidden)三类清单","parameters":{"type":"object","properties":{"instance":{"type":"string"},"format":{"type":"string","description":"mrpack / curseforge,默认 mrpack"}}}}},
        {"type":"function","function":{"name":"export_modpack","description":"把实例导出成整合包文件(写本地文件,需确认)。导出前自动做分发许可检查:有禁止分发的模组会拒绝导出(先替换、禁用或用 exclude 排除);来源不明的 jar 默认不打包,只有用户明确同意才传 allow_bundled_jars=true。确认卡会列出将写入的文件清单和大小。返回导出路径","parameters":{"type":"object","properties":{"instance":{"type":"string"},"format":{"type":"string","description":"mrpack(Modrinth)/ curseforge(CurseForge zip),默认 mrpack"},"name":{"type":"string","description":"整合包名,空则用实例名"},"version":{"type":"string","description":"整合包版本号,默认 1.0.0"},"summary":{"type":"string","description":"一句话简介"},"author":{"type":"string","description":"作者(CurseForge 清单里用)"},"icon":{"type":"string","description":"可选,png 图标文件路径"},"include_overrides":{"type":"array","items":{"type":"string"},"description":"打进包里的目录,config / resourcepacks / shaderpacks / datapacks 的子集,默认全部。config 就是当前实例的配置预设,用户不想带就去掉"},"allow_bundled_jars":{"type":"boolean","description":"把来源不明 / 许可未知的 jar 打进包(可能侵权)。只有用户明确同意时才设 true,默认 false"},"exclude":{"type":"array","items":{"type":"string"},"description":"不放进包里的模组文件名(如禁止分发的)"},"dest":{"type":"string","description":"导出目录,空则用启动器的导出目录"}}}}},
        {"type":"function","function":{"name":"plan_modpack","description":"一句话造包的第一步(只读,联网):按 MC 版本、加载器、主题描述和规模,在 Modrinth 与 CurseForge 上挑候选模组清单(两边按下载量和版本匹配合并去重,同一模组有 Modrinth 版本时优先 Modrinth;连不上 CurseForge 时只用 Modrinth 并在结果里注明),每项带入选理由、必需前置、与清单里其他模组的冲突提示,并在界面上展示后续步骤计划。只出清单,不建实例、不装任何东西","parameters":{"type":"object","properties":{"mc_version":{"type":"string","description":"如 1.20.1"},"loader":{"type":"string","description":"fabric / forge / neoforge / quilt"},"theme":{"type":"string","description":"主题描述,如「轻量科技 + 优化」"},"size":{"type":"string","description":"small(约 8 个)/ medium(约 20 个)/ large(约 40 个),默认 medium;主题里写了轻量时默认 small"},"keywords":{"type":"array","items":{"type":"string"},"description":"可选:额外的英文搜索词(主题里有分类认不出的玩法时用,如 create、farming)"}},"required":["mc_version","loader","theme"]}}},
        {"type":"function","function":{"name":"install_mods_batch","description":"把用户已经确认的一批模组一次装进同一个实例(需确认:确认卡逐项列出每个模组,整批只确认一次;必需前置随装)。在后台逐个安装,立刻返回 task_id(任务卡显示第几个、成功还是失败),用 get_task_status 查进度与逐项结果。只在用户确认清单之后调用,mods 用 plan_modpack / search_mods 结果里的 slug(Modrinth)或 id(CurseForge)","parameters":{"type":"object","properties":{"instance":{"type":"string"},"mods":{"type":"array","description":"要装的模组,每项 {slug, name, source, id}","items":{"type":"object","properties":{"slug":{"type":"string"},"name":{"type":"string"},"source":{"type":"string","description":"modrinth / curseforge,默认 modrinth"},"id":{"type":"string","description":"CurseForge 数字 id"}}}},"mc_version":{"type":"string","description":"可选,空则按实例已装版本"},"loader":{"type":"string","description":"可选,空则按实例已装加载器"}},"required":["instance","mods"]}}}
        ]
        """;

    private static List<object>? _schemas;

    /// <summary>本包全部 schema(每次给浅拷贝:调用方会把它并进自己的列表)。</summary>
    public static List<object> Schemas()
    {
        List<object>? cached = _schemas;
        if (cached == null)
        {
            cached = Py.ListOr(PyJson.Loads(SchemasJson));
            _schemas = cached;
        }
        return new List<object>(cached);
    }

    /// <summary>是否本包负责的工具。</summary>
    public static bool Handles(string? name) => name != null && Array.IndexOf(Names, name) >= 0;

    private static string InstanceOf(PyDict args, string instance)
    {
        string name = Py.S(Py.Or(args.Get("instance"), instance));
        return name.Length > 0 ? name : Py.S(Config.CONFIG.Get("default_instance", "default"));
    }

    // ───────────────────────────── 导出参数 ─────────────────────────────

    private static List<string> StrList(object? raw)
    {
        if (raw is string s)
        {
            return s.Split(new[] { ',', ' ', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
        return Py.ListOr(raw).Select(x => Py.Strip(Py.S(x))).Where(x => x.Length > 0).ToList();
    }

    /// <summary>export_modpack 的参数 → 导出选项(恒做分发许可检查、不覆盖已有文件)。</summary>
    public static ModpackExportOptions OptionsFrom(PyDict args)
    {
        var opt = new ModpackExportOptions
        {
            Format = ModpackDistribution.NormalizeFormat(Py.S(args.Get("format"))),
            Name = Py.S(args.Get("name")),
            Version = Py.S(args.Get("version")),
            Summary = Py.S(args.Get("summary")),
            Author = Py.S(args.Get("author")),
            IconPath = Py.S(args.Get("icon")),
            CheckDistribution = true,
            AllowBundledJars = Py.Truthy(args.Get("allow_bundled_jars")),
            Overwrite = false,
        };
        if (args.ContainsKey("include_overrides") && args.Get("include_overrides") != null)
        {
            List<string> dirs = StrList(args.Get("include_overrides")).Select(x => Py.Lower(x)).ToList();
            var bad = dirs.Where(d => !ExportPack.OVERRIDE_DIRS.Contains(d)).ToList();
            if (bad.Count > 0)
            {
                throw new ValueError("include_overrides 只能是 " + string.Join(" / ", ExportPack.OVERRIDE_DIRS) + " 的子集,不认识: " + string.Join(", ", bad));
            }
            opt.Overrides = dirs;
        }
        foreach (string f in StrList(args.Get("exclude")))
        {
            opt.Exclude.Add(f);
        }
        return opt;
    }

    /// <summary>
    /// 导出到哪(写进 args["_out_path"],之后检查点 / 预览 / 执行都用同一个)。dest 为空 → 启动器的导出目录;
    /// dest 以 .mrpack / .zip 结尾时当作文件名;重名自动加序号,绝不覆盖。
    /// </summary>
    public static string ResolveOutPath(PyDict args, string instance)
    {
        string cached = Py.S(args.Get("_out_path"));
        if (cached.Length > 0)
        {
            return cached;
        }
        string instName = InstanceOf(args, instance);
        var inst = new Instance(instName);
        string format = ModpackDistribution.NormalizeFormat(Py.S(args.Get("format")));
        (string name, string version) = ModpackExport.NameVersion(inst, Py.S(args.Get("name")), Py.S(args.Get("version")));
        string dest = Py.Strip(Py.S(args.Get("dest")));
        string path;
        if (dest.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            PPath p = new PPath(PPath.Norm(dest));
            path = ModpackExport.UniquePath(p.Parent.ToString(), p.Name);
        }
        else
        {
            string dir = dest.Length > 0 ? PPath.Norm(dest) : ContentExport.DefaultExportDir();
            path = ModpackExport.UniquePath(dir, ModpackExport.DefaultFileName(name, version, format));
        }
        args["_out_path"] = path;
        return path;
    }

    /// <summary>检查点要快照的路径:导出 = 新文件(此刻不存在,回滚即删掉);批量安装 = 实例 mods 目录。</summary>
    public static List<string> AffectedPaths(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        try
        {
            switch (name)
            {
                case "export_modpack":
                    return new List<string> { ResolveOutPath(args, instance) };
                case "install_mods_batch":
                    return new List<string> { PPath.Join(Instances.GetInstancePath(InstanceOf(args, instance)), "mods") };
            }
        }
        catch (Exception)
        {
        }
        return new List<string>();
    }

    /// <summary>给模型看的检查结果:去掉 sha512 / 下载地址这类长字段,只留判断要用的。</summary>
    public static PyDict CompactCheck(PyDict result)
    {
        List<object> Slim(object? list) => Py.ListOr(list).Select(x =>
        {
            PyDict e = Py.D(x);
            var row = PyDict.Of(
                "filename", Py.S(e.Get("filename")),
                "name", Py.S(e.Get("name")),
                "source", Py.S(e.Get("source")),
                "needs_consent", Py.Truthy(e.Get("needs_consent")),
                "reason", Py.S(e.Get("reason")));
            if (e.Get("allow_distribution") is bool allow)
            {
                row["allow_distribution"] = allow;
            }
            return (object)row;
        }).ToList();
        return PyDict.Of(
            "ok", true,
            "instance", result.Get("instance"),
            "format", result.Get("format"),
            "total", result.Get("total"),
            "truncated", result.Get("truncated"),
            "summary", result.Get("summary"),
            "linkable", Slim(result.Get("linkable")),
            "bundle", Slim(result.Get("bundle")),
            "forbidden", Slim(result.Get("forbidden")),
            "lookup_errors", result.Get("lookup_errors"),
            "advice", result.Get("advice"));
    }

    // ───────────────────────────── 批量安装 ─────────────────────────────

    /// <summary>
    /// 测试注入点:装一个模组(实例, 模组项 {slug,name,source,id}, mc 或 null, loader 或 null)→ {files:[...]}。
    /// 空 = 默认实现(与 install_mod 背后同一套 Core:Mods.InstallModrinthMod / Mods.InstallCurseforgeMod,必需前置随装)。
    /// </summary>
    public static Func<Instance, PyDict, string?, string?, PyDict>? InstallerHook;

    private static PyDict DefaultInstall(Instance inst, PyDict mod, string? mc, string? loader)
    {
        var dm = new DownloadManager(4L);
        string source = Py.Lower(Py.S(mod.Get("source")));
        if (source.StartsWith("curse", StringComparison.Ordinal))
        {
            return Mods.InstallCurseforgeMod(dm, Py.S(mod.Get("id")), inst, mc, loader);
        }
        string slug = Py.S(Py.Or(mod.Get("slug"), mod.Get("id"), mod.Get("name")));
        return Mods.InstallModrinthMod(dm, slug, inst, mc, loader);
    }

    /// <summary>mods 参数 → 去重后的模组项(字符串当 slug)。</summary>
    public static List<PyDict> BatchItems(PyDict args)
    {
        var items = new List<PyDict>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (object raw in Py.ListOr(args.Get("mods")))
        {
            PyDict mod = raw is string s ? PyDict.Of("slug", s, "name", s) : new PyDict(Py.D(raw));
            string source = Py.Lower(Py.S(mod.Get("source")));
            bool curse = source.StartsWith("curse", StringComparison.Ordinal);
            string key = curse ? "cf:" + Py.S(mod.Get("id")) : "mr:" + Py.S(Py.Or(mod.Get("slug"), mod.Get("id"), mod.Get("name")));
            if (key.EndsWith(":", StringComparison.Ordinal) || !seen.Add(key))
            {
                continue;
            }
            mod["source"] = curse ? "curseforge" : "modrinth";
            items.Add(mod);
        }
        return items;
    }

    /// <summary>显示名(name 优先,其次 slug / id)。</summary>
    public static string ItemName(PyDict mod) => Py.S(Py.Or(mod.Get("name"), mod.Get("slug"), mod.Get("id")));

    /// <summary>
    /// AI3-content:批量安装的一条进度事件。Phase = start(开始装第 Index 个)/ done(第 Index 个装完,Ok 表示成功,失败时 Error 是原因)。
    /// Index 从 1 数,Total 是去重后的总数。
    /// </summary>
    public sealed record BatchEvent(string Phase, int Index, int Total, string Name, bool Ok, string Error);

    /// <summary>mods 参数校验(空 / 超上限直接报错,什么都不装)→ 去重后的模组项。</summary>
    public static List<PyDict> CheckBatch(PyDict args)
    {
        List<PyDict> items = BatchItems(args);
        if (items.Count == 0)
        {
            throw new ValueError("mods 为空:要装的模组每项要有 slug(Modrinth)或 id(CurseForge)");
        }
        if (items.Count > MAX_BATCH)
        {
            throw new ValueError("一次最多装 " + MAX_BATCH + " 个模组,请分批");
        }
        return items;
    }

    /// <summary>宿主(启动器的任务表)用:按参数里的 instance 找到实例,不存在报错。</summary>
    public static Instance BatchInstance(PyDict args)
    {
        string name = InstanceOf(args, "");
        var inst = new Instance(name.Length > 0 ? name : null);
        if (!LocalFs.IsDir(inst.path))
        {
            throw new FileNotFoundError("实例不存在: " + name);
        }
        return inst;
    }

    /// <summary>后台任务已经起来时回给模型的结果(逐项列出要装的模组,告诉它怎么等结果)。</summary>
    public static PyDict BatchStarted(string taskId, Instance inst, List<PyDict> items) => PyDict.Of(
        "ok", true,
        "background", true,
        "task_id", taskId,
        "instance", inst.name,
        "total", (long)items.Count,
        "mods", items.Select(m => (object)PyDict.Of("name", ItemName(m), "slug", Py.S(m.Get("slug")), "id", Py.S(m.Get("id")), "source", Py.S(m.Get("source")))).ToList(),
        "message", "已在后台开始逐个安装 " + items.Count + " 个模组(任务卡上能看到第几个、每个成功还是失败),不用干等。",
        "next", "用 get_task_status(task_id=" + taskId + ", wait_seconds=60) 等它结束:status 是汇总,log 里逐项是 ✓ / ✗ 及失败原因。"
            + "结束前不要再对同一实例调 install_mods_batch;结束后把没装上的告诉用户,再 scan_mod_conflicts + preflight_check。");

    /// <summary>install_mods_batch 的执行体(已过确认):逐个装,单个失败不中断,最后一起报告。</summary>
    /// <param name="progress">AI3-content:逐项进度(开始 / 成功 / 失败),后台任务用它更新任务卡。</param>
    /// <param name="ct">取消:在两个模组之间检查(正在装的那个会装完)。</param>
    public static PyDict InstallBatch(Instance inst, PyDict args, Action<string>? log = null, Action<BatchEvent>? progress = null, System.Threading.CancellationToken ct = default)
    {
        List<PyDict> items = CheckBatch(args);
        string mc = Py.Strip(Py.S(args.Get("mc_version")));
        string loader = InstanceUpgrade.NormalizeLoader(Py.S(args.Get("loader")));
        Func<Instance, PyDict, string?, string?, PyDict> install = InstallerHook ?? DefaultInstall;
        var installed = new List<object>();
        var failed = new List<object>();
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PyDict mod = items[i];
            string label = ItemName(mod);
            log?.Invoke(ModpackPreview.BatchProgress(i + 1, items.Count, label));
            progress?.Invoke(new BatchEvent("start", i + 1, items.Count, label, false, ""));
            try
            {
                PyDict r = install(inst, mod, mc.Length > 0 ? mc : null, loader.Length > 0 ? loader : null);
                installed.Add(PyDict.Of("name", label, "slug", Py.S(mod.Get("slug")), "source", Py.S(mod.Get("source")),
                    "files", Py.ListOr(r.Get("files"))));
                progress?.Invoke(new BatchEvent("done", i + 1, items.Count, label, true, ""));
            }
            catch (Exception e)
            {
                failed.Add(PyDict.Of("name", label, "slug", Py.S(mod.Get("slug")), "error", Py.ExcStr(e)));
                progress?.Invoke(new BatchEvent("done", i + 1, items.Count, label, false, Py.ExcStr(e)));
            }
        }
        return PyDict.Of(
            "ok", failed.Count == 0,
            "instance", inst.name,
            "total", (long)items.Count,
            "installed", installed,
            "failed", failed,
            "next", failed.Count == 0
                ? "装完了:接着 scan_mod_conflicts + preflight_check 复核,有问题先 ask_user 再改。"
                : "有 " + failed.Count + " 个没装上(见 failed):把原因告诉用户,问要不要换同类模组或跳过,再 scan_mod_conflicts + preflight_check。");
    }
}

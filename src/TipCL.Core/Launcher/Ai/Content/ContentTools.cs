// AI2-content:无代码内容制作 —— create_datapack / create_resourcepack / write_kubejs_script / validate_content 的实现。
// i18n:ignore-file —— 本文件的中文只出现在回给模型的工具结果 / 报错与 schema 描述里(工具结果是数据,不是界面文案,
// 同 AiFsTools.cs 口径);确认卡上给用户看的标签与预览在 ContentPreview.cs,一律走 I18nCore.Tr。
//
// 为什么不放宽 fs_write:fs_write 的白名单是「什么文本都能写」,所以必须拒脚本、拒存档;这里换成专用工具,
// 每个工具只写一个很窄的位置,且写之前按类型做结构校验:
//   · create_datapack      → <游戏目录>/saves/<存档>/datapacks/<包名>/(pack.mcmeta + data/<ns>/…)
//   · create_resourcepack  → <游戏目录>/resourcepacks/<包名>/(pack.mcmeta + assets/<ns>/{lang,models,blockstates,textures}/…)
//   · write_kubejs_script  → <游戏目录>/kubejs/{server,client,startup}_scripts/<名字>.js(实例必须真装了 KubeJS)
//   · validate_content     → 只读:从 latest.log(和 KubeJS 自己的日志)里找加载错误
// 执行、确认卡预览、检查点三处用同一份「计划」(Plan):先算出要写哪些文件、写什么,再交给各自去用 ——
// 不会出现「预览给用户看的是 A、实际写的是 B、检查点快照的是 C」。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>数据包 / 资源包 / KubeJS 脚本工具。</summary>
public static class ContentTools
{
    /// <summary>本包的工具名(写三个 + 只读一个)。</summary>
    public static readonly string[] Names = { "create_datapack", "create_resourcepack", "write_kubejs_script", "validate_content" };

    public static bool Handles(string name) => Names.Contains(name);

    /// <summary>一次最多写这么多个文件。</summary>
    public const int MAX_FILES = 64;

    /// <summary>单个文本文件上限(256 KB,同 fs_write)。</summary>
    public const int MAX_FILE_BYTES = 256 * 1024;

    /// <summary>一次调用写入总量上限(含贴图)。</summary>
    public const int MAX_TOTAL_BYTES = 8 * 1024 * 1024;

    /// <summary>KubeJS 单个脚本上限(64 KB:正常脚本远小于此,再大多半是贴了不该贴的东西)。</summary>
    public const int MAX_SCRIPT_BYTES = 64 * 1024;

    /// <summary>KubeJS 三类脚本目录。</summary>
    public static readonly string[] ScriptDirs = { "server_scripts", "client_scripts", "startup_scripts" };

    private static readonly Regex ScriptNameRx = new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    // ───────────────────────────── 计划 ─────────────────────────────

    /// <summary>计划里的一个文件:目标、包内相对路径、新内容(文本或字节)、改前内容。</summary>
    public sealed class Write
    {
        public string Path = "";
        public string Rel = "";
        public string? Text;
        public byte[]? Bytes;
        public bool Existed;
        public string? OldText;
        public bool Changed;
        public int Width;
        public int Height;
    }

    /// <summary>一次调用要做的全部事(执行 / 预览 / 检查点共用)。</summary>
    public sealed class Plan
    {
        public string Tool = "";
        public string Root = "";
        public string Mc = "";
        public string McSource = "";
        public string Format = "";
        public bool Update;
        public bool RootExisted;
        public readonly List<Write> Writes = new();
        public readonly List<string> Notes = new();

        public IEnumerable<Write> Changed => Writes.Where(w => w.Changed);
    }

    private sealed record Ctx(Instance Inst, string Version, string GameDir, PyDict Folders);

    private static Ctx Context(PyDict args, string instance)
    {
        string version = Py.S(args.Get("version")).Trim();
        Instance inst = AiFsTools.ResolveInstance(args, instance);
        if (!LocalFs.IsDir(inst.path))
        {
            throw new FileNotFoundError("实例不存在: " + inst.name);
        }
        if (version.Length > 0 && !inst.HasVersion(version))
        {
            throw new FileNotFoundError("实例 " + inst.name + " 里没有版本 " + version + "(可先用 list_installed_versions 查)");
        }
        PyDict folders = VersionOps.FolderMap(inst, version.Length > 0 ? version : null);
        return new Ctx(inst, version, PPath.Abs(Py.S(folders.Get("game"))), folders);
    }

    /// <summary>
    /// 这次内容对应的 MC 版本:参数 mc_version 优先;其次从 version(版本 id 或其 inheritsFrom)里读;
    /// 再次看实例元信息 / 已装版本 —— 实例里装了多个不同 MC 版本又没指定时拒绝(不替用户挑)。
    /// </summary>
    public static (string mc, string source) ResolveMc(PyDict args, Instance inst, string version)
    {
        string given = Py.S(args.Get("mc_version")).Trim();
        if (given.Length > 0)
        {
            string n = ContentFormats.Normalize(given);
            if (n.Length == 0)
            {
                throw new ValueError("mc_version 写法不对(应为 1.20.1 / 1.21.4 / 26.1 这样的正式版号): " + given);
            }
            return (n, "参数 mc_version");
        }
        if (version.Length > 0)
        {
            string? fromId = Mods._mc_version_from_text(version);
            if (fromId == null && inst.VersionJson(version) is PyDict vj)
            {
                fromId = Mods._mc_version_from_text(Py.S(vj.Get("inheritsFrom"))) ?? Mods._mc_version_from_text(Py.S(vj.Get("clientVersion")));
            }
            if (fromId != null)
            {
                return (ContentFormats.Normalize(fromId), "版本 " + version);
            }
        }
        PyDict meta = Py.DictOr(inst.Meta());
        foreach (object? raw in new[] { Py.DictOr(meta.Get("modpack")).Get("mc_version"), meta.Get("mc_version") })
        {
            string? v = Mods._mc_version_from_text(raw);
            if (v != null)
            {
                return (ContentFormats.Normalize(v), "实例信息");
            }
        }
        var found = inst.InstalledIds().Select(id => Mods._mc_version_from_text(id)).Where(v => v != null)
            .Select(v => ContentFormats.Normalize(v!)).Where(v => v.Length > 0).Distinct().ToList();
        if (found.Count == 1)
        {
            return (found[0], "已装版本");
        }
        if (found.Count > 1)
        {
            throw new ValueError("实例 " + inst.name + " 里装了多个 MC 版本(" + string.Join(" / ", found) + "),请传 version 或 mc_version 指明是哪个");
        }
        throw new ValueError("推断不出实例的 MC 版本,请传 mc_version(如 1.20.1)");
    }

    /// <summary>真实路径必须落在 root 的真实路径之内(跟随符号链接 / 目录联接),否则拒绝。返回真实路径。</summary>
    private static string Guard(string path, string root)
    {
        string real = AiFsTools.RealPath(path);
        string realRoot = AiFsTools.RealPath(root);
        if (!AiFsTools.IsWithin(real, realRoot) || AiFsTools.FsDenied(real))
        {
            throw new PermissionError("目标路径越出了允许范围(" + root + "): " + path);
        }
        return real;
    }

    private static string StripExt(string path, string ext)
        => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - ext.Length) : path;

    private static string Dump(object json) => PyJson.Dumps(json, indent: 2, ensureAscii: false) + "\n";

    private static string Norm(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

    private static void AddText(Plan plan, string target, string rel, string text)
    {
        if (Directory.Exists(target))
        {
            throw new ValueError(rel + " 已经是一个文件夹,不能当文件写");
        }
        if (plan.Writes.Any(w => string.Equals(w.Rel, rel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValueError("同一个文件写了两次: " + rel);
        }
        if (Encoding.UTF8.GetByteCount(text) > MAX_FILE_BYTES)
        {
            throw new ValueError(rel + " 太大(单个文件上限 256 KB)");
        }
        var w = new Write { Path = target, Rel = rel, Text = Norm(text), Existed = File.Exists(target) };
        if (w.Existed)
        {
            try
            {
                w.OldText = AiFs.ReadText(target, replace: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new PermissionError("读不了现有文件 " + rel + ": " + Py.ExcStr(ex));
            }
        }
        w.Changed = !w.Existed || w.OldText != w.Text;
        plan.Writes.Add(w);
    }

    private static void AddBytes(Plan plan, string target, string rel, byte[] data, int width, int height)
    {
        if (Directory.Exists(target))
        {
            throw new ValueError(rel + " 已经是一个文件夹,不能当文件写");
        }
        if (plan.Writes.Any(w => string.Equals(w.Rel, rel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValueError("同一个文件写了两次: " + rel);
        }
        var w = new Write { Path = target, Rel = rel, Bytes = data, Existed = File.Exists(target), Width = width, Height = height };
        w.Changed = !w.Existed || !File.ReadAllBytes(target).SequenceEqual(data);
        plan.Writes.Add(w);
    }

    private static void CheckTotal(Plan plan)
    {
        long total = plan.Writes.Sum(w => w.Bytes?.LongLength ?? Encoding.UTF8.GetByteCount(w.Text ?? ""));
        if (total > MAX_TOTAL_BYTES)
        {
            throw new ValueError("这次要写的内容太多(" + (total / 1024) + " KB,上限 8 MB),请拆成几次");
        }
    }

    private static List<object> FilesArg(object? raw)
    {
        if (raw == null)
        {
            return new List<object>();
        }
        if (raw is not List<object> list)
        {
            throw new ValueError("files 应为数组(每项一个文件)");
        }
        if (list.Count > MAX_FILES)
        {
            throw new ValueError("一次最多写 " + MAX_FILES + " 个文件");
        }
        return list;
    }

    /// <summary>现有 pack.mcmeta 里的格式号(读不出来返回 null)。</summary>
    private static long? ExistingFormat(string metaPath)
    {
        try
        {
            PyDict pack = Py.DictOr(Py.DictOr(PyJson.Loads(AiFs.ReadText(metaPath, replace: true))).Get("pack"));
            object? v = pack.Get("pack_format") ?? pack.Get("max_format");
            return v is List<object> l && l.Count > 0 ? Py.Int(l[0]) : v != null ? Py.Int(v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void PackMeta(Plan plan, string root, ContentFormats.FormatRow row, string description, string name)
    {
        string metaPath = Path.Combine(root, "pack.mcmeta");
        bool metaExists = File.Exists(metaPath);
        if (!metaExists || description.Length > 0)
        {
            AddText(plan, metaPath, "pack.mcmeta", Dump(ContentFormats.PackMeta(row, plan.Mc, description.Length > 0 ? description : name)));
            return;
        }
        long? cur = ExistingFormat(metaPath);
        if (cur != null && cur != row.Major)
        {
            plan.Notes.Add("现有 pack.mcmeta 的格式号是 " + cur + ",与 MC " + plan.Mc + " 的 " + ContentFormats.FormatText(row)
                + " 不一致(游戏会提示不兼容);传 description 可按当前版本重写 pack.mcmeta");
        }
    }

    // ───────────────────────────── create_datapack ─────────────────────────────

    /// <summary>算出 create_datapack 要写的文件(全部校验在这里做;不合格抛异常,一个字都不写)。</summary>
    public static Plan PlanDatapack(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string name = ContentValidate.CheckFolderName(Py.S(args.Get("name")), "name");
        string world = ContentValidate.CheckWorldName(Py.S(args.Get("world")));
        string nsDefault = Py.S(args.Get("namespace")).Trim();
        string description = Py.S(args.Get("description")).Trim();
        bool update = Py.Truthy(args.Get("update"));
        List<object> files = FilesArg(args.Get("files"));
        if (nsDefault.Length > 0 && !ContentValidate.IsNamespace(nsDefault))
        {
            throw new ValueError("namespace 只能用小写字母、数字和 _ . -: " + nsDefault);
        }
        Ctx ctx = Context(args, instance);
        (string mc, string source) = ResolveMc(args, ctx.Inst, ctx.Version);
        ContentFormats.FormatRow row = ContentFormats.DataPackFormat(mc) ?? throw new ValueError(
            "MC " + mc + " 不在数据包格式对照表里(收录 " + ContentFormats.KnownRange(ContentFormats.DataPack)
            + " 的正式版;数据包 1.13 起才有)。不猜格式号 —— 请确认版本号是否写对");

        string savesDir = PPath.Abs(Py.S(ctx.Folders.Get("saves")));
        string worldDir = Path.Combine(savesDir, world);
        if (!Directory.Exists(worldDir))
        {
            throw new FileNotFoundError("存档不存在: " + world + "(" + savesDir + " 下没有这个文件夹,可先用 list_saves 查)");
        }
        if (!File.Exists(Path.Combine(worldDir, "level.dat")))
        {
            throw new ValueError(world + " 不像是存档(里面没有 level.dat)");
        }
        string root = Path.Combine(worldDir, "datapacks", name);
        Guard(root, worldDir);
        if (File.Exists(root))
        {
            throw new ValueError("datapacks 下已有同名文件: " + name);
        }
        var plan = new Plan
        {
            Tool = "create_datapack", Root = root, Mc = mc, McSource = source, Format = ContentFormats.FormatText(row),
            Update = update, RootExisted = Directory.Exists(root),
        };
        if (plan.RootExisted && !update)
        {
            throw new ValueError("数据包 " + name + " 已存在;要在它上面修改请传 update=true(会先把改动给用户看)");
        }
        if (files.Count == 0 && !(update && description.Length > 0))
        {
            throw new ValueError("files 不能为空(至少一个配方 / 战利品表 / 进度 / 函数 / 标签)");
        }
        PackMeta(plan, root, row, description, name);
        for (int i = 0; i < files.Count; i++)
        {
            string where = "files[" + i + "]";
            if (files[i] is not PyDict f)
            {
                throw new ValueError(where + " 应为对象 {type, path, content}");
            }
            string type = ContentFormats.Singular(Py.S(f.Get("type")));
            if (!ContentFormats.DataTypes.Contains(type))
            {
                throw new ValueError(where + ".type 只能是 " + string.Join(" / ", ContentFormats.DataTypes) + ",实际是 " + Py.S(f.Get("type")));
            }
            string ns = Py.S(f.Get("namespace")).Trim();
            ns = ns.Length > 0 ? ns : nsDefault;
            if (ns.Length == 0)
            {
                throw new ValueError(where + " 缺少 namespace(包级 namespace 或文件自己的 namespace)");
            }
            if (!ContentValidate.IsNamespace(ns))
            {
                throw new ValueError(where + ".namespace 只能用小写字母、数字和 _ . -: " + ns);
            }
            string ext = type == "function" ? ".mcfunction" : ".json";
            string path = ContentValidate.CheckResourcePath(StripExt(Py.S(f.Get("path")).Trim(), ext), where + ".path");
            string dir;
            string text;
            string label = where + "(" + type + " " + ns + ":" + path + ")";
            if (type == "function")
            {
                dir = ContentFormats.DataDir("function", mc);
                text = ContentValidate.Function(f.Get("content"), mc, label);
            }
            else
            {
                object json = ContentValidate.ParseJson(f.Get("content"), label);
                string note;
                if (type == "tag")
                {
                    string tagType = ContentFormats.Singular(Py.S(f.Get("tag_type")));
                    if (!ContentFormats.TagTypes.Contains(tagType))
                    {
                        throw new ValueError(where + ".tag_type 只能是 " + string.Join(" / ", ContentFormats.TagTypes));
                    }
                    dir = ContentFormats.TagDir(tagType, mc);
                    note = ContentValidate.Tag(json, mc, label);
                }
                else
                {
                    dir = ContentFormats.DataDir(type, mc);
                    note = type switch
                    {
                        "recipe" => ContentValidate.Recipe(json, mc, label),
                        "loot_table" => ContentValidate.LootTable(json, mc, label),
                        _ => ContentValidate.Advancement(json, mc, label),
                    };
                }
                if (note.Length > 0)
                {
                    plan.Notes.Add(label + ":" + note);
                }
                text = Dump(json);
            }
            string rel = "data/" + ns + "/" + dir + "/" + path + ext;
            string target = Guard(Path.Combine(new[] { root }.Concat(rel.Split('/')).ToArray()), root);
            AddText(plan, target, rel, text);
        }
        CheckTotal(plan);
        return plan;
    }

    // ───────────────────────────── create_resourcepack ─────────────────────────────

    /// <summary>资源包支持的文件类型。</summary>
    public static readonly string[] AssetTypes = { "lang", "model", "blockstate", "texture" };

    /// <summary>算出 create_resourcepack 要写的文件。贴图只认用户给的本地 png(source),不生成图片。</summary>
    public static Plan PlanResourcepack(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string name = ContentValidate.CheckFolderName(Py.S(args.Get("name")), "name");
        string nsDefault = Py.S(args.Get("namespace")).Trim();
        string description = Py.S(args.Get("description")).Trim();
        bool update = Py.Truthy(args.Get("update"));
        List<object> files = FilesArg(args.Get("files"));
        if (nsDefault.Length > 0 && !ContentValidate.IsNamespace(nsDefault))
        {
            throw new ValueError("namespace 只能用小写字母、数字和 _ . -: " + nsDefault);
        }
        Ctx ctx = Context(args, instance);
        (string mc, string source) = ResolveMc(args, ctx.Inst, ctx.Version);
        ContentFormats.FormatRow row = ContentFormats.ResourcePackFormat(mc) ?? throw new ValueError(
            "MC " + mc + " 不在资源包格式对照表里(收录 " + ContentFormats.KnownRange(ContentFormats.ResourcePack)
            + " 的正式版;1.13 之前的旧结构本工具不生成)。不猜格式号 —— 请确认版本号是否写对");
        string packsDir = PPath.Abs(Py.S(ctx.Folders.Get("resourcepacks")));
        string root = Path.Combine(packsDir, name);
        Guard(root, ctx.GameDir);
        if (File.Exists(root))
        {
            throw new ValueError("resourcepacks 下已有同名文件: " + name);
        }
        var plan = new Plan
        {
            Tool = "create_resourcepack", Root = root, Mc = mc, McSource = source, Format = ContentFormats.FormatText(row),
            Update = update, RootExisted = Directory.Exists(root),
        };
        if (plan.RootExisted && !update)
        {
            throw new ValueError("资源包 " + name + " 已存在;要在它上面修改请传 update=true(会先把改动给用户看)");
        }
        if (files.Count == 0 && !(update && description.Length > 0))
        {
            throw new ValueError("files 不能为空(至少一个语言文件 / 模型 / 方块状态 / 贴图)");
        }
        PackMeta(plan, root, row, description, name);
        for (int i = 0; i < files.Count; i++)
        {
            string where = "files[" + i + "]";
            if (files[i] is not PyDict f)
            {
                throw new ValueError(where + " 应为对象 {type, path, content / source}");
            }
            string type = Py.Lower(Py.S(f.Get("type")).Trim());
            type = type switch { "models" => "model", "blockstates" => "blockstate", "textures" => "texture", "language" => "lang", _ => type };
            if (!AssetTypes.Contains(type))
            {
                throw new ValueError(where + ".type 只能是 " + string.Join(" / ", AssetTypes) + ",实际是 " + Py.S(f.Get("type")));
            }
            string ns = Py.S(f.Get("namespace")).Trim();
            ns = ns.Length > 0 ? ns : (nsDefault.Length > 0 ? nsDefault : "minecraft");
            if (!ContentValidate.IsNamespace(ns))
            {
                throw new ValueError(where + ".namespace 只能用小写字母、数字和 _ . -: " + ns);
            }
            string ext = type == "texture" ? ".png" : ".json";
            string path = StripExt(Py.S(f.Get("path")).Trim(), ext);
            string label = where + "(" + type + " " + ns + ":" + path + ")";
            string rel;
            if (type == "lang")
            {
                string code = path.ToLowerInvariant() == path ? path : throw new ValueError(where + ".path 语言代码要小写(如 zh_cn、en_us): " + path);
                if (!ContentValidate.IsLangCode(code))
                {
                    throw new ValueError(where + ".path 应为语言代码(如 zh_cn、en_us): " + path);
                }
                rel = "assets/" + ns + "/lang/" + code + ".json";
            }
            else
            {
                ContentValidate.CheckResourcePath(path, where + ".path");
                string sub = type switch { "model" => "models", "blockstate" => "blockstates", _ => "textures" };
                rel = "assets/" + ns + "/" + sub + "/" + path + ext;
            }
            string target = Guard(Path.Combine(new[] { root }.Concat(rel.Split('/')).ToArray()), root);
            if (type == "texture")
            {
                if (f.Get("content") != null)
                {
                    throw new ValueError(label + ":贴图不接受 content —— 只能用用户附带的 png 文件(source 填它的本地路径),本工具不生成图片");
                }
                byte[] data = ReadPng(Py.S(f.Get("source")), label);
                (int w, int h) = ContentValidate.Png(data, label);
                AddBytes(plan, target, rel, data, w, h);
                continue;
            }
            object json = ContentValidate.ParseJson(f.Get("content"), label);
            switch (type)
            {
                case "lang":
                    ContentValidate.Lang(json, label);
                    if (Py.Truthy(f.Get("merge")) && File.Exists(target))
                    {
                        PyDict merged;
                        try
                        {
                            merged = Py.DictOr(PyJson.Loads(AiFs.ReadText(target, replace: true))).Copy();
                        }
                        catch (Exception ex)
                        {
                            throw new ValueError(label + ":现有语言文件不是合法 JSON,不能合并(" + Py.ExcStr(ex) + ");去掉 merge 改为整份覆盖");
                        }
                        foreach (KeyValuePair<string, object?> kv in (PyDict)json)
                        {
                            merged[kv.Key] = kv.Value;
                        }
                        json = merged;
                    }
                    break;
                case "model":
                    ContentValidate.Model(json, label);
                    break;
                default:
                    ContentValidate.Blockstate(json, label);
                    break;
            }
            AddText(plan, target, rel, Dump(json));
        }
        CheckTotal(plan);
        return plan;
    }

    /// <summary>读用户附带的 png:必须是存在的绝对路径、扩展名 .png、不超过 4 MB(内容是不是真 PNG 由 ContentValidate.Png 判)。</summary>
    private static byte[] ReadPng(string source, string label)
    {
        string p = (source ?? "").Trim();
        if (p.Length == 0)
        {
            throw new ValueError(label + ":贴图要传 source(用户附带的 png 的本地路径)");
        }
        if (!Path.IsPathRooted(p) || p.Contains('\0'))
        {
            throw new ValueError(label + ":source 要写完整的本地绝对路径: " + p);
        }
        if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValueError(label + ":只接受 .png 贴图: " + p);
        }
        var fi = new FileInfo(p);
        if (!fi.Exists)
        {
            throw new FileNotFoundError(label + ":找不到贴图文件 " + p);
        }
        if (fi.Length > ContentValidate.MAX_PNG_BYTES)
        {
            throw new ValueError(label + ":贴图太大(上限 4 MB)");
        }
        return File.ReadAllBytes(fi.FullName);
    }

    // ───────────────────────────── write_kubejs_script ─────────────────────────────

    /// <summary>
    /// 实例(版本)的 mods 里是否装了 KubeJS:先找文件名带 kubejs 的 jar(.disabled 不算),再读 jar 里的
    /// fabric.mod.json / quilt.mod.json / META-INF/(neoforge.)mods.toml 认 mod id = kubejs(KubeJS 的附属模组 id 不是这个);
    /// jar 打不开时退回按文件名认 kubejs-&lt;加载器&gt;-…jar。返回命中的 jar 名,没有返回空串。
    /// </summary>
    public static string FindKubeJs(string modsDir)
    {
        if (!Directory.Exists(modsDir))
        {
            return "";
        }
        foreach (string jar in Directory.EnumerateFiles(modsDir, "*.jar").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.GetFileName(jar);
            if (!file.Contains("kubejs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            bool? byMeta = JarDeclaresKubeJs(jar);
            if (byMeta == true
                || (byMeta == null && Regex.IsMatch(file, @"^kubejs-(forge|neoforge|fabric|quilt)-", RegexOptions.IgnoreCase)))
            {
                return file;
            }
        }
        return "";
    }

    private static readonly Regex ModIdRx = new(@"(?:""id""\s*:\s*""kubejs""|modId\s*=\s*""kubejs"")", RegexOptions.CultureInvariant);

    /// <summary>jar 的元数据是否声明 mod id = kubejs;jar 读不了返回 null。</summary>
    private static bool? JarDeclaresKubeJs(string jar)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(jar);
            foreach (string entry in new[] { "fabric.mod.json", "quilt.mod.json", "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
            {
                ZipArchiveEntry? e = zip.GetEntry(entry);
                if (e == null || e.Length > 1024 * 1024)
                {
                    continue;
                }
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                if (ModIdRx.IsMatch(reader.ReadToEnd()))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>算出 write_kubejs_script 要写的文件:装了 KubeJS、目录 / 文件名在白名单内、大小在上限内、静态检查通过。</summary>
    public static Plan PlanKubeJs(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string typeRaw = Py.Lower(Py.S(args.Get("script_type")).Trim());
        string nameRaw = Py.S(args.Get("name")).Trim();
        object? content = args.Get("content");
        bool update = Py.Truthy(args.Get("update"));
        string dir = typeRaw switch
        {
            "server" or "server_scripts" => "server_scripts",
            "client" or "client_scripts" => "client_scripts",
            "startup" or "startup_scripts" => "startup_scripts",
            _ => throw new ValueError("script_type 只能是 server / client / startup(对应 kubejs 下的三个脚本目录)"),
        };
        string name = StripExt(nameRaw, ".js");
        if (!ScriptNameRx.IsMatch(name))
        {
            throw new ValueError("name 只能用字母、数字、_ 和 -(最长 64,不带路径;.js 后缀可省): " + nameRaw);
        }
        if (content is PyDict || content is List<object>)
        {
            throw new ValueError("content 是脚本源码文本");
        }
        string text = Norm(Py.S(content));
        if (text.Trim().Length == 0)
        {
            throw new ValueError("content 不能为空");
        }
        if (text.Contains('\0'))
        {
            throw new ValueError("脚本里有空字节");
        }
        if (Encoding.UTF8.GetByteCount(text) > MAX_SCRIPT_BYTES)
        {
            throw new ValueError("脚本太大(上限 64 KB)");
        }
        Ctx ctx = Context(args, instance);
        string modsDir = PPath.Abs(Py.S(ctx.Folders.Get("mods")));
        string jar = FindKubeJs(modsDir);
        if (jar.Length == 0)
        {
            throw new ValueError("这个实例" + (ctx.Version.Length > 0 ? "(版本 " + ctx.Version + ")" : "")
                + "没装 KubeJS(" + modsDir + " 里没找到 kubejs 的 jar),脚本不会被加载。改配方 / 战利品请用 create_datapack;要用脚本得先装 KubeJS");
        }
        List<string> hits = ContentValidate.KubeJsViolations(text);
        if (hits.Count > 0)
        {
            throw new PermissionError("脚本里有不允许的用法,拒绝写入(脚本在游戏进程里运行,不允许碰文件 / 网络 / 进程 / 反射):\n- "
                + string.Join("\n- ", hits.Take(20)));
        }
        string kubeRoot = Path.Combine(ctx.GameDir, "kubejs");
        string target = Guard(Path.Combine(kubeRoot, dir, name + ".js"), ctx.GameDir);
        var plan = new Plan
        {
            Tool = "write_kubejs_script", Root = Path.Combine(kubeRoot, dir), Mc = "", McSource = "", Format = "",
            Update = update, RootExisted = Directory.Exists(Path.Combine(kubeRoot, dir)),
        };
        if (File.Exists(target) && !update)
        {
            throw new ValueError("脚本 " + dir + "/" + name + ".js 已存在;要覆盖请传 update=true(会先把改动给用户看)");
        }
        AddText(plan, target, "kubejs/" + dir + "/" + name + ".js", text.TrimEnd('\n') + "\n");
        plan.Notes.Add("KubeJS:" + jar);
        return plan;
    }

    // ───────────────────────────── 执行 / 检查点 ─────────────────────────────

    /// <summary>按工具名算计划(不是本包的写工具返回 null)。</summary>
    public static Plan? PlanFor(string name, PyDict? args, string instance) => name switch
    {
        "create_datapack" => PlanDatapack(args, instance),
        "create_resourcepack" => PlanResourcepack(args, instance),
        "write_kubejs_script" => PlanKubeJs(args, instance),
        _ => null,
    };

    /// <summary>检查点要快照的文件:计划里真的会变的那些(新建的回滚时删掉,改过的还原)。算不出计划返回空清单。</summary>
    public static List<string> AffectedPaths(string name, PyDict? args, string instance)
    {
        try
        {
            return PlanFor(name, args, instance)?.Changed.Select(w => w.Path).ToList() ?? new List<string>();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private static string Tip(string tool) => tool switch
    {
        "create_datapack" => "提醒用户:进存档后输入 /reload(需要作弊权限)或重进存档让数据包生效;没生效时用 validate_content 查加载报错。",
        "create_resourcepack" => "提醒用户:在「选项 → 资源包」里启用这个包,已启用的按 F3+T 重新加载;没生效时用 validate_content 查加载报错。",
        _ => "提醒用户:server_scripts 在游戏里 /reload 生效,client_scripts 按 F3+T,startup_scripts 要重启游戏;报错用 validate_content 查。",
    };

    /// <summary>执行写工具:按计划落盘(没变的文件不碰),返回结构化结果。</summary>
    public static PyDict Apply(Plan plan)
    {
        var written = new List<object>();
        var unchanged = new List<object>();
        foreach (Write w in plan.Writes)
        {
            if (!w.Changed)
            {
                unchanged.Add(w.Rel);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(w.Path)!);
            if (w.Bytes != null)
            {
                string tmp = w.Path + ".tipcl-tmp";
                File.WriteAllBytes(tmp, w.Bytes);
                File.Move(tmp, w.Path, overwrite: true);
            }
            else
            {
                AiFs.WriteText(w.Path, w.Text ?? "");
            }
            written.Add(w.Rel + (w.Existed ? "(已修改)" : "(新建)"));
        }
        var result = PyDict.Of("ok", true, "tool", plan.Tool, "path", plan.Root);
        if (plan.Mc.Length > 0)
        {
            result["mc_version"] = plan.Mc;
            result["mc_version_source"] = plan.McSource;
            result["pack_format"] = plan.Format;
        }
        result["written"] = written;
        result["unchanged"] = unchanged;
        if (plan.Notes.Count > 0)
        {
            result["notes"] = plan.Notes.Cast<object>().ToList();
        }
        result["next_step"] = written.Count == 0 ? "内容与现有文件完全相同,没有写入任何东西。" : Tip(plan.Tool);
        return result;
    }

    /// <summary>ToolRunner 的分发入口。</summary>
    public static object Run(string name, PyDict? args, string instance)
    {
        args ??= new PyDict();
        if (name == "validate_content")
        {
            return ValidateContent(args, instance);
        }
        Plan plan = PlanFor(name, args, instance) ?? throw new ValueError("没有这个工具: " + name);
        return Apply(plan);
    }

    // ───────────────────────────── validate_content ─────────────────────────────

    /// <summary>
    /// 日志里的加载错误特征(类别 → 正则)。来源:原版 / KubeJS 加载失败时写进 latest.log 的固定措辞
    /// (配方 Parsing error loading recipe、数据文件 Couldn't parse data file、函数 Failed to load function、
    /// 标签 Couldn't load tag、模型 Unable to load model、贴图 Using missing texture、语言 Failed to load translations …)。
    /// 只是「这一行像加载错误」的判据,不解析堆栈。
    /// </summary>
    public static readonly (string Category, string Pattern)[] LogPatterns =
    {
        ("kubejs", @"(?i)kubejs.*\b(?:error|exception|failed)\b|\b(?:error|exception)\b.*kubejs"),
        ("datapack", @"Parsing error loading (?:recipe|custom advancement)"),
        ("datapack", @"Couldn't parse (?:data file|loot table|element|advancement|recipe)"),
        ("datapack", @"Couldn't load (?:tag|function|advancement|loot table|recipe)"),
        ("datapack", @"Failed to load function|Failed to load datapacks|Unknown function"),
        ("datapack", @"Missing metadata in pack|Couldn't load pack metadata|Failed to read pack"),
        ("datapack", @"Unknown or incomplete command|Incorrect argument for command"),
        ("resourcepack", @"Unable to load model|Missing textures? in model|Unable to resolve texture|Using missing texture"),
        ("resourcepack", @"Exception loading blockstate definition|Failed to load blockstate"),
        ("resourcepack", @"Failed to load translations|Skipped language file|Couldn't read strings from"),
        ("resourcepack", @"Unable to parse metadata from|Invalid path in (?:resource )?pack"),
        ("resourcepack", @"(?:Couldn't|Unable to|Failed to) (?:load|parse)\b.*\bassets/"),
        ("datapack", @"(?:Couldn't|Unable to|Failed to) (?:load|parse)\b.*\bdata/"),
    };

    /// <summary>KubeJS 自己的日志里哪些行算错误。</summary>
    private static readonly Regex KubeLogErrRx = new(@"\[ERR|\bERROR\b|\b\w*(?:Exception|Error)\b:", RegexOptions.CultureInvariant);

    private const int MAX_ISSUES = 60;

    private const long LOG_BYTES = 4L * 1024 * 1024;

    /// <summary>判断一行日志属于哪类加载错误(都不像返回空串)。</summary>
    public static string Classify(string line)
    {
        foreach ((string cat, string pattern) in LogPatterns)
        {
            if (Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant))
            {
                return cat;
            }
        }
        return "";
    }

    private static bool IsDetailLine(string line)
    {
        string t = line.TrimStart();
        if (line.StartsWith("[", StringComparison.Ordinal) || t.Length == 0 || t.StartsWith("at ", StringComparison.Ordinal))
        {
            return false;
        }
        return t.StartsWith("Caused by", StringComparison.Ordinal) || Regex.IsMatch(t, @"^[A-Za-z_$][\w.$]*(?:Exception|Error)\b");
    }

    private static string Cut(string s) => s.Length > 400 ? s.Substring(0, 400) + "…" : s;

    /// <summary>validate_content:从 latest.log 与 KubeJS 日志里找数据包 / 资源包 / 脚本加载错误,结构化返回。</summary>
    public static PyDict ValidateContent(PyDict? args, string instance)
    {
        args ??= new PyDict();
        string kind = Py.Lower(Py.S(args.Get("kind")).Trim());
        string filter = Py.S(args.Get("filter")).Trim();
        kind = kind.Length == 0 ? "all" : kind;
        if (kind is not ("all" or "datapack" or "resourcepack" or "kubejs"))
        {
            throw new ValueError("kind 只能是 all / datapack / resourcepack / kubejs");
        }
        Ctx ctx = Context(args, instance);
        var sources = new List<(string path, bool kubeLog)>();
        string latest = AiFsTools.FindLog(ctx.Inst, "latest", ctx.Version);
        if (latest.Length > 0)
        {
            sources.Add((PPath.Abs(latest), false));
        }
        if (kind is "all" or "kubejs")
        {
            foreach (string f in new[] { "startup.log", "server.log", "client.log" })
            {
                string p = Path.Combine(ctx.GameDir, "logs", "kubejs", f);
                if (File.Exists(p))
                {
                    sources.Add((p, true));
                }
            }
        }
        var issues = new List<object>();
        var counts = PyDict.Of("datapack", 0L, "resourcepack", 0L, "kubejs", 0L);
        var seen = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        bool truncated = false;
        foreach ((string path, bool kubeLog) in sources)
        {
            string[] lines = Norm(Crash.ReadText(path, LOG_BYTES)).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string cat = kubeLog ? (KubeLogErrRx.IsMatch(line) ? "kubejs" : "") : Classify(line);
                if (cat.Length == 0 || (kind != "all" && cat != kind))
                {
                    continue;
                }
                var detail = new List<object>();
                for (int j = i + 1; j < lines.Length && j <= i + 4 && detail.Count < 2; j++)
                {
                    if (IsDetailLine(lines[j]))
                    {
                        detail.Add(Cut(lines[j].Trim()));
                    }
                    else if (lines[j].StartsWith("[", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
                string text = Cut(Regex.Replace(line.Trim(), @"^\[[0-9:.]+\]\s*", ""));
                if (filter.Length > 0 && !(text + "\n" + string.Join("\n", detail)).Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string key = cat + "|" + text;
                if (seen.TryGetValue(key, out PyDict? dup))
                {
                    dup["repeat"] = Py.Int(dup.Get("repeat")) + 1;
                    continue;
                }
                counts[cat] = Py.Int(counts.Get(cat)) + 1;
                if (issues.Count >= MAX_ISSUES)
                {
                    truncated = true;
                    continue;
                }
                var row = PyDict.Of("category", cat, "file", path, "line_no", (long)(i + 1), "text", text, "detail", detail, "repeat", 1L);
                seen[key] = row;
                issues.Add(row);
            }
        }
        var result = PyDict.Of("ok", true, "instance", ctx.Inst.name, "scanned_files", sources.Select(s => (object)s.path).ToList());
        if (latest.Length > 0)
        {
            try
            {
                result["latest_log_mtime"] = File.GetLastWriteTime(PPath.Abs(latest)).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception)
            {
                // 读不到修改时间就不给
            }
        }
        result["issues"] = issues;
        result["counts"] = counts;
        result["truncated"] = truncated;
        result["message"] = sources.Count == 0
            ? "没找到 latest.log(游戏还没在这个实例 / 版本里运行过?)。先让用户进一次游戏再查。"
            : issues.Count == 0
                ? "日志里没发现数据包 / 资源包 / KubeJS 加载错误。注意日志是游戏上次运行时写的:改完内容要先重载(或重进游戏)再查。"
                : "找到 " + issues.Count + " 条加载错误(按类别:" + PyJson.Dumps(counts) + ")。";
        result["tip"] = "改完数据包:游戏里 /reload 或重进存档;改完资源包:按 F3+T 重新加载;KubeJS:server_scripts 用 /reload、client_scripts 按 F3+T、startup_scripts 要重启游戏。";
        return result;
    }

    // ───────────────────────────── schema ─────────────────────────────

    /// <summary>四个工具的 schema(OpenAI function 形状;Windows 版没有对应工具,命名照 Linux 现有 snake_case 风格)。</summary>
    public const string SchemaJson = """
        [
        {"type":"function","function":{"name":"create_datapack","description":"在某个存档的 datapacks/<name>/ 下生成或修改数据包(改配方、战利品表、进度、函数、标签)。pack.mcmeta 的格式号按 MC 版本自动取,1.21 起目录名用单数(recipe、loot_table…)也会自动处理;每个文件写入前做结构校验,不合格整次拒绝。已存在的包要改时传 update=true。写之前用户会看到改前改后。","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本名,一般留空"},"mc_version":{"type":"string","description":"可选,MC 版本如 1.20.1;不填从实例推断"},"world":{"type":"string","description":"存档文件夹名(saves 下),可先用 list_saves 查"},"name":{"type":"string","description":"数据包文件夹名,字母数字和 _ - ."},"namespace":{"type":"string","description":"默认命名空间,小写,如 mypack;覆盖原版内容用 minecraft"},"description":{"type":"string","description":"包说明;update 时传了才重写 pack.mcmeta"},"update":{"type":"boolean","description":"包已存在时传 true 在其上修改"},"files":{"type":"array","description":"要写的文件","items":{"type":"object","properties":{"type":{"type":"string","description":"recipe / loot_table / advancement / function / tag"},"path":{"type":"string","description":"包内路径,不带扩展名,小写,如 diamond_from_dirt 或 blocks/my_ore"},"content":{"type":"string","description":"JSON 文本;function 为命令文本,每行一条,不带开头的 /"},"tag_type":{"type":"string","description":"type=tag 时必填:block / item / entity_type / fluid / function / game_event"},"namespace":{"type":"string","description":"可选,覆盖包级 namespace"}},"required":["type","path","content"]}}},"required":["world","name","files"]}}},
        {"type":"function","function":{"name":"create_resourcepack","description":"在实例 resourcepacks/<name>/ 下生成或修改资源包:语言文件(改物品名 / 翻译)、方块或物品模型、方块状态、贴图。贴图只能用用户附带的 png(source 填本地路径,必须是真 PNG,边长为 2 的幂或 16 的倍数),不会生成图片。pack.mcmeta 按 MC 版本自动取格式号。已存在的包要改时传 update=true。","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本名,一般留空"},"mc_version":{"type":"string","description":"可选,MC 版本;不填从实例推断"},"name":{"type":"string","description":"资源包文件夹名"},"namespace":{"type":"string","description":"默认命名空间,默认 minecraft"},"description":{"type":"string"},"update":{"type":"boolean","description":"包已存在时传 true 在其上修改"},"files":{"type":"array","items":{"type":"object","properties":{"type":{"type":"string","description":"lang / model / blockstate / texture"},"path":{"type":"string","description":"lang 填语言代码如 zh_cn;model / texture 填如 block/my_block;blockstate 填方块名"},"content":{"type":"string","description":"JSON 文本(texture 不用)"},"source":{"type":"string","description":"type=texture 时:用户附带的 png 的本地路径"},"merge":{"type":"boolean","description":"type=lang 时合并进已有语言文件而不是整份覆盖"},"namespace":{"type":"string"}},"required":["type","path"]}}},"required":["name","files"]}}},
        {"type":"function","function":{"name":"write_kubejs_script","description":"写 KubeJS 脚本(高风险,每次都要用户确认)。只在实例已装 KubeJS 时可用,只能写 kubejs 下 server_scripts / client_scripts / startup_scripts 里的 .js;会做静态检查,用到 require、eval、加载文件 / 网络 / 进程类(java.io、java.net、Runtime、ProcessBuilder 等)会被拒。只是改配方、战利品请优先用 create_datapack。","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本名,一般留空"},"script_type":{"type":"string","description":"server / client / startup"},"name":{"type":"string","description":"文件名,字母数字 _ -,.js 可省"},"content":{"type":"string","description":"脚本源码"},"update":{"type":"boolean","description":"文件已存在时传 true 覆盖"}},"required":["script_type","name","content"]}}},
        {"type":"function","function":{"name":"validate_content","description":"从 latest.log(以及 KubeJS 日志)里找数据包 / 资源包 / KubeJS 脚本的加载错误,按类别结构化返回。用户说「写的东西没生效」时用;日志是游戏上次运行写的,改完要先在游戏里重载","parameters":{"type":"object","properties":{"instance":{"type":"string"},"version":{"type":"string","description":"版本隔离时填版本名"},"kind":{"type":"string","description":"all / datapack / resourcepack / kubejs,默认 all"},"filter":{"type":"string","description":"可选,只看包含这个词的错误(如包名或命名空间)"}}}}}
        ]
        """;

    /// <summary>解析好的 schema 列表(每次新列表)。</summary>
    public static List<object> Schemas() => Py.ListOr(PyJson.Loads(SchemaJson));

    // ───────────────────────────── 自检 ─────────────────────────────

    /// <summary>--ai-selftest 用的纯逻辑检查(不碰磁盘、不联网)。</summary>
    public static void SelfCheck(Action<string, bool> check)
    {
        check("内容工具已进表(schema / 元信息)", Names.All(n => Tools.Names().Contains(n) && ToolMeta.Get(n) != null));
        check("pack_format 对照:1.20.1→15、1.21→48,未收录的版本拒绝",
            ContentFormats.DataPackFormat("1.20.1")?.Major == 15 && ContentFormats.DataPackFormat("1.21")?.Major == 48
            && ContentFormats.ResourcePackFormat("1.20.1")?.Major == 15 && ContentFormats.DataPackFormat("1.16.6") == null
            && ContentFormats.DataPackFormat("1.12.2") == null);
        check("1.21 起数据包目录用单数", ContentFormats.DataDir("recipe", "1.20.4") == "recipes" && ContentFormats.DataDir("recipe", "1.21") == "recipe"
            && ContentFormats.TagDir("item", "1.20.1") == "tags/items" && ContentFormats.TagDir("item", "1.21.1") == "tags/item");
        check("KubeJS 危险用法拦截", ContentValidate.KubeJsViolations("Java.loadClass('java.lang.Runtime')").Count > 0
            && ContentValidate.KubeJsViolations("ServerEvents.recipes(event => { event.remove({ output: 'minecraft:stick' }) })").Count == 0);
        check("KubeJS 脚本不给「本对话都允许」", !ToolRunner.AllowsAlways("write_kubejs_script"));
        bool traversal;
        try
        {
            ContentValidate.CheckResourcePath("../escape");
            traversal = false;
        }
        catch (PermissionError)
        {
            traversal = true;
        }
        check("包内路径穿越被拒", traversal);
    }
}

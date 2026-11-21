// AI2-moddev-mcp:本地模组 jar 的元数据解析(install_local_mod 用)。 // i18n:ignore-file(这里的中文全是回给模型 / 外部 agent 的工具结果与错误原因,不上界面)
//
// 读四种元数据文件:fabric.mod.json / quilt.mod.json / META-INF/mods.toml(Forge)/ META-INF/neoforge.mods.toml。
// 一个 jar 可能同时带好几份(多加载器 jar),所以 Loaders 是一个集合,兼容判断「任一份对得上就算」。
// 和 Conflict.InspectJar 的区别:那边只取第一份元数据、不读适用 MC 版本;这里要给「装进测试实例」做准入判断,
// 所以把 MC 版本约束、${file.jarVersion} 占位(回落 MANIFEST.MF 的 Implementation-Version)、
// 「不是合法 zip」与「是 zip 但不是模组」区分开。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>一个本地模组 jar 的元数据。</summary>
public sealed class ModJarInfo
{
    public string Path = "";

    public string FileName = "";

    /// <summary>是不是能打开的 zip。</summary>
    public bool ValidZip;

    /// <summary>出错原因(打不开 / 元数据损坏);正常时为空。</summary>
    public string Error = "";

    /// <summary>jar 声明支持的加载器(fabric / quilt / forge / neoforge),按发现顺序。</summary>
    public List<string> Loaders = new();

    /// <summary>读到了哪几份元数据文件。</summary>
    public List<string> MetadataFiles = new();

    public string Id = "";

    public string Name = "";

    public string Version = "";

    /// <summary>适用 MC 版本约束的原文(fabric 写法如 "~1.20.1",forge 写法如 "[1.20.1,1.21)");没写为空。</summary>
    public string McVersion = "";

    /// <summary>必需的依赖(不含 minecraft / java / 加载器本身):{id, version}。</summary>
    public List<PyDict> Depends = new();

    public bool IsMod => ValidZip && Loaders.Count > 0 && Id.Length > 0;

    public PyDict ToDict() => PyDict.Of(
        "file", FileName,
        "id", Id,
        "name", Name,
        "version", Version,
        "loaders", Loaders.Cast<object>().ToList(),
        "mc_versions", McVersion,
        "depends", Depends.Cast<object>().ToList(),
        "metadata", MetadataFiles.Cast<object>().ToList());

    private static readonly HashSet<string> SkipDeps = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "forge", "neoforge", "fabricloader", "fabric-loader", "quilt_loader", "quilt-loader", "fml", "javafml",
    };

    /// <summary>读一个 jar。永不抛异常:打不开时 ValidZip=false 并写 Error。</summary>
    public static ModJarInfo Read(string path)
    {
        var info = new ModJarInfo { Path = path ?? "", FileName = System.IO.Path.GetFileName(path ?? "") };
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path!);
        }
        catch (Exception ex)
        {
            info.Error = "不是合法的 jar/zip 文件: " + ex.Message;
            return info;
        }
        using (zip)
        {
            try
            {
                // 读一下条目表:截断的 zip 在这里才会抛
                _ = zip.Entries.Count;
                info.ValidZip = true;
            }
            catch (Exception ex)
            {
                info.Error = "不是合法的 jar/zip 文件: " + ex.Message;
                return info;
            }
            var names = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                names[e.FullName.Replace('\\', '/')] = e;
            }
            string manifestVersion = ManifestVersion(names);
            try
            {
                if (names.TryGetValue("fabric.mod.json", out ZipArchiveEntry? fabric))
                {
                    info.MetadataFiles.Add("fabric.mod.json");
                    FromFabric(info, ReadText(fabric));
                }
                if (names.TryGetValue("quilt.mod.json", out ZipArchiveEntry? quilt))
                {
                    info.MetadataFiles.Add("quilt.mod.json");
                    FromQuilt(info, ReadText(quilt));
                }
                if (names.TryGetValue("META-INF/neoforge.mods.toml", out ZipArchiveEntry? neo))
                {
                    info.MetadataFiles.Add("META-INF/neoforge.mods.toml");
                    FromToml(info, ReadText(neo), "neoforge", manifestVersion);
                }
                if (names.TryGetValue("META-INF/mods.toml", out ZipArchiveEntry? forge))
                {
                    info.MetadataFiles.Add("META-INF/mods.toml");
                    FromToml(info, ReadText(forge), "forge", manifestVersion);
                }
            }
            catch (Exception ex)
            {
                info.Error = "模组元数据解析失败: " + ex.Message;
            }
        }
        if (info.ValidZip && info.MetadataFiles.Count == 0)
        {
            info.Error = "jar 里没有 fabric.mod.json / quilt.mod.json / META-INF/mods.toml / META-INF/neoforge.mods.toml,不是可装的模组";
        }
        else if (info.ValidZip && info.Id.Length == 0 && info.Error.Length == 0)
        {
            info.Error = "模组元数据里没有 id";
        }
        return info;
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        string text = Encoding.UTF8.GetString(ms.ToArray());
        return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
    }

    private static string ManifestVersion(Dictionary<string, ZipArchiveEntry> names)
    {
        if (!names.TryGetValue("META-INF/MANIFEST.MF", out ZipArchiveEntry? mf))
        {
            return "";
        }
        try
        {
            foreach (string line in ReadText(mf).Split('\n'))
            {
                string l = line.TrimEnd('\r');
                if (l.StartsWith("Implementation-Version:", StringComparison.OrdinalIgnoreCase))
                {
                    return l.Substring("Implementation-Version:".Length).Trim();
                }
            }
        }
        catch (Exception)
        {
        }
        return "";
    }

    private static void AddLoader(ModJarInfo info, string loader)
    {
        if (!info.Loaders.Contains(loader))
        {
            info.Loaders.Add(loader);
        }
    }

    private static void SetBasics(ModJarInfo info, string id, string name, string version)
    {
        // 多份元数据时以第一份为准(id 一般各份一致)
        if (info.Id.Length == 0)
        {
            info.Id = id;
        }
        if (info.Name.Length == 0)
        {
            info.Name = name.Length > 0 ? name : id;
        }
        if (info.Version.Length == 0)
        {
            info.Version = version;
        }
    }

    private static void AddDep(ModJarInfo info, string id, string version)
    {
        if (id.Length == 0)
        {
            return;
        }
        if (string.Equals(id, "minecraft", StringComparison.OrdinalIgnoreCase))
        {
            if (info.McVersion.Length == 0)
            {
                info.McVersion = version;
            }
            return;
        }
        if (SkipDeps.Contains(id) || info.Depends.Any(d => string.Equals(Py.S(d.Get("id")), id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        info.Depends.Add(PyDict.Of("id", id, "version", version.Length > 0 ? version : "*"));
    }

    /// <summary>fabric 的版本约束可以是字符串,也可以是字符串数组(任一满足)。数组按「 || 」拼回一串。</summary>
    private static string ConstraintText(object? value)
    {
        if (value is List<object> list)
        {
            return string.Join(" || ", list.Select(Py.S).Where(s => s.Length > 0));
        }
        return Py.S(value);
    }

    private static void FromFabric(ModJarInfo info, string json)
    {
        if (PyJson.Loads(json) is not PyDict d)
        {
            throw new ValueError("fabric.mod.json 不是 JSON 对象");
        }
        AddLoader(info, "fabric");
        SetBasics(info, Py.S(d.Get("id")), Py.S(d.Get("name")), Py.S(d.Get("version")));
        if (d.Get("depends") is PyDict deps)
        {
            foreach (KeyValuePair<string, object?> kv in deps)
            {
                AddDep(info, kv.Key, ConstraintText(kv.Value));
            }
        }
    }

    private static void FromQuilt(ModJarInfo info, string json)
    {
        if (PyJson.Loads(json) is not PyDict d)
        {
            throw new ValueError("quilt.mod.json 不是 JSON 对象");
        }
        PyDict ql = d.Get("quilt_loader") as PyDict ?? d;
        AddLoader(info, "quilt");
        string name = ql.Get("metadata") is PyDict meta ? Py.S(meta.Get("name")) : "";
        SetBasics(info, Py.S(ql.Get("id")), name, Py.S(ql.Get("version")));
        foreach (object? raw in Py.ListOr(ql.Get("depends")))
        {
            if (raw is PyDict dep)
            {
                AddDep(info, Py.S(dep.Get("id")), ConstraintText(dep.Get("versions")));
            }
            else if (raw is string s)
            {
                AddDep(info, s, "*");
            }
        }
    }

    private static void FromToml(ModJarInfo info, string text, string flavor, string manifestVersion)
    {
        PyDict toml;
        try
        {
            toml = TomlLite.Parse(text) ?? new PyDict();
        }
        catch (Exception ex)
        {
            throw new ValueError((flavor == "neoforge" ? "neoforge.mods.toml" : "mods.toml") + " 解析失败: " + ex.Message);
        }
        PyDict first = Py.ListOr(toml.Get("mods")).OfType<PyDict>().FirstOrDefault() ?? new PyDict();
        string id = Py.S(first.Get("modId"));
        string version = Py.S(first.Get("version"));
        if (version.Contains("${", StringComparison.Ordinal))
        {
            version = manifestVersion.Length > 0 ? manifestVersion : version;
        }
        var depIds = new List<(string id, string range, bool required)>();
        if (toml.Get("dependencies") is PyDict depTable)
        {
            foreach (KeyValuePair<string, object?> kv in depTable)
            {
                // 只要本模组自己的依赖(多模组 jar 里别的 [[mods]] 的依赖不算)
                if (id.Length > 0 && !string.Equals(kv.Key, id, StringComparison.Ordinal))
                {
                    continue;
                }
                IEnumerable<object?> items = kv.Value is List<object> l ? l : new List<object?> { kv.Value };
                foreach (object? item in items)
                {
                    if (item is not PyDict dep)
                    {
                        continue;
                    }
                    string type = Py.Lower(Py.S(dep.Get("type")));
                    object? mandatory = dep.Get("mandatory");
                    bool required = type.Length > 0
                        ? type == "required"
                        : !(mandatory is false || Py.Lower(Py.S(mandatory)) == "false");
                    depIds.Add((Py.S(dep.Get("modId")), Py.S(Py.Or(dep.Get("versionRange"), "*")), required));
                }
            }
        }
        // NeoForge 1.20.2–1.20.4 的模组还用 META-INF/mods.toml,但会声明依赖 neoforge:按 neoforge 认
        string loader = flavor;
        if (flavor == "forge" && (depIds.Any(d => string.Equals(d.id, "neoforge", StringComparison.OrdinalIgnoreCase))
            || Py.Lower(Py.S(toml.Get("modLoader"))).Contains("neo")))
        {
            loader = "neoforge";
        }
        AddLoader(info, loader);
        SetBasics(info, id, Py.S(first.Get("displayName")), version);
        foreach ((string depId, string range, bool required) in depIds)
        {
            if (string.Equals(depId, "minecraft", StringComparison.OrdinalIgnoreCase))
            {
                AddDep(info, depId, range);
            }
            else if (required)
            {
                AddDep(info, depId, range);
            }
        }
    }

    // ───────────────────────────── 加载器 / MC 版本判断 ─────────────────────────────

    /// <summary>把各种写法的加载器名归一成 fabric / quilt / forge / neoforge / vanilla(空 = 认不出)。</summary>
    public static string NormLoader(string? raw)
    {
        string s = Py.Lower((raw ?? "").Trim());
        if (s.Length == 0 || s is "无" or "none" or "vanilla" or "原版")
        {
            return s.Length == 0 ? "" : "vanilla";
        }
        if (s.Contains("neoforge") || s.Contains("neo"))
        {
            return "neoforge";
        }
        if (s.Contains("quilt"))
        {
            return "quilt";
        }
        if (s.Contains("fabric"))
        {
            return "fabric";
        }
        if (s.Contains("forge"))
        {
            return "forge";
        }
        return "";
    }

    /// <summary>
    /// 这个 jar 能不能装进加载器为 <paramref name="instanceLoader"/> 的实例。规则:
    /// 同名加载器;Quilt 实例也能加载 Fabric 模组;NeoForge 1.20.1 是 Forge 的分支,能加载 Forge 模组。
    /// </summary>
    public bool CompatibleWith(string instanceLoader, string mcVersion, out string reason)
    {
        string inst = NormLoader(instanceLoader);
        reason = "";
        if (inst.Length == 0 || inst == "vanilla")
        {
            reason = "目标实例没有装模组加载器(原版),装不了模组";
            return false;
        }
        if (Loaders.Contains(inst))
        {
            return true;
        }
        if (inst == "quilt" && Loaders.Contains("fabric"))
        {
            return true;
        }
        if (inst == "neoforge" && Loaders.Contains("forge") && (mcVersion ?? "").Trim() == "1.20.1")
        {
            return true;
        }
        reason = "加载器不匹配:jar 是 " + string.Join(" / ", Loaders) + " 模组,实例用的是 " + inst;
        return false;
    }

    /// <summary>
    /// <paramref name="mc"/> 满不满足约束 <paramref name="constraint"/>。true / false;认不出写法或版本号
    /// (快照、预发布等)时返回 null —— 只作提示,不据此拒绝。
    /// 支持 fabric 写法(* / 1.20.1 / 1.20.x / &gt;=1.20 &lt;1.21 / ~1.20.1 / ^1.20 / a || b)与
    /// forge 区间写法([1.20.1,1.21) / [1.20.1] / [1.20,) / 多段用逗号相连)。
    /// </summary>
    public static bool? McMatches(string? constraint, string? mc)
    {
        string c = (constraint ?? "").Trim();
        string v = (mc ?? "").Trim();
        if (c.Length == 0 || c == "*")
        {
            return true;
        }
        int[]? ver = ParseVer(v);
        if (ver == null)
        {
            return null;
        }
        if (c[0] == '[' || c[0] == '(')
        {
            return ForgeRanges(c, ver);
        }
        bool? any = false;
        foreach (string alt in c.Split(new[] { "||" }, StringSplitOptions.None))
        {
            bool? one = FabricAll(alt.Trim(), ver);
            if (one == true)
            {
                return true;
            }
            if (one == null)
            {
                any = null;
            }
        }
        return any;
    }

    private static int[]? ParseVer(string s)
    {
        string t = s.Trim();
        if (t.Length == 0)
        {
            return null;
        }
        // 去掉构建后缀 +xxx;-pre/-rc 之类认不出就返回 null(不乱判)
        int plus = t.IndexOf('+');
        if (plus >= 0)
        {
            t = t.Substring(0, plus);
        }
        string[] parts = t.Split('.');
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0)
            {
                return null;
            }
        }
        return nums;
    }

    private static int Cmp(int[] a, int[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < a.Length ? a[i] : 0;
            int y = i < b.Length ? b[i] : 0;
            if (x != y)
            {
                return x < y ? -1 : 1;
            }
        }
        return 0;
    }

    private static bool? FabricAll(string expr, int[] ver)
    {
        if (expr.Length == 0 || expr == "*")
        {
            return true;
        }
        foreach (string token in Regex.Split(expr, @"\s+").Where(t => t.Length > 0))
        {
            bool? one = FabricOne(token, ver);
            if (one != true)
            {
                return one;
            }
        }
        return true;
    }

    private static bool? FabricOne(string token, int[] ver)
    {
        string op = "";
        foreach (string candidate in new[] { ">=", "<=", ">", "<", "=", "~", "^" })
        {
            if (token.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                break;
            }
        }
        string body = token.Substring(op.Length).Trim();
        if (body == "*" || body.Length == 0)
        {
            return true;
        }
        // 1.20.x / 1.20.* 通配
        if (body.EndsWith(".x", StringComparison.OrdinalIgnoreCase) || body.EndsWith(".*", StringComparison.Ordinal))
        {
            int[]? prefix = ParseVer(body.Substring(0, body.Length - 2));
            if (prefix == null)
            {
                return null;
            }
            for (int i = 0; i < prefix.Length; i++)
            {
                if ((i < ver.Length ? ver[i] : 0) != prefix[i])
                {
                    return false;
                }
            }
            return true;
        }
        int[]? b = ParseVer(body);
        if (b == null)
        {
            return null;
        }
        int c = Cmp(ver, b);
        switch (op)
        {
            case ">=": return c >= 0;
            case "<=": return c <= 0;
            case ">": return c > 0;
            case "<": return c < 0;
            case "~":
            {
                // ~1.20.1 = >=1.20.1 <1.21;~1.20 = >=1.20 <1.21
                int[] upper = b.Length >= 2 ? new[] { b[0], b[1] + 1 } : new[] { b[0] + 1 };
                return c >= 0 && Cmp(ver, upper) < 0;
            }
            case "^":
                return c >= 0 && Cmp(ver, new[] { b[0] + 1 }) < 0;
            default:
                return c == 0;
        }
    }

    private static bool? ForgeRanges(string text, int[] ver)
    {
        // 多段区间以逗号相连:[1.19,1.20),[1.20.1,) —— 按右括号切段
        var ranges = new List<string>();
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (sb.Length == 0 && (ch == ',' || char.IsWhiteSpace(ch)))
            {
                continue;
            }
            sb.Append(ch);
            if (ch == ']' || ch == ')')
            {
                ranges.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0)
        {
            return null;
        }
        bool unknown = false;
        foreach (string r in ranges)
        {
            bool? one = ForgeRange(r, ver);
            if (one == true)
            {
                return true;
            }
            if (one == null)
            {
                unknown = true;
            }
        }
        return unknown ? null : false;
    }

    private static bool? ForgeRange(string r, int[] ver)
    {
        if (r.Length < 2)
        {
            return null;
        }
        bool lowIncl = r[0] == '[';
        bool highIncl = r[r.Length - 1] == ']';
        string inner = r.Substring(1, r.Length - 2);
        string[] parts = inner.Split(',');
        if (parts.Length == 1)
        {
            int[]? exact = ParseVer(parts[0]);
            return exact == null ? null : Cmp(ver, exact) == 0;
        }
        if (parts.Length != 2)
        {
            return null;
        }
        string lo = parts[0].Trim();
        string hi = parts[1].Trim();
        if (lo.Length > 0)
        {
            int[]? l = ParseVer(lo);
            if (l == null)
            {
                return null;
            }
            int c = Cmp(ver, l);
            if (c < 0 || (c == 0 && !lowIncl))
            {
                return false;
            }
        }
        if (hi.Length > 0)
        {
            int[]? h = ParseVer(hi);
            if (h == null)
            {
                return null;
            }
            int c = Cmp(ver, h);
            if (c > 0 || (c == 0 && !highIncl))
            {
                return false;
            }
        }
        return true;
    }
}

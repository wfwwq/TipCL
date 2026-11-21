// AI-perf-saves:可复用的硬件探测(CPU / 核数 / 内存 / 显卡 / Java 摘要)。
// i18n:ignore-file —— 本文件的中文只出现在 FormatText 拼给模型看的那段文字里(与 Windows 版
// Sysinfo.FormatText 同口径,工具结果是数据而不是界面文案),界面不直接显示它。
//
// 为什么不是直接用 Sysinfo:Sysinfo.Collect 是 Windows 版整份照搬(还顺带扫实例、磁盘、显示器),
// CPU / 显卡探测是私有方法,Linux 上显卡只认 lspci(很多发行版默认没装 pciutils),物理核数按
// 「physical id」去重 = 数出来的是 CPU 插槽数而不是核数。这里给一套**字段与 Windows 版一致**
// (total_mb / avail_mb / cores_logical / cores_physical / gpus[name,driver,vram_mb])的小 API,
// 解析函数全是纯函数(ParseMeminfo / ParseCpuinfo / ParseLspci / GpusFromDrm / GpusFromNvidiaProc),
// 测试与别的模块(诊断、AI 工具、设置页)都能直接复用。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TipCL.Core.Launcher;

/// <summary>
/// 硬件探测(只读)。Linux 读 /proc、/sys 与 lspci;Windows(在 Windows 上跑 Avalonia 版时)
/// 走注册表 + CIM;macOS 与探测失败时回退到 .NET 运行时能给出的值(逻辑核数、GC 可见内存)。
/// 结果缓存 <see cref="CacheSeconds"/> 秒,<c>force</c> 绕过缓存。
/// </summary>
public static class SystemInfo
{
    /// <summary>快照缓存时长(秒):显卡 / CPU 名称不会变,内存可用量变得快但诊断用不着秒级精度。</summary>
    public const double CacheSeconds = 120.0;

    private static readonly object Gate = new();
    private static PyDict? _cache;
    private static DateTime _cacheAt = DateTime.MinValue;

    // ───────────────────────────── 对外 API ─────────────────────────────

    /// <summary>
    /// 完整快照:<c>{collected_at, os, cpu, memory, total_mb, avail_mb, gpus, java, summary}</c>。
    /// <c>memory</c> 与 Sysinfo.MemoryInfo 同形(total_mb / avail_mb / load_percent / total_bytes / avail_bytes,
    /// 另带 source 说明数据来源);顶层的 total_mb / avail_mb 是同值的便捷字段。
    /// </summary>
    public static PyDict Snapshot(bool force = false)
    {
        lock (Gate)
        {
            if (!force && _cache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < CacheSeconds)
            {
                return new PyDict(_cache);
            }
        }
        PyDict mem = Memory();
        PyDict cpu = Cpu();
        List<object> gpus = Gpus();
        List<object> java = JavaSummary();
        PyDict snap = PyDict.Of(
            "collected_at", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            "os", OsInfo(),
            "cpu", cpu,
            "memory", mem,
            "total_mb", mem.Get("total_mb"),
            "avail_mb", mem.Get("avail_mb"),
            "gpus", gpus,
            "java", java);
        snap["summary"] = Summary(snap);
        lock (Gate)
        {
            _cache = snap;
            _cacheAt = DateTime.UtcNow;
        }
        return new PyDict(snap);
    }

    /// <summary>电脑内存总量(MB);探测不到时为 0。</summary>
    public static long TotalMemoryMb() => Py.Int(Py.Or(Memory().Get("total_mb"), 0L));

    /// <summary>当前可用内存(MB);探测不到时为 0。</summary>
    public static long AvailMemoryMb() => Py.Int(Py.Or(Memory().Get("avail_mb"), 0L));

    /// <summary>内存:<c>{total_mb, avail_mb, load_percent, total_bytes, avail_bytes, source}</c>。</summary>
    public static PyDict Memory()
    {
        PyDict mem = new PyDict();
        string source = "";
        try
        {
            if (!Utils.IS_WINDOWS && !Utils.IS_MAC && File.Exists("/proc/meminfo"))
            {
                mem = ParseMeminfo(File.ReadAllText("/proc/meminfo"));
                source = "/proc/meminfo";
            }
            else
            {
                mem = Sysinfo.MemoryInfo();
                source = Utils.IS_WINDOWS ? "GlobalMemoryStatusEx" : "";
            }
        }
        catch (Exception)
        {
            mem = new PyDict();
        }
        if (Py.Int(Py.Or(mem.Get("total_mb"), 0L)) <= 0)
        {
            // 回退:.NET 运行时看得到的物理内存上限(容器里是 cgroup 限额,也正是游戏能用的上限)
            long total = 0;
            try
            {
                total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            }
            catch (Exception)
            {
                total = 0;
            }
            mem = PyDict.Of("total_mb", total > 0 ? total / 1048576 : 0L, "avail_mb", 0L, "load_percent", 0L,
                "total_bytes", total, "avail_bytes", 0L);
            source = total > 0 ? "dotnet_gc" : "unknown";
        }
        mem["source"] = source;
        return mem;
    }

    /// <summary>CPU:<c>{name, cores_logical, cores_physical}</c>(cores_physical 为 0 = 探测不到)。</summary>
    public static PyDict Cpu()
    {
        try
        {
            if (Utils.IS_WINDOWS)
            {
                return WindowsCpu();
            }
            if (!Utils.IS_MAC && File.Exists("/proc/cpuinfo"))
            {
                PyDict parsed = ParseCpuinfo(File.ReadAllText("/proc/cpuinfo"));
                if (Py.Int(Py.Or(parsed.Get("cores_logical"), 0L)) <= 0)
                {
                    parsed["cores_logical"] = (long)Environment.ProcessorCount;
                }
                if (Py.S(parsed.Get("name")).Length == 0)
                {
                    parsed["name"] = RuntimeInformation.ProcessArchitecture.ToString();
                }
                return parsed;
            }
        }
        catch (Exception)
        {
        }
        return PyDict.Of("name", RuntimeInformation.ProcessArchitecture.ToString(),
            "cores_logical", (long)Environment.ProcessorCount, "cores_physical", 0L);
    }

    /// <summary>显卡列表:每项 <c>{name, vendor, driver, vram_mb}</c>;探测不到时为空表。</summary>
    public static List<object> Gpus()
    {
        try
        {
            if (Utils.IS_WINDOWS)
            {
                return WindowsGpus();
            }
            if (Utils.IS_MAC)
            {
                return new List<object>();
            }
            List<object> drm = GpusFromDrm("/sys/class/drm");
            List<object> nvidia = GpusFromNvidiaProc("/proc/driver/nvidia/gpus");
            string lspci = Sysinfo._run(new List<string> { "lspci", "-mm" }, 5.0);
            if (lspci.Length == 0)
            {
                lspci = Sysinfo._run(new List<string> { "lspci" }, 5.0);
            }
            return MergeLinuxGpus(ParseLspci(lspci), drm, nvidia, ParseNvidiaSmi(NvidiaSmiText()));
        }
        catch (Exception)
        {
            return new List<object>();
        }
    }

    /// <summary>已装 Java 摘要(启动器管理的那批,与 Windows 版 get_system_info 同口径):<c>[{name, major, path}]</c>。</summary>
    public static List<object> JavaSummary(int max = 8)
    {
        var list = new List<object>();
        try
        {
            foreach (object raw in Java.ListInstalledJavas().Take(max))
            {
                if (raw is PyDict item)
                {
                    list.Add(PyDict.Of("name", Py.Or(item.Get("name"), ""), "major", item.Get("major"),
                        "path", Py.Or(item.Get("exe"), item.Get("path"), "")));
                }
            }
        }
        catch (Exception)
        {
        }
        return list;
    }

    /// <summary>给模型看的多行文字(格式照 Windows 版 Sysinfo.FormatText 的 CPU / 内存 / 显卡 / Java 几行)。</summary>
    public static string FormatText(PyDict snap)
    {
        var lines = new List<string>();
        string summary = Py.S(snap.Get("summary"));
        if (summary.Length > 0)
        {
            lines.Add(summary);
        }
        PyDict cpu = Py.D(snap.Get("cpu"));
        long phys = Py.Int(Py.Or(cpu.Get("cores_physical"), 0L));
        lines.Add("CPU " + Py.S(Py.Or(cpu.Get("name"), "?")) + "  " + (phys > 0 ? phys.ToString(CultureInfo.InvariantCulture) : "?")
                  + "C/" + Py.Str(Py.Or(cpu.Get("cores_logical"), "?")) + "T");
        PyDict mem = Py.D(snap.Get("memory"));
        lines.Add("内存 " + Py.Str(Py.Or(mem.Get("total_mb"), 0L)) + " MB  可用 " + Py.Str(Py.Or(mem.Get("avail_mb"), 0L))
                  + " MB  占用 " + Py.Str(Py.Or(mem.Get("load_percent"), 0L)) + "%");
        List<object> gpus = Py.ListOr(snap.Get("gpus"));
        foreach (object raw in gpus)
        {
            PyDict g = Py.D(raw);
            long vram = Py.Int(Py.Or(g.Get("vram_mb"), 0L));
            string driver = Py.S(g.Get("driver"));
            lines.Add("显卡 " + Py.S(g.Get("name")) + (vram > 0 ? "  " + vram + " MB" : "") + (driver.Length > 0 ? "  驱动 " + driver : ""));
        }
        if (gpus.Count == 0)
        {
            lines.Add("显卡 未检测到");
        }
        List<object> java = Py.ListOr(snap.Get("java"));
        if (java.Count > 0)
        {
            lines.Add("Java " + string.Join(", ", java.Take(6).Select(j =>
            {
                PyDict d = Py.D(j);
                string path = Py.S(d.Get("path"));
                return Py.Str(Py.Or(d.Get("major"), "?")) + "(" + (path.Length > 0 ? new PPath(path).Name : "?") + ")";
            })));
        }
        else
        {
            lines.Add("Java 启动器里还没有装 Java(启动时会按版本自动下载)");
        }
        return string.Join("\n", lines);
    }

    // ───────────────────────────── 纯函数(可测)─────────────────────────────

    /// <summary>解析 /proc/meminfo(单位 kB)。可用量优先 MemAvailable,老内核回退 MemFree。</summary>
    public static PyDict ParseMeminfo(string text)
    {
        var kb = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string line in Py.SplitLines(text ?? ""))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            string digits = new string(line.Substring(colon + 1).Where(char.IsDigit).ToArray());
            if (digits.Length > 0 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            {
                kb[line.Substring(0, colon).Trim()] = v;
            }
        }
        long total = kb.TryGetValue("MemTotal", out long t) ? t * 1024 : 0;
        long avail = kb.TryGetValue("MemAvailable", out long a) ? a * 1024 : (kb.TryGetValue("MemFree", out long f) ? f * 1024 : 0);
        long load = total > 0 ? (long)Math.Round((total - avail) * 100.0 / total) : 0;
        return PyDict.Of("total_mb", total / 1048576, "avail_mb", avail / 1048576, "load_percent", load,
            "total_bytes", total, "avail_bytes", avail);
    }

    /// <summary>
    /// 解析 /proc/cpuinfo。物理核数:有 core id 就按 (physical id, core id) 去重;只有 cpu cores 就乘插槽数;
    /// 都没有(多数 ARM)记 0 = 不知道。名称依次认 model name / Model / Hardware / cpu model。
    /// </summary>
    public static PyDict ParseCpuinfo(string text)
    {
        string name = "";
        long logical = 0;
        var cores = new HashSet<string>(StringComparer.Ordinal);
        var sockets = new HashSet<string>(StringComparer.Ordinal);
        long coresPerSocket = 0;
        string physId = "0";
        string altName = "";
        foreach (string raw in Py.SplitLines(text ?? ""))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            string key = raw.Substring(0, colon).Trim().ToLowerInvariant();
            string value = raw.Substring(colon + 1).Trim();
            switch (key)
            {
                case "processor":
                    logical++;
                    physId = "0";
                    break;
                case "model name":
                    if (name.Length == 0)
                    {
                        name = value;
                    }
                    break;
                case "model":
                case "hardware":
                case "cpu model":
                    // x86 的 model 是个数字,只有不是纯数字时才当名称用
                    if (altName.Length == 0 && value.Length > 0 && !value.All(char.IsDigit))
                    {
                        altName = value;
                    }
                    break;
                case "physical id":
                    physId = value;
                    sockets.Add(value);
                    break;
                case "core id":
                    cores.Add(physId + ":" + value);
                    break;
                case "cpu cores":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n > coresPerSocket)
                    {
                        coresPerSocket = n;
                    }
                    break;
            }
        }
        long physical = cores.Count > 0 ? cores.Count : (coresPerSocket > 0 ? coresPerSocket * Math.Max(1, sockets.Count) : 0);
        if (name.Length == 0)
        {
            name = altName;
        }
        return PyDict.Of("name", string.Join(" ", Py.SplitWs(name)), "cores_logical", logical, "cores_physical", physical);
    }

    private static readonly Regex QuotedField = new("\"([^\"]*)\"", RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析 lspci 输出(兼容 <c>lspci -mm</c> 的引号字段与普通 <c>lspci</c> 的「类别: 厂商 型号」两种形状),
    /// 只留显示类设备(VGA / 3D / Display)。
    /// </summary>
    public static List<object> ParseLspci(string text)
    {
        var list = new List<object>();
        foreach (string line in Py.SplitLines(text ?? ""))
        {
            string lower = line.ToLowerInvariant();
            if (!(lower.Contains("vga") || lower.Contains("3d controller") || lower.Contains("display")))
            {
                continue;
            }
            string name;
            MatchCollection fields = QuotedField.Matches(line);
            if (fields.Count >= 3)
            {
                // -mm:槽位 "类别" "厂商" "设备" ...
                name = (fields[1].Groups[1].Value + " " + fields[2].Groups[1].Value).Trim();
            }
            else
            {
                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                name = colon >= 0 ? line.Substring(colon + 2).Trim() : line.Trim();
                name = Regex.Replace(name, "\\s*\\(rev [0-9a-fA-F]+\\)\\s*$", "");
            }
            if (name.Length > 0)
            {
                list.Add(PyDict.Of("name", name, "vendor", VendorFromName(name), "driver", "", "vram_mb", 0L));
            }
        }
        return list;
    }

    /// <summary>
    /// 从 /sys/class/drm 读显卡(card0、card1…;跳过 card0-HDMI-A-1 这类输出口):
    /// device/vendor(PCI 厂商号)、device/device、device/uevent 的 DRIVER=,
    /// AMD 的 device/mem_info_vram_total(字节)。
    /// </summary>
    public static List<object> GpusFromDrm(string drmRoot)
    {
        var list = new List<object>();
        if (!Directory.Exists(drmRoot))
        {
            return list;
        }
        var cards = Directory.GetDirectories(drmRoot)
            .Select(d => Path.GetFileName(d))
            .Where(n => Regex.IsMatch(n, "^card\\d+$"))
            .OrderBy(n => int.Parse(n.Substring(4), CultureInfo.InvariantCulture))
            .ToList();
        foreach (string card in cards)
        {
            string dev = Path.Combine(drmRoot, card, "device");
            string vendorId = ReadSmall(Path.Combine(dev, "vendor")).ToLowerInvariant();
            if (vendorId.Length == 0)
            {
                continue;
            }
            string deviceId = ReadSmall(Path.Combine(dev, "device")).ToLowerInvariant();
            string driver = "";
            foreach (string line in Py.SplitLines(ReadSmall(Path.Combine(dev, "uevent"))))
            {
                if (line.StartsWith("DRIVER=", StringComparison.Ordinal))
                {
                    driver = line.Substring(7).Trim();
                }
            }
            long vram = 0;
            if (long.TryParse(ReadSmall(Path.Combine(dev, "mem_info_vram_total")), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) && bytes > 0)
            {
                vram = bytes / 1048576;
            }
            string vendor = VendorName(vendorId);
            list.Add(PyDict.Of("name", vendor + " GPU" + (deviceId.Length > 0 ? " (" + deviceId + ")" : ""),
                "vendor", vendor, "driver", driver, "vram_mb", vram, "pci_vendor", vendorId, "pci_device", deviceId));
        }
        return list;
    }

    /// <summary>读 NVIDIA 专有驱动的 /proc/driver/nvidia/gpus/*/information 里的 Model 行。</summary>
    public static List<object> GpusFromNvidiaProc(string gpusRoot)
    {
        var list = new List<object>();
        if (!Directory.Exists(gpusRoot))
        {
            return list;
        }
        foreach (string dir in Directory.GetDirectories(gpusRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            foreach (string line in Py.SplitLines(ReadSmall(Path.Combine(dir, "information"))))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    string model = line.Substring(colon + 1).Trim();
                    if (model.Length > 0)
                    {
                        list.Add(PyDict.Of("name", model, "vendor", "NVIDIA", "driver", "nvidia", "vram_mb", 0L));
                    }
                }
            }
        }
        return list;
    }

    /// <summary>PCI 厂商号 → 厂商名(只认游戏相关的几家;认不出原样返回)。</summary>
    public static string VendorName(string pciVendorId)
    {
        string id = (pciVendorId ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            "0x10de" => "NVIDIA",
            "0x1002" or "0x1022" => "AMD",
            "0x8086" => "Intel",
            "0x1af4" => "VirtIO",
            "0x15ad" => "VMware",
            "0x80ee" => "VirtualBox",
            "0x1414" => "Microsoft",
            _ => id.Length > 0 ? id : "?",
        };
    }

    /// <summary>
    /// 三路来源合并:lspci 名称最准(带具体型号);/sys 有驱动名与 AMD 显存;NVIDIA 专有驱动的 /proc 有型号。
    /// lspci 没装时用 /sys 的厂商 + 设备号,NVIDIA 的再用 /proc 的型号替换。
    /// </summary>
    public static List<object> MergeLinuxGpus(List<object> lspci, List<object> drm, List<object> nvidia)
        => MergeLinuxGpus(lspci, drm, nvidia, new List<object>());

    /// <summary>
    /// 同上,再叠一路 nvidia-smi(<see cref="ParseNvidiaSmi"/>)。AI3-linux-real 真机(WSL2)实测补的:
    /// <list type="bullet">
    /// <item>NVIDIA 专有驱动下 lspci / /sys / /proc 都给不出显存(vram_mb 恒为 0),nvidia-smi 给得出;
    /// 按顺序把它的型号 / 显存 / 驱动版本合进 NVIDIA 条目。</item>
    /// <item>WSL2 里 lspci 只有 GPU-PV 占位的「Microsoft Corporation Basic Render Driver」,真实独显只有
    /// /usr/lib/wsl/lib/nvidia-smi 看得到;多出来的 nvidia-smi 条目先顶替这些占位,再不够就追加。</item>
    /// </list>
    /// </summary>
    public static List<object> MergeLinuxGpus(List<object> lspci, List<object> drm, List<object> nvidia, List<object> smi)
    {
        List<object> merged = MergeLinuxGpusRaw(lspci, drm, nvidia);
        smi ??= new List<object>();
        int next = 0;
        for (int i = 0; i < merged.Count && next < smi.Count; i++)
        {
            var g = new PyDict(Py.D(merged[i]));
            if (!string.Equals(Py.S(g.Get("vendor")), "NVIDIA", StringComparison.OrdinalIgnoreCase)
                && !Py.S(g.Get("name")).ToLowerInvariant().Contains("nvidia"))
            {
                continue;
            }
            merged[i] = g;
            PyDict s = Py.D(smi[next++]);
            g["name"] = s.Get("name");
            g["vendor"] = "NVIDIA";
            if (Py.Int(Py.Or(s.Get("vram_mb"), 0L)) > 0)
            {
                g["vram_mb"] = s.Get("vram_mb");
            }
            if (Py.S(g.Get("driver")).Length == 0)
            {
                g["driver"] = "nvidia";
            }
            g["driver_version"] = Py.Or(s.Get("driver_version"), "");
        }
        for (; next < smi.Count; next++)
        {
            var s = new PyDict(Py.D(smi[next]));
            int placeholder = merged.FindIndex(o => IsWslPlaceholderGpu(Py.S(Py.D(o).Get("name"))));
            if (placeholder >= 0)
            {
                merged[placeholder] = s;
            }
            else
            {
                merged.Add(s);
            }
        }
        return Sysinfo._prefer_physical_gpus(merged);
    }

    /// <summary>WSL2 GPU-PV 在 lspci 里的占位设备(不是真实型号)。</summary>
    public static bool IsWslPlaceholderGpu(string name)
        => (name ?? "").IndexOf("basic render driver", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// 解析 <c>nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits</c>
    /// (也容忍带 「MiB」 单位的写法)。报错文字(驱动没加载、没有设备等)一律不当显卡。
    /// </summary>
    public static List<object> ParseNvidiaSmi(string text)
    {
        var list = new List<object>();
        foreach (string line in Py.SplitLines(text ?? ""))
        {
            string[] parts = line.Split(',');
            if (parts.Length < 3)
            {
                continue;
            }
            string name = parts[0].Trim();
            string memText = parts[1].Trim();
            string digits = new string(memText.TakeWhile(char.IsDigit).ToArray());
            if (name.Length == 0 || digits.Length == 0 || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long mib))
            {
                continue;
            }
            string rest = memText.Substring(digits.Length).Trim();
            if (rest.Length > 0 && !rest.Equals("MiB", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            list.Add(PyDict.Of("name", name, "vendor", "NVIDIA", "driver", "nvidia", "vram_mb", mib,
                "driver_version", parts[2].Trim()));
        }
        return list;
    }

    private static List<object> MergeLinuxGpusRaw(List<object> lspci, List<object> drm, List<object> nvidia)
    {
        var used = new HashSet<int>();
        PyDict? TakeDrm(string vendor)
        {
            for (int i = 0; i < drm.Count; i++)
            {
                PyDict d = Py.D(drm[i]);
                if (!used.Contains(i) && string.Equals(Py.S(d.Get("vendor")), vendor, StringComparison.OrdinalIgnoreCase))
                {
                    used.Add(i);
                    return d;
                }
            }
            return null;
        }
        var result = new List<object>();
        if (lspci.Count > 0)
        {
            foreach (object raw in lspci)
            {
                var g = new PyDict(Py.D(raw));
                PyDict? d = TakeDrm(Py.S(g.Get("vendor")));
                if (d != null)
                {
                    g["driver"] = Py.Or(d.Get("driver"), "");
                    g["vram_mb"] = Py.Or(d.Get("vram_mb"), 0L);
                }
                result.Add(g);
            }
            return result;
        }
        int nv = 0;
        foreach (object raw in drm)
        {
            var g = new PyDict(Py.D(raw));
            if (Py.S(g.Get("vendor")) == "NVIDIA" && nv < nvidia.Count)
            {
                g["name"] = Py.D(nvidia[nv]).Get("name");
                nv++;
            }
            result.Add(g);
        }
        for (; nv < nvidia.Count; nv++)
        {
            result.Add(nvidia[nv]);
        }
        return result;
    }

    // ───────────────────────────── 内部 ─────────────────────────────

    private static string VendorFromName(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("nvidia"))
        {
            return "NVIDIA";
        }
        if (lower.Contains("amd") || lower.Contains("ati ") || lower.Contains("advanced micro devices") || lower.Contains("radeon"))
        {
            return "AMD";
        }
        if (lower.Contains("intel"))
        {
            return "Intel";
        }
        if (lower.Contains("microsoft"))
        {
            return "Microsoft";
        }
        return "";
    }

    /// <summary>
    /// nvidia-smi 的查询输出;没装 / 没有 NVIDIA 显卡时为空串(起不来立刻返回,不等超时)。
    /// 先找 PATH,再找 WSL2 的 /usr/lib/wsl/lib/nvidia-smi(非登录 shell 里它不一定在 PATH 上)。
    /// </summary>
    private static string NvidiaSmiText()
    {
        var args = new List<string> { "--query-gpu=name,memory.total,driver_version", "--format=csv,noheader,nounits" };
        string text = Sysinfo._run(new List<string> { "nvidia-smi" }.Concat(args).ToList(), 5.0);
        const string wsl = "/usr/lib/wsl/lib/nvidia-smi";
        if (ParseNvidiaSmi(text).Count == 0 && File.Exists(wsl))
        {
            text = Sysinfo._run(new List<string> { wsl }.Concat(args).ToList(), 5.0);
        }
        return text;
    }

    private static string ReadSmall(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static PyDict OsInfo()
    {
        string system = Utils.IS_WINDOWS ? "Windows" : (Utils.IS_MAC ? "Darwin" : "Linux");
        string pretty = "";
        if (!Utils.IS_WINDOWS && !Utils.IS_MAC)
        {
            foreach (string line in Py.SplitLines(ReadSmall("/etc/os-release")))
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    pretty = line.Substring(12).Trim().Trim('"');
                }
            }
        }
        string desc = "";
        try
        {
            desc = RuntimeInformation.OSDescription;
        }
        catch (Exception)
        {
        }
        return PyDict.Of("system", system, "display", pretty.Length > 0 ? pretty : desc, "description", desc,
            "arch", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
    }

    private static string Summary(PyDict snap)
    {
        var parts = new List<string>();
        string os = Py.S(Py.D(snap.Get("os")).Get("display"));
        if (os.Length > 0)
        {
            parts.Add(os);
        }
        string cpu = Py.S(Py.D(snap.Get("cpu")).Get("name"));
        if (cpu.Length > 0)
        {
            parts.Add(cpu);
        }
        long total = Py.Int(Py.Or(snap.Get("total_mb"), 0L));
        if (total > 0)
        {
            parts.Add((long)Math.Round(total / 1024.0) + "GB");
        }
        List<object> gpus = Py.ListOr(snap.Get("gpus"));
        if (gpus.Count > 0)
        {
            parts.Add(Py.S(Py.D(gpus[0]).Get("name")));
        }
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    private static object? CimJson(string script)
    {
        try
        {
            return Sysinfo._ps_json(script, 8.0);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<object> AsList(object? o)
        => o is List<object> list ? list : (o is PyDict d ? new List<object> { d } : new List<object>());

    private static PyDict WindowsCpu()
    {
        string name = Sysinfo._winreg_value("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString",
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "");
        long logical = Environment.ProcessorCount;
        long physical = 0;
        foreach (object raw in AsList(CimJson("Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors | ConvertTo-Json -Compress")))
        {
            PyDict row = Py.D(raw);
            try
            {
                physical += Py.Int(Py.Or(row.Get("NumberOfCores"), 0L));
            }
            catch (Exception)
            {
            }
            string cim = Py.Strip(Py.S(row.Get("Name")));
            if (name.Length == 0 && cim.Length > 0)
            {
                name = cim;
            }
        }
        return PyDict.Of("name", string.Join(" ", Py.SplitWs(name)), "cores_logical", logical, "cores_physical", physical);
    }

    private static List<object> WindowsGpus()
    {
        var list = new List<object>();
        foreach (object raw in AsList(CimJson("Get-CimInstance Win32_VideoController | Where-Object { $_.Name } | Select-Object Name,DriverVersion,AdapterRAM | ConvertTo-Json -Compress")))
        {
            PyDict row = Py.D(raw);
            string name = Py.Strip(Py.S(row.Get("Name")));
            if (name.Length == 0)
            {
                continue;
            }
            long ram = 0;
            try
            {
                ram = Py.Int(Py.Or(row.Get("AdapterRAM"), 0L));
            }
            catch (Exception)
            {
            }
            list.Add(PyDict.Of("name", name, "vendor", VendorFromName(name), "driver", Py.S(row.Get("DriverVersion")),
                "vram_mb", ram > 0 ? ram / 1048576 : 0L));
        }
        return Sysinfo._prefer_physical_gpus(list);
    }
}

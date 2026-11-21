// AI3-linux-real: Linux-side probe. Never opens anything for real: the only xdg-open on PATH is a
// recorder script (see wsl_probe.sh), and this program refuses to run if that is not the case.
using System.Collections;
using System.Reflection;
using System.Text;
using TipCL.Core;
using TipCL.Core.Launcher;

static class P
{
    static int fails;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + what);
        if (!ok) fails++;
    }

    static string Dump(object o, int ind = 0)
    {
        string pad = new string(' ', ind);
        if (o is IEnumerable<KeyValuePair<string, object>> d)
        {
            var sb = new StringBuilder("{\n");
            foreach (var kv in d) sb.Append(pad + "  " + kv.Key + ": " + Dump(kv.Value, ind + 2) + "\n");
            return sb + pad + "}";
        }
        if (o is string s) return "\"" + s + "\"";
        if (o is IEnumerable e)
        {
            var sb = new StringBuilder("[\n");
            foreach (var x in e) sb.Append(pad + "  " + Dump(x, ind + 2) + ",\n");
            return sb + pad + "]";
        }
        return o == null ? "null" : o.ToString();
    }

    static object Get(object dict, string key)
    {
        foreach (var kv in (IEnumerable<KeyValuePair<string, object>>)dict) if (kv.Key == key) return kv.Value;
        return null;
    }

    static long L(object o) => o == null ? -1 : Convert.ToInt64(o);

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "all";
        if (mode is "all" or "sysinfo") SysInfo();
        if (mode is "all" or "shell") Shell();
        Console.WriteLine(fails == 0 ? "PROBE OK" : "PROBE FAILURES: " + fails);
        return fails == 0 ? 0 : 1;
    }

    // ───────────────────────── SystemInfo.Snapshot vs /proc ─────────────────────────
    static void SysInfo()
    {
        Console.WriteLine("===== SystemInfo.Snapshot(force: true)");
        object snap = SystemInfo.Snapshot(force: true);
        Console.WriteLine(Dump(snap));
        Console.WriteLine("===== FormatText");
        Console.WriteLine(SystemInfo.FormatText((PyDict)snap));

        string[] cpuinfo = File.ReadAllLines("/proc/cpuinfo");
        long procCount = cpuinfo.Count(l => l.StartsWith("processor"));
        string model = cpuinfo.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? "";
        model = string.Join(" ", model.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var pairs = new HashSet<string>(); string pid = "0";
        foreach (string l in cpuinfo)
        {
            if (l.StartsWith("processor")) pid = "0";
            if (l.StartsWith("physical id")) pid = l.Split(':', 2)[1].Trim();
            if (l.StartsWith("core id")) pairs.Add(pid + ":" + l.Split(':', 2)[1].Trim());
        }
        object cpu = Get(snap, "cpu");
        Console.WriteLine($"/proc/cpuinfo: processors={procCount} model='{model}' unique(physical id,core id)={pairs.Count} Environment.ProcessorCount={Environment.ProcessorCount}");
        Check(L(Get(cpu, "cores_logical")) == procCount, "cpu.cores_logical == /proc/cpuinfo processor count");
        Check((string)Get(cpu, "name") == model, "cpu.name == /proc/cpuinfo model name");
        Check(L(Get(cpu, "cores_physical")) == pairs.Count, "cpu.cores_physical == unique (physical id, core id)");

        long memTotalKb = 0, memAvailKb = 0;
        foreach (string l in File.ReadAllLines("/proc/meminfo"))
        {
            if (l.StartsWith("MemTotal:")) memTotalKb = long.Parse(new string(l.Where(char.IsDigit).ToArray()));
            if (l.StartsWith("MemAvailable:")) memAvailKb = long.Parse(new string(l.Where(char.IsDigit).ToArray()));
        }
        object mem = Get(snap, "memory");
        Console.WriteLine($"/proc/meminfo: MemTotal={memTotalKb} kB ({memTotalKb / 1024} MB) MemAvailable={memAvailKb} kB ({memAvailKb / 1024} MB)");
        Check(L(Get(snap, "total_mb")) == memTotalKb / 1024, "total_mb == MemTotal/1024");
        Check(Math.Abs(L(Get(snap, "avail_mb")) - memAvailKb / 1024) < 512, "avail_mb ~= MemAvailable/1024 (±512MB, it moves)");
        Check((string)Get(mem, "source") == "/proc/meminfo", "memory.source == /proc/meminfo");
        Check((string)Get(Get(snap, "os"), "system") == "Linux", "os.system == Linux");

        var gpus = ((IEnumerable)Get(snap, "gpus")).Cast<object>().ToList();
        Console.WriteLine($"gpus: {gpus.Count}  (/sys/class/drm cards: {(Directory.Exists("/sys/class/drm") ? string.Join(",", Directory.GetDirectories("/sys/class/drm").Select(Path.GetFileName)) : "<no dir>")})");
        string expectGpus = Environment.GetEnvironmentVariable("PROBE_EXPECT_GPUS");
        if (!string.IsNullOrEmpty(expectGpus))
        {
            Check(gpus.Count == int.Parse(expectGpus), "gpu count == lspci display-class count (" + expectGpus + ")");
        }
        string expectName = Environment.GetEnvironmentVariable("PROBE_EXPECT_GPU0");
        if (!string.IsNullOrEmpty(expectName) && gpus.Count > 0)
        {
            Check(((string)Get(gpus[0], "name")).Contains(expectName), "gpus[0].name contains '" + expectName + "'");
        }
        Check(((string)Get(snap, "summary")).Length > 0, "summary not empty");
    }

    // ───────────────────────── xdg-open argv (fake recorder) ─────────────────────────
    static void Shell()
    {
        Console.WriteLine("===== xdg-open argument passing (fake xdg-open)");
        string logDir = Environment.GetEnvironmentVariable("XDG_OPEN_LOG_DIR");
        string which = Environment.GetEnvironmentVariable("PATH").Split(':').Select(d => Path.Combine(d, "xdg-open")).FirstOrDefault(File.Exists);
        Console.WriteLine("xdg-open resolves to: " + which);
        if (string.IsNullOrEmpty(logDir) || which == null || !File.ReadAllText(which).Contains("fake xdg-open"))
        {
            Check(false, "refusing to run: xdg-open on PATH is not the recorder");
            return;
        }
        Directory.CreateDirectory(logDir);
        foreach (string f in Directory.GetFiles(logDir)) File.Delete(f);

        Crash.ShellOpenHook = null;
        Auth.OpenBrowserHook = null;
        TipCL.App.Common.ShellOpen.Hook = null;

        string baseDir = Path.Combine(Path.GetTempPath(), "tipcl probe 中文 目录");
        Directory.CreateDirectory(baseDir);
        string[] paths =
        {
            baseDir,
            Path.Combine(baseDir, "a \"quoted\" name"),
            Path.Combine(baseDir, "trailing backslash\\"),
            Path.Combine(baseDir, "back\\\\slash\\\" mix"),
            Path.Combine(baseDir, "tab\there"),
            Path.Combine(baseDir, "$HOME;`id`&&|*?'single'"),
            Path.Combine(baseDir, "new\nline"),
            "",
            "-n",
        };
        var expected = new List<string>();
        foreach (string p in paths)
        {
            try { Crash.ShellOpen(p); expected.Add("Crash.ShellOpen|" + p); }
            catch (Exception ex) { Console.WriteLine("Crash.ShellOpen threw for " + Esc(p) + ": " + ex.GetType().Name + " " + ex.Message); }
            if (p.Length > 0)
            {
                TipCL.App.Common.ShellOpen.Folder(p);
                expected.Add("ShellOpen.Folder|" + p);
            }
        }
        string url = "https://example.invalid/path with space?a=1&b=中文#frag";
        TipCL.App.Common.ShellOpen.Url(url);
        expected.Add("ShellOpen.Url|" + url);
        Check(TipCL.App.Common.ShellOpen.TryUrl(url + "&try=1"), "ShellOpen.TryUrl returns true");
        expected.Add("ShellOpen.TryUrl|" + url + "&try=1");
        Check(Crash.OpenPath(baseDir), "Crash.OpenPath(existing dir) returns true");
        expected.Add("Crash.OpenPath|" + baseDir);
        Check(!Crash.OpenPath(Path.Combine(baseDir, "does-not-exist")), "Crash.OpenPath(missing) returns false and opens nothing");

        MethodInfo ob = typeof(Auth).GetMethod("OpenBrowser", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        string loginUrl = "https://www.microsoft.com/link?otc=ABCD";
        ob.Invoke(null, new object[] { loginUrl });
        string authExpect = Environment.GetEnvironmentVariable("PROBE_EXPECT_AUTH_OPEN") ?? "0";
        if (authExpect == "1") expected.Add("Auth.OpenBrowser|" + loginUrl);

        // xdg-open is started without waiting: give the recorder processes a moment
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.GetFiles(logDir).Length < expected.Count && DateTime.UtcNow < deadline) Thread.Sleep(100);
        Thread.Sleep(500);
        var calls = Directory.GetFiles(logDir).OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => File.ReadAllBytes(f)).Select(b => Encoding.UTF8.GetString(b)).ToList();
        Console.WriteLine($"recorded xdg-open invocations: {calls.Count}, expected: {expected.Count}");
        // recorder format: argc \0 argv[1] \0 ... argv[n] \0
        var got = calls.Select(c => c.Split('\0')).Select(a => a.Skip(1).Take(int.Parse(a[0])).ToArray()).ToList();
        foreach (var argv in got) Console.WriteLine("  argv[" + argv.Length + "] = " + string.Join(" , ", argv.Select(Esc)));
        Check(got.Count == expected.Count, "invocation count matches");
        var remaining = got.ToList();
        foreach (string e in expected)
        {
            string label = e.Substring(0, e.IndexOf('|'));
            string arg = e.Substring(e.IndexOf('|') + 1);
            int idx = remaining.FindIndex(a => a.Length == 1 && a[0] == arg);
            Check(idx >= 0, label + "(" + Esc(arg) + ") -> xdg-open received exactly [" + Esc(arg) + "]");
            if (idx >= 0) remaining.RemoveAt(idx);
        }
        foreach (var extra in remaining) Check(false, "unexpected xdg-open argv: " + string.Join(" , ", extra.Select(Esc)));
    }

    static string Esc(string s) => "«" + s.Replace("\n", "\\n").Replace("\t", "\\t") + "»";
}

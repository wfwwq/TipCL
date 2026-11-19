// 仓库一致性守卫(audit/D 新增)。
//
// 为什么需要:这批东西**没有任何编译期或运行时守卫**,坏了只会在"发布出去之后"才暴露:
//   1) 版本号散在 5 个地方 —— CoreInfo.Version(运行时自己报的那个)、两个 csproj 的 <Version>
//      (决定 exe 文件属性)、update.json / update-linux.json(客户端检查更新时读的那两个)。
//      任何一处漂移的后果是:包名/更新清单/自报版本对不上 ⇒
//      「.deb 文件名不是 tipcl_<源码版本>_amd64.deb」⇒ CI 的 verify-linux 判据直接红,
//      或者客户端永远认为自己是新版、永远不提示更新(P1 级,且静默)。
//   2) `.sh` 必须是 LF 且不能带 BOM。仓库主体是在 Windows 上开发的(core.autocrlf 常见为 true),
//      任何一次「从 Windows 重新加文件」都会把 bash 脚本写成 CRLF ⇒ 真机上
//      `bash: $'\r': command not found`,而 .gitattributes 此前没有任何 *.sh 规则。
//   3) .gitignore 漏一类产物 ⇒ 每次本机跑完测试工作区就脏,提交时容易把产物/日志带进仓库
//      (TestResults/*.trx 是 `dotnet test -l trx` 的默认落点)。
//   4) sln 漏登记工程 ⇒ `dotnet build <sln>` 编不到那个工程,它在 CI 里悄悄腐烂。
//
// 这些断言都只读仓库文件(几 KB),毫秒级、无网络;找不到仓库根时(例如测试被复制到别处跑)
// 一律**安静跳过**,不制造环境相关的假红。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests.Repo;

public class RepoConsistencyTests
{
    /// <summary>从测试输出目录往上找带 GOAL.md + src/ 的目录;找不到返回 null(用例自行跳过)。</summary>
    private static string? RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GOAL.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "src", "TipCL.Core")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    private static string Read(string root, string relative)
    {
        string p = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(p), $"仓库里应该有 {relative}(实际不存在:{p})");
        return File.ReadAllText(p);
    }

    private static string? ExtractCoreInfoVersion(string text)
    {
        Match m = Regex.Match(text, "Version\\s*=\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractCsprojVersion(string text)
    {
        Match m = Regex.Match(text, "<Version>([^<]+)</Version>");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractManifestVersion(string root, string relative)
    {
        using JsonDocument doc = JsonDocument.Parse(Read(root, relative));
        return doc.RootElement.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    [Fact]
    public void VersionIsIdenticalEverywhereItIsSpelled()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string core = ExtractCoreInfoVersion(Read(root, "src/TipCL.Core/CoreInfo.cs")) ?? "";
        string app = ExtractCsprojVersion(Read(root, "src/TipCL.App/TipCL.App.csproj")) ?? "";
        string win = ExtractCsprojVersion(Read(root, "windos/src/TipCL/TipCL.csproj")) ?? "";
        string upd = ExtractManifestVersion(root, "update.json") ?? "";
        string updLinux = ExtractManifestVersion(root, "update-linux.json") ?? "";
        // 运行时自报的那一个(启动横幅 / HTTP UA / 插件宿主默认版本都读它)
        string runtime = TipCL.Core.Launcher.AppInfo.APP_VERSION;

        Assert.False(string.IsNullOrEmpty(core), "src/TipCL.Core/CoreInfo.cs 里没找到 Version 常量");
        Assert.False(string.IsNullOrEmpty(app), "src/TipCL.App/TipCL.App.csproj 里没找到 <Version>");
        Assert.False(string.IsNullOrEmpty(win), "windos/src/TipCL/TipCL.csproj 里没找到 <Version>");
        Assert.False(string.IsNullOrEmpty(upd), "update.json 里没找到 version");
        Assert.False(string.IsNullOrEmpty(updLinux), "update-linux.json 里没找到 version");

        // CI 的 verify-linux 判据里就有一条「.deb 文件名 == tipcl_<CoreInfo 版本>_amd64.deb」,
        // 所以这几处必须逐字一致;不一致时把全部取值打印出来,别让人靠猜。
        string all = $"CoreInfo={core} / App.csproj={app} / windos.csproj={win} / update.json={upd} / update-linux.json={updLinux} / AppInfo.APP_VERSION={runtime}";
        Assert.True(core == runtime, "CoreInfo.Version 与 AppInfo.APP_VERSION(运行时自报版本)不一致:" + all);
        Assert.True(core == app, "CoreInfo.Version 与 src/TipCL.App 的 <Version> 不一致:" + all);
        Assert.True(core == win, "CoreInfo.Version 与 windos/src/TipCL 的 <Version> 不一致:" + all);
        Assert.True(core == upd, "CoreInfo.Version 与 update.json 的 version 不一致(客户端会永远认为自己是最新):" + all);
        Assert.True(core == updLinux, "CoreInfo.Version 与 update-linux.json 的 version 不一致(Linux 自更新会失效):" + all);
    }

    [Fact]
    public void UpdateManifestsPointAtTheirOwnArtifactAndCarryASha256()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        foreach ((string file, string expectedName) in new[]
        {
            ("update.json", "TipCL.exe"),
            ("update-linux.json", "TipCL-linux-x64.AppImage"),
        })
        {
            using JsonDocument doc = JsonDocument.Parse(Read(root, file));
            string url = doc.RootElement.GetProperty("url").GetString() ?? "";
            string sha = doc.RootElement.GetProperty("sha256").GetString() ?? "";

            Assert.EndsWith("/" + expectedName, url, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", sha);
        }
    }

    [Fact]
    public void AllShellScriptsAreLfOnlyWithoutBom()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        List<string> scripts = Directory
            .EnumerateFiles(Path.Combine(root, "tools"), "*.sh", SearchOption.AllDirectories)
            .Concat(Directory.Exists(Path.Combine(root, "windos", "tools"))
                ? Directory.EnumerateFiles(Path.Combine(root, "windos", "tools"), "*.sh", SearchOption.AllDirectories)
                : Enumerable.Empty<string>())
            .ToList();

        Assert.NotEmpty(scripts);   // tools/*.sh 本来就该在;一个都没有说明路径约定变了

        List<string> bad = new();
        foreach (string path in scripts)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string rel = Path.GetRelativePath(root, path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bad.Add(rel + "(带 UTF-8 BOM,bash 会把 shebang 认错)");
                continue;
            }
            if (Array.IndexOf(bytes, (byte)'\r') >= 0)
            {
                bad.Add(rel + "(含 CR:CRLF 结帐在 Linux 上是 `$'\\r': command not found`)");
                continue;
            }
            string head = Encoding.UTF8.GetString(bytes, 0, Math.Min(40, bytes.Length));
            if (!head.StartsWith("#!", StringComparison.Ordinal))
            {
                bad.Add(rel + "(第一行不是 shebang)");
            }
        }

        Assert.True(bad.Count == 0, "以下 shell 脚本不满足 LF + 无 BOM + shebang:" + Environment.NewLine
            + string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void GitAttributesPinsShellScriptsToLf()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string attrs = Read(root, ".gitattributes");
        // 只要求"存在 *.sh 的 eol=lf 规则";具体写法允许前后空白
        Assert.Matches("(?m)^\\s*\\*\\.[sS][hH]\\s+text\\s+eol=lf\\s*$", attrs);
    }

    [Fact]
    public void GitIgnoreCoversTestAndBuildArtifacts()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string[] lines = Read(root, ".gitignore")
            .Split('\n')
            .Select(l => l.Trim().TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToArray();

        // 每个条目都是"本机跑一次构建/测试就会生成的东西"
        foreach (string required in new[]
        {
            "bin/", "obj/", "dist/", "smoke.png",
            "TestResults/", "*.trx",
        })
        {
            Assert.True(
                lines.Any(l => string.Equals(l, required, StringComparison.Ordinal) || string.Equals(l, required.TrimEnd('/'), StringComparison.Ordinal)),
                $".gitignore 缺 `{required}` —— 本机跑 dotnet test / 出包后工作区会脏,容易把产物提交进仓库");
        }
    }

    [Fact]
    public void BothSolutionsRegisterEveryProjectThatBelongsToThem()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        // Linux 树:三个工程一个都不能漏(TipCL.App / TipCL.Core / TipCL.Tests)
        string linuxSln = Read(root, "TipCL.Linux.sln");
        foreach (string csproj in new[]
        {
            "src/TipCL.App/TipCL.App.csproj",
            "src/TipCL.Core/TipCL.Core.csproj",
            "tests/TipCL.Tests/TipCL.Tests.csproj",
        })
        {
            string winPath = csproj.Replace('/', '\\');
            Assert.True(linuxSln.Contains(winPath, StringComparison.OrdinalIgnoreCase),
                $"TipCL.Linux.sln 没登记 {csproj}(dotnet build <sln> 会编不到它)");
        }

        // Windows 树:TipCL 与它的测试工程必须在;TipCL.NativeBlockE2E 是**刻意**按路径构建的驱动
        // (见该工程 csproj 头部注释:net48 控制台 + 真 WPF,不进 sln),这里不要求它登记。
        string winSln = Read(root, "windos/TipCL.sln");
        foreach (string csproj in new[]
        {
            "src\\TipCL\\TipCL.csproj",
            "tests\\TipCL.Tests\\TipCL.Tests.csproj",
        })
        {
            Assert.True(winSln.Contains(csproj, StringComparison.OrdinalIgnoreCase),
                $"windos/TipCL.sln 没登记 {csproj}");
        }
    }

    [Fact]
    public void EmbeddedResourceLogicalNamesMatchWhatCoreResourcesLooksFor()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        // 内嵌词表的 LogicalName 写错 ⇒ 资源还在、但 CoreResources.ListDir("locales") 枚举不到
        // ⇒ Tr() 全部回落成 key 本身(界面整片显示中文 key)。这里钉住 Linux 侧那两条。
        string csproj = Read(root, "src/TipCL.Core/TipCL.Core.csproj");
        Assert.Contains("LogicalName=\"TipCL.Core.locales.en.json\"", csproj, StringComparison.Ordinal);
        Assert.Contains("LogicalName=\"TipCL.Core.locales.zh_CN.json\"", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyLinuxScriptDefaultsToTheDirectoryThePackagingScriptsActuallyProduce()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        // tools/package.sh / package.ps1 / CI 都发布到 dist/TipCL-linux-x64;
        // verify-linux.sh 的默认参数若指向别的目录,裸跑一次就是「找不到产物」直接 exit 1。
        string text = Read(root, "tools/verify-linux.sh");
        Assert.Contains("$ROOT/dist/TipCL-linux-x64", text, StringComparison.Ordinal);
    }
}
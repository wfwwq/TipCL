// AI3-i18n:守住 tools/i18n-audit.py 的「运行时接线」标记(i18n:runtime-tr)。
//
// 这个标记让审计把「字面量本身不是 Tr(...) 的实参,但所在的表取出后一定过 I18nCore.Tr」的字面量算作已接线
// (老工具标签字典 Tools.Labels / Formats、步骤行前缀表 AiUiText.StateKeys)。它是一句**声明**,
// 不能随手往别处贴来压低未接线计数 —— 所以:
//   1. 源码里用到标记的文件必须正好等于 i18n-audit.py 的 RUNTIME_TR_FILES 白名单(多了少了都红);
//   2. begin / end 必须成对;
//   3. 白名单里每个文件都有行为测试证明 en 下确实出英文(Tools.cs → AiI18nUxLabelTests.EveryToolLabel_*;
//      AiUiText.cs → AiI18nResidueCoreTests.StepPrefixesAndSummary_*),新增文件时要一并补。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests.Repo;

public class I18nRuntimeTrMarkerTests
{
    private const string Mark = "i18n:runtime-tr";

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GOAL.md"))
                && File.Exists(Path.Combine(dir.FullName, "tools", "i18n-audit.py")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (string sub in new[] { Path.Combine("src", "TipCL.App"), Path.Combine("src", "TipCL.Core") })
        {
            string dir = Path.Combine(root, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/"))
                {
                    continue;
                }
                yield return rel;
            }
        }
    }

    [Fact]
    public void MarkerIsOnlyUsedInTheAuditWhitelist_AndBlocksArePaired()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }
        string audit = File.ReadAllText(Path.Combine(root, "tools", "i18n-audit.py"));
        Match m = Regex.Match(audit, @"RUNTIME_TR_FILES\s*=\s*\((?<body>.*?)\)", RegexOptions.Singleline);
        Assert.True(m.Success, "i18n-audit.py 里找不到 RUNTIME_TR_FILES");
        var whitelist = Regex.Matches(m.Groups["body"].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Contains("src/TipCL.Core/Launcher/Ai/Tools.cs", whitelist);

        var used = new List<string>();
        foreach (string rel in SourceFiles(root))
        {
            string[] lines = File.ReadAllLines(Path.Combine(root, rel));
            if (!lines.Any(l => l.Contains(Mark, StringComparison.Ordinal)))
            {
                continue;
            }
            used.Add(rel);
            int begins = lines.Count(l => l.Contains(Mark + "-begin", StringComparison.Ordinal));
            int ends = lines.Count(l => l.Contains(Mark + "-end", StringComparison.Ordinal));
            Assert.True(begins == ends, rel + " 的 i18n:runtime-tr-begin / -end 不成对(" + begins + " / " + ends + ")");
        }
        used.Sort(StringComparer.Ordinal);
        Assert.True(whitelist.SequenceEqual(used),
            "用到 i18n:runtime-tr 标记的文件与 tools/i18n-audit.py 的 RUNTIME_TR_FILES 不一致。\n"
            + "白名单:" + string.Join(", ", whitelist) + "\n实际:" + string.Join(", ", used) + "\n"
            + "这个标记会让审计把字面量算作已接线 —— 只有确实在运行时过 I18nCore.Tr 的表才能用,并且要补一条 en 下不含中文的行为测试。");
    }

    [Fact]
    public void ToolsLabelReallyPassesTheTablesThroughTr()
    {
        // 标记的前提:Tools.Label 读完 Labels / Formats 之后一定过 I18nCore.Tr(源码层面的锁,行为层面见 AiI18nUxLabelTests)
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }
        string src = File.ReadAllText(Path.Combine(root, "src", "TipCL.Core", "Launcher", "Ai", "Tools.cs"));
        Assert.Contains("FillTemplate(I18nCore.Tr(fmt), args)", src);
        Assert.Contains("Labels.TryGetValue(key, out string? label) ? I18nCore.Tr(label) : key", src);
        string ui = File.ReadAllText(Path.Combine(root, "src", "TipCL.Core", "Launcher", "Ai", "AiUiText.cs"));
        Assert.Contains("string prefix = I18nCore.Tr(zh);", ui);
    }
}

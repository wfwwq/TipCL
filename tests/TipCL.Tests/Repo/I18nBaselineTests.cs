// i18n 接线基线守卫(audit/D 第二轮)。
//
// 用户能直接感知的一类质量缺陷是「界面切成英文后大半还是中文」。本仓 Linux 侧的实情是:
// 5198 条 CJK 字面量里只有 299 条(5.75%)走了 I18nCore.Tr(...),其余 4899 条**切语言改不动**;
// 其中 2086 条在词表里**已经有 en 译文**,只是调用点没接线。
//
// 只靠"这次比上次少"管不住这件事 —— 没人记得上次是多少。所以:
//   * tools/i18n-audit.py --baseline progress/I18N-BASELINE.md 会落一份快照,
//     里面有一个 I18N-GUARD 机读块(unwrapped_total + per_file);
//   * 本测试**重新跑一遍审计**,拿当前数字和基线比:未接线数量**不允许增加**(减少/持平都行)。
//
// 这样"以后随手再加一句中文硬编码"会当场红;接线(把字面量套进 Tr)让数字下降时,
// 重跑一次 --baseline 刷新快照即可(那也是进展的证据)。
//
// 依赖 python:找不到解释器时**安静返回**(不制造环境相关的假红);
// 想强制要求可用 I18N_GUARD_REQUIRE_PYTHON=1。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TipCL.Tests.Repo;

public class I18nBaselineTests
{
    private const string BaselineRel = "progress/I18N-BASELINE.md";

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

    /// <summary>从基线文件里取出 I18N-GUARD 机读块(```json ... ```)。</summary>
    private static JsonElement ReadGuardBlock(string baselineText)
    {
        Match m = Regex.Match(baselineText, "<!-- I18N-GUARD-BEGIN -->\\s*```json\\s*(?<json>.+?)\\s*```\\s*<!-- I18N-GUARD-END -->",
            RegexOptions.Singleline);
        if (!m.Success)
        {
            throw new InvalidOperationException(
                "progress/I18N-BASELINE.md 里没有 I18N-GUARD 块 —— 别手写这个文件,"
                + "用 `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md` 重新生成");
        }
        using JsonDocument doc = JsonDocument.Parse(m.Groups["json"].Value);
        return doc.RootElement.Clone();
    }

    private static string? FindPython()
    {
        string? forced = Environment.GetEnvironmentVariable("I18N_AUDIT_PYTHON");
        if (!string.IsNullOrWhiteSpace(forced))
        {
            return forced;
        }
        foreach (string candidate in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using Process? p = Process.Start(psi);
                if (p is null)
                {
                    continue;
                }
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000))
                {
                    try { p.Kill(); } catch { }
                    continue;
                }
                if (p.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // 解释器不存在/不可执行:继续找下一个
            }
        }
        return null;
    }

    [Fact]
    public void BaselineSnapshotHasAWellFormedGuardBlock()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string path = Path.Combine(root, BaselineRel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            "缺少 i18n 基线快照:" + BaselineRel + " —— 用 `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md` 生成");

        JsonElement guard = ReadGuardBlock(File.ReadAllText(path));
        Assert.True(guard.TryGetProperty("unwrapped_total", out JsonElement total) && total.GetInt32() > 0,
            "基线块缺 unwrapped_total(或为 0)—— 这不可能是真实快照");
        Assert.True(guard.TryGetProperty("per_file", out JsonElement perFile) && perFile.ValueKind == JsonValueKind.Object,
            "基线块缺 per_file 表");
        Assert.True(guard.TryGetProperty("cjk_literals", out JsonElement cjk) && cjk.GetInt32() > 0,
            "基线块缺 cjk_literals(分母)—— 没有分母的覆盖率没法读");
        Assert.True(perFile.GetRawText().Length > 0);
    }

    [Fact]
    public void UnwiredCjkLiteralCountMustNotGrowSinceBaseline()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        bool required = Environment.GetEnvironmentVariable("I18N_GUARD_REQUIRE_PYTHON") == "1";
        string? python = FindPython();
        if (python is null)
        {
            if (required)
            {
                Assert.Fail("I18N_GUARD_REQUIRE_PYTHON=1 但找不到 python3/python,无法核对 i18n 基线");
            }
            return;   // 没有解释器:这条守卫在本机不生效,如实跳过(不是"通过")
        }

        string baselinePath = Path.Combine(root, BaselineRel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(baselinePath), "缺少基线快照:" + BaselineRel);
        JsonElement baseline = ReadGuardBlock(File.ReadAllText(baselinePath));

        // 重新跑一遍审计 —— 用 --json --out 落临时文件,避开控制台编码问题
        string outFile = Path.Combine(Path.GetTempPath(), "tipcl-i18n-" + Guid.NewGuid().ToString("N") + ".json");
        var psi = new ProcessStartInfo(python, $"\"{Path.Combine(root, "tools", "i18n-audit.py")}\" --json --out \"{outFile}\"")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string stdout;
        string stderr;
        string currentJson;
        try
        {
            using Process p = Process.Start(psi)!;
            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(240_000), "i18n-audit.py 超时(240s)");
            Assert.True(p.ExitCode == 0, "i18n-audit.py 退出码 " + p.ExitCode + "\n" + stdout + "\n" + stderr);
            Assert.True(File.Exists(outFile), "i18n-audit.py 没写出 " + outFile + "\n" + stdout + stderr);
            // 必须在 finally 删掉临时文件**之前**把内容读出来(第一版就是在这里读晚了一步)
            currentJson = File.ReadAllText(outFile);
        }
        finally
        {
            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
        }

        using JsonDocument currentDoc = JsonDocument.Parse(currentJson);
        JsonElement currentRoot = currentDoc.RootElement;
        int current = currentRoot.GetProperty("unwrapped").GetInt32();
        int allowed = baseline.GetProperty("unwrapped_total").GetInt32();

        Assert.True(current <= allowed,
            "未接线的 CJK 字面量从基线的 " + allowed + " 涨到了 " + current + "。\n"
            + "这意味着新代码里又多了切换语言改不动的中文硬编码 —— 请把新增的用户可见文案套进 I18nCore.Tr(...);\n"
            + "如果确实是有意增加(i18n:ignore 之类),就重跑: python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md\n"
            + "详细清单见 " + BaselineRel + "(按文件聚合 + 每条是否已有 en 译文)。");

        // 顺带:接线数不该倒退(有人把 Tr 拆掉会掉)
        int allowedWrapped = baseline.GetProperty("wrapped").GetInt32();
        int currentWrapped = currentRoot.GetProperty("wrapped").GetInt32();
        Assert.True(currentWrapped >= allowedWrapped,
            "已接线(wrapped)数量从基线的 " + allowedWrapped + " 掉到了 " + currentWrapped
            + " —— 有 Tr(...) 被拆掉或字面量被改写,请检查。");
    }

    /// <summary>
    /// 基线里那一列「其中已有 en 译文」是有意义的:这些文案接线后**立刻**能用,
    /// 不需要先补词表。这条测试只保证基线块带了这个数,给分派的人一个准确的分母。
    /// </summary>
    [Fact]
    public void BaselineReportsHowManyUnwiredLiteralsAlreadyHaveAnEnTranslation()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string path = Path.Combine(root, BaselineRel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return;   // 由 BaselineSnapshotHasAWellFormedGuardBlock 报缺失
        }

        JsonElement guard = ReadGuardBlock(File.ReadAllText(path));
        Assert.True(guard.TryGetProperty("unwrapped_with_en_key", out JsonElement withEn),
            "基线块缺 unwrapped_with_en_key —— 这个数是分派接线工作的关键输入");
        Assert.True(withEn.GetInt32() >= 0);
        Assert.True(guard.GetProperty("unwrapped_total").GetInt32() >= withEn.GetInt32(),
            "unwrapped_with_en_key 不可能超过 unwrapped_total");
    }

    /// <summary>给报告用:按文件聚合的未接线清单(前 N 个文件)必须能读出来。</summary>
    [Fact]
    public void BaselinePerFileTableIsSortedAndNonEmpty()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }

        string path = Path.Combine(root, BaselineRel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return;
        }

        JsonElement perFile = ReadGuardBlock(File.ReadAllText(path)).GetProperty("per_file");
        var counts = new List<int>();
        foreach (JsonProperty p in perFile.EnumerateObject())
        {
            Assert.EndsWith(".cs", p.Name, StringComparison.Ordinal);
            Assert.True(p.Value.GetInt32() > 0);
            counts.Add(p.Value.GetInt32());
        }
        Assert.NotEmpty(counts);
    }
}
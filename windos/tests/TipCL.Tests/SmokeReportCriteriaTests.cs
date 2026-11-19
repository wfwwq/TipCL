// WPF 冒烟矩阵的「判据」测试(audit/D 第二轮)。
//
// 背景:net48 版 `--smoke` 会真开窗口、遍历每一页、点一批按钮、开一批对话框,把结果写成
// <TIPCL_HOME>/_wpf_smoke/run-<ts>/report.json。这份 report 是唯一的证据来源,但过去
// 只有"人工跑一次、人工看一眼" —— 结论没落盘、没判据、也没法在别人机器上重跑。
//
// 本文件把判据固化成**可测的纯函数**(Evaluate),并用合成 report 覆盖每一类失败;
// 同时,如果设置了环境变量 TIPCL_WPF_SMOKE_REPORT(由 windos/tools/smoke-matrix.ps1 产出的
// 真报告路径),就对**真实报告**再跑一遍判据 —— 那是本机/真机回归的门槛。
//
// 9 条判据(与 windos/tools/smoke-matrix.ps1 的 C1..C9 逐条对应):
//   C1 进程退出码 0            C6 对话框数 >= required_dialogs
//   C2 state == finished       C7 每页截图存在且 > 10 KB(不是"截图失败:...")
//   C3 issues 为空             C8 i18n_missing 为空
//   C4 页数 >= 10 且每页 ok    C9 不是 watchdog 超时(退出码 99 / state=watchdog-timeout)
//   C5 点击数 >= required_clicks 且每次 ok
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TipCL.Tests;

public class SmokeReportCriteriaTests
{
    /// <summary>判据的最小阈值(与 ps1 的 -MinPages / -MinShotBytes 一致)。</summary>
    private const int MinPages = 10;
    private const long MinShotBytes = 10240;

    /// <summary>评估结果:空 = 全部通过。</summary>
    public sealed class Verdict
    {
        public List<string> Problems { get; } = new List<string>();
        public bool Ok => Problems.Count == 0;
        public bool Has(string tag) => Problems.Any(p => p.StartsWith(tag + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// 判据实现。exitCode = 进程退出码;reportJson = report.json 原文(null 表示没找到)。
    /// shotSize = 截图路径 → 字节数(缺失的路径不在表里)。
    /// </summary>
    private static Verdict Evaluate(int exitCode, string? reportJson, Func<string, long> shotSize, bool timedOut = false)
    {
        var v = new Verdict();

        // C1 / C9:进程层面
        if (timedOut) v.Problems.Add("C1 进程超时被强杀");
        else if (exitCode != 0) v.Problems.Add($"C1 退出码 {exitCode}");
        if (exitCode == 99) v.Problems.Add("C9 watchdog 超时(退出码 99)");

        if (string.IsNullOrWhiteSpace(reportJson))
        {
            v.Problems.Add("C2 没有 report.json");
            return v;
        }

        using JsonDocument doc = JsonDocument.Parse(reportJson);
        JsonElement root = doc.RootElement;

        // C2
        string state = GetString(root, "state");
        if (state != "finished") v.Problems.Add($"C2 state={state}");
        if (state == "watchdog-timeout") v.Problems.Add("C9 state=watchdog-timeout");

        // C3
        JsonElement issues = GetArray(root, "issues");
        int issueCount = issues.ValueKind == JsonValueKind.Array ? issues.GetArrayLength() : 0;
        if (issueCount > 0) v.Problems.Add($"C3 issues {issueCount} 条");

        // C4 + C7
        JsonElement pages = GetArray(root, "pages");
        int pageCount = pages.ValueKind == JsonValueKind.Array ? pages.GetArrayLength() : 0;
        if (pageCount < MinPages) v.Problems.Add($"C4 只覆盖 {pageCount} 页(< {MinPages})");
        if (pages.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement page in pages.EnumerateArray())
            {
                string id = GetString(page, "id");
                if (!GetBool(page, "ok")) v.Problems.Add($"C4 页 '{id}' ok=false");

                string shot = GetString(page, "shot");
                if (string.IsNullOrEmpty(shot) || shot.StartsWith("截图失败", StringComparison.Ordinal))
                {
                    v.Problems.Add($"C7 页 '{id}' 没有截图({shot})");
                    continue;
                }
                long bytes = shotSize(shot);
                if (bytes <= MinShotBytes) v.Problems.Add($"C7 页 '{id}' 截图只有 {bytes} B(<= {MinShotBytes})");
            }
        }

        // C5
        JsonElement clicks = GetArray(root, "clicks");
        int clickCount = clicks.ValueKind == JsonValueKind.Array ? clicks.GetArrayLength() : 0;
        int requiredClicks = GetInt(root, "required_clicks");
        if (clickCount < requiredClicks) v.Problems.Add($"C5 只点了 {clickCount} 次(< required_clicks {requiredClicks})");
        if (clicks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement c in clicks.EnumerateArray())
            {
                if (!GetBool(c, "ok")) v.Problems.Add($"C5 点击 '{GetString(c, "page")}' / '{GetString(c, "label")}' ok=false");
            }
        }

        // C6
        JsonElement dialogs = GetArray(root, "dialogs");
        int dialogCount = dialogs.ValueKind == JsonValueKind.Array ? dialogs.GetArrayLength() : 0;
        int requiredDialogs = GetInt(root, "required_dialogs");
        if (dialogCount < requiredDialogs) v.Problems.Add($"C6 只开了 {dialogCount} 个对话框(< required_dialogs {requiredDialogs})");

        // C8
        JsonElement missing = GetArray(root, "i18n_missing");
        int missingCount = missing.ValueKind == JsonValueKind.Array ? missing.GetArrayLength() : 0;
        if (missingCount > 0) v.Problems.Add($"C8 i18n_missing {missingCount} 条");

        return v;
    }

    private static string GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static bool GetBool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : 0;

    private static JsonElement GetArray(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) ? v : default;

    // ————— 合成 report:一份"全过"的样板,dirty 参数用来逐项污染 —————

    private static string Report(
        int pageCount = 10,
        bool pageOk = true,
        int clickCount = 9,
        bool clickOk = true,
        int dialogCount = 5,
        string state = "finished",
        bool issues = false,
        bool i18nMissing = false,
        bool badShotOnFirstPage = false,
        string firstShot = "/tmp/frames/launch.png")
    {
        var pages = new List<string>();
        for (int i = 0; i < pageCount; i++)
        {
            string id = "page" + i;
            string shot = i == 0
                ? (badShotOnFirstPage ? "截图失败:RenderTargetBitmap 失败" : firstShot)
                : $"/tmp/frames/{id}.png";
            pages.Add($"{{\"id\":\"{id}\",\"title\":\"标题{i}\",\"ok\":{(pageOk ? "true" : "false")},\"ms\":{i + 1},\"shot\":\"{shot}\"}}");
        }
        var clicks = new List<string>();
        for (int i = 0; i < clickCount; i++)
        {
            clicks.Add($"{{\"page\":\"page{i % 3}\",\"label\":\"按钮{i}\",\"ok\":{(clickOk ? "true" : "false")},\"ms\":{100 + i}}}");
        }
        var dialogs = new List<string>();
        for (int i = 0; i < dialogCount; i++)
        {
            dialogs.Add($"{{\"title\":\"对话框{i}\",\"type\":\"in-app\",\"answered_with\":\"取消\"}}");
        }
        string issuesJson = issues ? "[{\"where\":\"page0\",\"text\":\"NullReferenceException\"}]" : "[]";
        string missingJson = i18nMissing ? "[\"登录\"]" : "[]";

        return "{"
            + $"\"state\":\"{state}\",\"exit_code\":0,\"run\":\"run-test\","
            + "\"required_clicks\":9,\"required_dialogs\":5,"
            + $"\"pages\":[{string.Join(",", pages)}],"
            + $"\"clicks\":[{string.Join(",", clicks)}],"
            + $"\"dialogs\":[{string.Join(",", dialogs)}],"
            + $"\"issues\":{issuesJson},"
            + $"\"i18n_missing\":{missingJson}"
            + "}";
    }

    /// <summary>所有截图都按"足够大"回答。</summary>
    private static long BigShot(string path) => 40 * 1024;

    [Fact]
    public void CleanReportPassesAllNineCriteria()
    {
        Verdict v = Evaluate(0, Report(), BigShot);
        Assert.True(v.Ok, "干净报告不该红:" + string.Join(" | ", v.Problems));
    }

    [Fact]
    public void NonZeroExitIsRed()
    {
        Verdict v = Evaluate(1, Report(), BigShot);
        Assert.True(v.Has("C1"));
    }

    [Fact]
    public void WatchdogTimeoutIsRed()
    {
        Verdict v = Evaluate(99, Report(state: "watchdog-timeout"), BigShot);
        Assert.True(v.Has("C1"));
        Assert.True(v.Has("C9"));
    }

    [Fact]
    public void MissingReportIsRed()
    {
        Verdict v = Evaluate(0, null, BigShot);
        Assert.True(v.Has("C2"));
    }

    [Fact]
    public void UnfinishedStateIsRed()
    {
        Verdict v = Evaluate(0, Report(state: "running"), BigShot);
        Assert.True(v.Has("C2"));
    }

    [Fact]
    public void AnyIssueIsRed()
    {
        Verdict v = Evaluate(0, Report(issues: true), BigShot);
        Assert.True(v.Has("C3"));
    }

    [Fact]
    public void FailingPageIsRed()
    {
        Verdict v = Evaluate(0, Report(pageOk: false), BigShot);
        Assert.True(v.Has("C4"));
    }

    [Fact]
    public void TooFewPagesIsRed()
    {
        Verdict v = Evaluate(0, Report(pageCount: 4), BigShot);
        Assert.True(v.Has("C4"));
    }

    [Fact]
    public void TooFewClicksOrFailingClickIsRed()
    {
        Assert.True(Evaluate(0, Report(clickCount: 3), BigShot).Has("C5"));
        Assert.True(Evaluate(0, Report(clickOk: false), BigShot).Has("C5"));
    }

    [Fact]
    public void TooFewDialogsIsRed()
    {
        Assert.True(Evaluate(0, Report(dialogCount: 1), BigShot).Has("C6"));
    }

    [Fact]
    public void MissingOrTinyScreenshotIsRed()
    {
        // 第一页 shot 是"截图失败:..."字符串
        Assert.True(Evaluate(0, Report(badShotOnFirstPage: true), BigShot).Has("C7"));
        // 第一页截图存在但很小(近空白帧)
        Assert.True(Evaluate(0, Report(), _ => 100).Has("C7"));
        // 第一页截图文件根本不存在
        Assert.True(Evaluate(0, Report(), path => path.Contains("launch") ? 0 : BigShot(path)).Has("C7"));
    }

    [Fact]
    public void MissingI18nKeyIsRed()
    {
        Assert.True(Evaluate(0, Report(i18nMissing: true), BigShot).Has("C8"));
    }

    // ————— 判据实现与脚本的对应关系(防两边各写一套后漂移)————

    [Fact]
    public void ScriptDeclaresTheSameNineCriteriaTags()
    {
        string? root = RepoRoot();
        if (root is null) return;

        string script = Path.Combine(root, "windos", "tools", "smoke-matrix.ps1");
        Assert.True(File.Exists(script), "windos/tools/smoke-matrix.ps1 不存在:" + script);
        // ps1 是无 BOM 时 PowerShell 5.1 会按 ANSI 读、含中文就解析失败 —— 这里顺手守住 BOM
        byte[] bytes = File.ReadAllBytes(script);
        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "windos/tools/smoke-matrix.ps1 缺 UTF-8 BOM:PowerShell 5.1 会把中文按 ANSI 读,脚本直接解析失败");

        string text = File.ReadAllText(script);
        for (int i = 1; i <= 9; i++)
        {
            Assert.Contains($"C{i}", text, StringComparison.Ordinal);
        }
        Assert.Contains("required_clicks", text, StringComparison.Ordinal);
        Assert.Contains("i18n_missing", text, StringComparison.Ordinal);
    }

    // ————— 真报告门槛(opt-in):smoke-matrix.ps1 产出后设这个环境变量再跑测试 —————

    [Fact]
    public void RealReportFromSmokeMatrixScriptPasses()
    {
        string? reportPath = Environment.GetEnvironmentVariable("TIPCL_WPF_SMOKE_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath)) return;   // 没给就不跑:CI 的 windows runner 没有交互桌面
        Assert.True(File.Exists(reportPath), "TIPCL_WPF_SMOKE_REPORT 指向的文件不存在:" + reportPath);

        string json = File.ReadAllText(reportPath);
        string dir = Path.GetDirectoryName(reportPath) ?? ".";
        long ShotSize(string p)
        {
            string full = Path.IsPathRooted(p) ? p : Path.Combine(dir, p);
            return File.Exists(full) ? new FileInfo(full).Length : 0;
        }

        Verdict v = Evaluate(0, json, ShotSize);
        Assert.True(v.Ok, "真报告没过判据:" + Environment.NewLine + string.Join(Environment.NewLine, v.Problems));
    }

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GOAL.md"))
                && Directory.Exists(Path.Combine(dir.FullName, "windos", "tools")))
            {
                return dir.FullName;
            }
        }
        return null;
    }
}
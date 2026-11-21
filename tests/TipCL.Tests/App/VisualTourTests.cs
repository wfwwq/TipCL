// AI3-visual:--visual-tour 巡检工具的单测 —— 剧本解析、按 Name / 文字找控件、找不到时记 FAIL 不崩、
// 假数据目录生成器、离线闸门的假 Modrinth 结果。
// 碰 Avalonia 的用例一律走 TestApp.Run(无头会话专属 UI 线程)。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TipCL.App.Session;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class VisualTourScriptTests
{
    [Fact]
    public void Parse_ReadsHeaderAndSteps_WithCommentsAndTrailingCommas()
    {
        const string json = @"{
          // 注释与尾逗号都允许(剧本是手写的)
          ""width"": 1000, ""height"": 640, ""theme"": ""dark"", ""fixture"": true, ""settleMs"": 120,
          ""steps"": [
            { ""op"": ""navigate"", ""page"": ""download"" },
            { ""op"": ""rightclick"", ""text"": ""26.3"" },
            { ""op"": ""click"", ""name"": ""AiBtn"", ""ms"": 50 },
            { ""op"": ""click"", ""type"": ""Button"", ""contains"": ""编辑"", ""index"": 1 },
            { ""op"": ""key"", ""keys"": ""Ctrl+Z"" },
            { ""op"": ""scroll"", ""to"": { ""text"": ""方案"" } },
            { ""op"": ""scroll"", ""screens"": 1.5, ""in"": { ""name"": ""Sv"" } },
            { ""op"": ""resize"", ""width"": 780, ""height"": 600 },
            { ""op"": ""theme"", ""value"": ""light"" },
            { ""op"": ""wait"" },
            { ""op"": ""shot"", ""name"": ""dl-edit"", ""layer"": ""main"" },
            { ""op"": ""expect"", ""tip"": ""关闭"", ""absent"": true },
          ],
        }";
        TourScript s = TourScriptParser.Parse(json);
        Assert.Equal(1000, s.Width);
        Assert.Equal(640, s.Height);
        Assert.Equal("dark", s.Theme);
        Assert.True(s.Fixture);
        Assert.Equal(120, s.SettleMs);
        Assert.Empty(s.Problems);
        Assert.Equal(12, s.Steps.Count);
        Assert.Equal("download", s.Steps[0].Page);
        Assert.Equal("26.3", s.Steps[1].Target!.Text);
        Assert.Equal("AiBtn", s.Steps[2].Target!.Name);
        Assert.Equal(50, s.Steps[2].Ms);
        Assert.Equal("Button", s.Steps[3].Target!.Type);
        Assert.Equal("编辑", s.Steps[3].Target!.Contains);
        Assert.Equal(1, s.Steps[3].Target!.Index);
        Assert.Equal("Ctrl+Z", s.Steps[4].Keys);
        Assert.Equal("方案", s.Steps[5].ScrollTo!.Text);
        Assert.Equal(1.5, s.Steps[6].Screens);
        Assert.Equal("Sv", s.Steps[6].Target!.Name);
        Assert.Equal(780, s.Steps[7].Width);
        Assert.Equal("light", s.Steps[8].Theme);
        Assert.Equal(300, s.Steps[9].Ms);   // wait 缺省 300ms
        Assert.Equal("main", s.Steps[10].Layer);
        Assert.True(s.Steps[11].Absent);
        Assert.Equal(Enumerable.Range(0, 12), s.Steps.Select(x => x.Index));
    }

    [Fact]
    public void Parse_RecordsProblems_ButKeepsBadSteps()
    {
        TourScript s = TourScriptParser.Parse(@"[
            { ""op"": ""fly"" },
            { ""op"": ""navigate"" },
            { ""op"": ""click"" },
            { ""op"": ""shot"", ""name"": ""../x"" },
            { ""op"": ""theme"", ""value"": ""blue"" },
            3
        ]");
        Assert.Equal(6, s.Steps.Count);           // 坏步骤照样留着:运行时记 FAIL
        Assert.Equal(6, s.Problems.Count);
        Assert.Contains(s.Problems, p => p.Contains("fly"));
        Assert.Contains(s.Problems, p => p.Contains("navigate"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("\"just a string\"")]
    [InlineData("{ \"width\": 10 }")]
    public void Parse_Throws_OnStructurallyBrokenScript(string json)
    {
        Assert.Throws<FormatException>(() => TourScriptParser.Parse(json));
    }

    [Theory]
    [InlineData("Ctrl+Z", "Z", true, false)]
    [InlineData("ctrl+shift+y", "Y", true, true)]
    [InlineData("Esc", "Escape", false, false)]
    [InlineData("Enter", "Enter", false, false)]
    [InlineData("5", "D5", false, false)]
    [InlineData("F5", "F5", false, false)]
    [InlineData("Shift+Tab", "Tab", false, true)]
    public void ParseKeys_NormalizesToAvaloniaKeyNames(string keys, string main, bool ctrl, bool shift)
    {
        var k = TourScriptParser.ParseKeys(keys);
        Assert.Equal(main, k.Key);
        Assert.Equal(ctrl, k.Ctrl);
        Assert.Equal(shift, k.Shift);
        Assert.True(Enum.TryParse<Avalonia.Input.Key>(k.Key, out _), "not an Avalonia Key: " + k.Key);
    }
}

[Collection("TipCL 真机交互回归")]
public class VisualTourFinderTests
{
    private static void Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Win, Button Named, Button Texted, Button IconOnly, TextBlock Label) Build()
    {
        var named = new Button { Name = "BtnNamed", Content = "甲" };
        var texted = new Button { Content = "编辑组件" };
        var icon = new Button { Content = new Border { Width = 16, Height = 16 } };
        ToolTip.SetTip(icon, "启动配置");
        var label = new TextBlock { Text = "  版本\n列表 " };
        var hidden = new Button { Content = "编辑组件", IsVisible = false };
        var second = new Button { Content = "编辑组件" };
        var panel = new StackPanel { Children = { named, texted, icon, label, hidden, second } };
        var win = new Window { Width = 400, Height = 400, Content = panel };
        win.Show();
        Pump();
        Pump();
        return (win, named, texted, icon, label);
    }

    [Fact]
    public void Find_ByName_ByText_ByTip_ByTypeAndText() => TestApp.Run(() =>
    {
        var (win, named, texted, icon, label) = Build();
        try
        {
            var layers = new List<Visual> { win };
            Assert.Same(named, TourFinder.Find(layers, new TourSelector { Name = "BtnNamed" }));
            // 按文字:命中按钮本身;按钮模板里同文字的 TextBlock 不重复计数;隐藏的那个不算
            List<Control> all = TourFinder.FindAll(layers, new TourSelector { Text = "编辑组件" });
            Assert.Equal(2, all.Count);
            Assert.Same(texted, all[0]);
            Assert.NotSame(texted, TourFinder.Find(layers, new TourSelector { Text = "编辑组件", Index = 1 }));
            Assert.Same(icon, TourFinder.Find(layers, new TourSelector { Tip = "启动配置" }));
            // 空白归一:多余空格 / 换行不影响匹配
            Assert.Same(label, TourFinder.Find(layers, new TourSelector { Text = "版本 列表" }));
            Assert.Same(label, TourFinder.Find(layers, new TourSelector { Contains = "列表" }));
            // 类型 + 文字:「文字为 X 的按钮」
            Assert.Same(named, TourFinder.Find(layers, new TourSelector { Type = "Button", Text = "甲" }));
            // 中心点落在按钮上(命中测试点中目标或其子孙)
            Point? p = TourFinder.CenterInTop(texted);
            Assert.NotNull(p);
            Assert.True(TourFinder.HitCheck(win, texted, p!.Value).Ok);
        }
        finally
        {
            win.Close();
        }
    });

    [Fact]
    public void Find_ReturnsNull_WhenNothingMatches() => TestApp.Run(() =>
    {
        var (win, _, _, _, _) = Build();
        try
        {
            var layers = new List<Visual> { win };
            Assert.Null(TourFinder.Find(layers, new TourSelector { Text = "根本不存在的文字" }));
            Assert.Null(TourFinder.Find(layers, new TourSelector { Name = "Nope" }));
            Assert.Null(TourFinder.Find(layers, new TourSelector { Text = "编辑组件", Index = 5 }));
            Assert.Empty(TourFinder.FindAll(layers, new TourSelector()));   // 空选择器什么都不命中
        }
        finally
        {
            win.Close();
        }
    });

    [Fact]
    public void Truncated_ReportsTextNarrowerThanItsNaturalWidth() => TestApp.Run(() =>
    {
        var narrow = new TextBlock { Text = "这是一句明显写不下的很长很长的说明文字", Width = 40, FontSize = 13 };
        var wide = new TextBlock { Text = "短", FontSize = 13 };
        var win = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { narrow, wide } } };
        win.Show();
        Pump();
        Pump();
        try
        {
            List<string> hits = TourFinder.Truncated(win);
            Assert.Single(hits);
            Assert.Contains("写不下", hits[0]);
        }
        finally
        {
            win.Close();
        }
    });

    [Fact]
    public void Runner_MissingTarget_IsFail_AndTourContinues() => TestApp.Run(() =>
    {
        string outDir = Path.Combine(Path.GetTempPath(), "tipcl-visualtour-test-" + Guid.NewGuid().ToString("N"));
        string scriptPath = Path.Combine(outDir, "t.json");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(scriptPath, "{}");
        try
        {
            TourScript script = TourScriptParser.Parse(@"{ ""width"": 900, ""height"": 550, ""settleMs"": 30, ""steps"": [
                { ""op"": ""click"", ""text"": ""绝对不存在的按钮 xyz"" },
                { ""op"": ""navigate"", ""page"": ""settings"" },
                { ""op"": ""expect"", ""text"": ""也不存在 qq"", ""absent"": true },
                { ""op"": ""fly"" },
                { ""op"": ""key"", ""keys"": ""Ctrl+Nope"" },
                { ""op"": ""shot"", ""name"": ""t1"", ""layer"": ""main"" }
            ] }");
            var runner = new TourRunner(script, scriptPath, outDir) { WireApp = false };
            runner.Execute();

            IReadOnlyList<TourStepResult> r = runner.Results;
            Assert.Equal(6, r.Count);   // 第 0 步 FAIL 之后照样跑完
            Assert.Equal("FAIL", r[0].Status);
            Assert.Contains("not found", r[0].Message);
            Assert.Equal("OK", r[1].Status);
            Assert.Equal("OK", r[2].Status);
            Assert.Equal("FAIL", r[3].Status);
            Assert.Equal("FAIL", r[4].Status);
            Assert.Equal("OK", r[5].Status);
            Assert.True(File.Exists(Path.Combine(outDir, "t1.png")));
            Assert.Equal(VisualTour.ExitFailures, runner.ExitCode);

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "tour-result.json")));
            Assert.Equal(3, doc.RootElement.GetProperty("summary").GetProperty("fail").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("shots").GetInt32());
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch (Exception) { }
        }
    });

    [Fact]
    public void Runner_RefusesToClickLaunchButtons()
    {
        Assert.Contains("启动游戏", VisualTour.LaunchWords);
        Assert.Contains("启动", VisualTour.LaunchWords);
    }
}

public class VisualFixtureTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "tipcl-fixture-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Build_CreatesInstancesVersionsModsSavesServersAccountsPlaytimeAndCaches()
    {
        string home = NewDir();
        try
        {
            VisualFixture.Summary s = VisualFixture.Build(home, new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.FromHours(8)));
            Assert.Equal(4, s.Instances);
            Assert.True(s.Versions >= 7);
            Assert.True(s.Mods >= 60);
            Assert.True(s.Saves >= 6);
            Assert.True(s.Servers >= 3);
            Assert.True(s.Sessions > 0);

            string mc = Path.Combine(home, ".minecraft");
            Assert.True(File.Exists(Path.Combine(mc, ".instance.json")));   // 根实例也有元数据 ⇒ 单根模式,四个实例都列得出来
            foreach (string name in VisualFixture.InstanceNames.Skip(1))
                Assert.True(File.Exists(Path.Combine(mc, name, ".instance.json")), name);

            // 模组 jar 是合法 zip,里面有 fabric.mod.json
            string fabricMods = Path.Combine(mc, "versions", "fabric-loader-0.15.11-1.20.1", "mods");
            string jar = Directory.GetFiles(fabricMods, "*.jar").First();
            using (ZipArchive z = ZipFile.OpenRead(jar))
            {
                using var reader = new StreamReader(z.GetEntry("fabric.mod.json")!.Open());
                using JsonDocument mod = JsonDocument.Parse(reader.ReadToEnd());
                Assert.Equal(1, mod.RootElement.GetProperty("schemaVersion").GetInt32());
            }
            Assert.Equal(32, Directory.GetFiles(fabricMods).Length);

            // level.dat 用 Core 自己的读取器读得出世界名
            string save = Path.Combine(mc, "versions", "fabric-loader-0.15.11-1.20.1", "saves", "新的世界", "level.dat");
            Assert.Equal("新的世界", TipCL.Core.Launcher.Sync.LevelDat.ReadFile(save).LevelName);

            // servers.dat 用 Core 自己的读取器读得出服务器
            List<object> servers = Terracotta.ReadGameServers(Path.Combine(mc, "servers.dat"));
            Assert.Contains(servers, row => TipCL.Core.Py.S(((TipCL.Core.PyDict)row)["name"]) == "生存服");

            using (JsonDocument acc = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "accounts.json"))))
                Assert.Equal("Steve", acc.RootElement.GetProperty("active").GetString());
            using (JsonDocument pt = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "playtime.json"))))
                Assert.True(pt.RootElement.GetProperty("instances").GetProperty(".minecraft").GetProperty("total").GetInt64() > 0);
            using (JsonDocument man = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "cache", "version_manifest.json"))))
                Assert.Equal("26.3", man.RootElement.GetProperty("latest").GetProperty("release").GetString());
            Assert.True(File.Exists(Path.Combine(home, "cache", "news.json")));
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public void Build_RefusesNonEmptyDirWithoutMarker_AndRebuildsWithMarker()
    {
        string home = NewDir();
        try
        {
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "precious.txt"), "user data");
            Assert.Throws<InvalidOperationException>(() => VisualFixture.Build(home, DateTimeOffset.Now));
            Assert.True(File.Exists(Path.Combine(home, "precious.txt")));   // 什么都没动

            File.Delete(Path.Combine(home, "precious.txt"));
            VisualFixture.Build(home, DateTimeOffset.Now);
            File.WriteAllText(Path.Combine(home, ".minecraft", "stale.txt"), "x");
            VisualFixture.Build(home, DateTimeOffset.Now);                    // 有标记:清空重建
            Assert.False(File.Exists(Path.Combine(home, ".minecraft", "stale.txt")));
            Assert.True(File.Exists(Path.Combine(home, VisualFixture.Marker)));
        }
        finally
        {
            try { Directory.Delete(home, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public void Build_RefusesTheRealHome()
    {
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipCL");
        Assert.Throws<InvalidOperationException>(() => VisualFixture.Build(real, DateTimeOffset.Now));
    }
}

public class VisualTourNetTests
{
    [Fact]
    public void ModrinthSearch_IsFaked_PerProjectType()
    {
        var uri = new Uri("https://api.modrinth.com/v2/search?query=&facets=%5B%5B%22project_type%3Ashader%22%5D%5D&limit=20");
        Assert.True(TourNet.IsModrinthSearch(uri));
        Assert.Equal("shader", TourNet.ProjectType(uri));
        Assert.True(TourNet.IsModrinthSearch(new Uri("https://mod.mcimirror.top/modrinth/v2/search?query=a")));
        Assert.False(TourNet.IsModrinthSearch(new Uri("https://api.curseforge.com/v1/mods/search")));
        Assert.False(TourNet.IsModrinthSearch(new Uri("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")));

        using JsonDocument doc = JsonDocument.Parse(TourNet.FakeSearchJson("shader"));
        JsonElement hits = doc.RootElement.GetProperty("hits");
        Assert.True(hits.GetArrayLength() >= 3);
        Assert.Equal("shader", hits[0].GetProperty("project_type").GetString());
        Assert.False(string.IsNullOrEmpty(hits[0].GetProperty("title").GetString()));
    }
}

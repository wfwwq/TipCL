// AI3-i18n:上一轮剩下的 i18n 残留(界面侧)。
//   1. 启动页预检提示条走真的 AiPreflight(条目来自老 Preflight / Conflict 代码):en 下不含 CJK,切回中文后文字照旧;
//   2. AI 页步骤行(准备: / 完成: 前缀剥掉后的标签)、步骤组标题、权限标签:en 下不含 CJK,中文下与改动前一致;
//   3. 版本设置对话框的隔离 / GC / 绑定账号下拉、存档对话框的媒体下拉:文案每次现查词表 ——
//      先在中文下取一次(类型已初始化)再切 en,拿到的就是英文;切回中文一字不差;媒体下拉按下标映射目录。
// 全部在无头会话里跑(TestApp);切语言写在测试体里面(会话每次派发会按配置重设语言)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TipCL.App.Pages.Ai;
using TipCL.App.Pages.Launch;
using TipCL.App.Pages.Version;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using TipCL.Tests.Core;
using Xunit;

namespace TipCL.Tests.App;

[Collection("TipCL 真机交互回归")]
public class AiI18nResidueAppTests
{
    [Fact]
    public void LaunchPreflightBar_WithItemsFromTheOldPreflight_InEnglish_HasNoCjk()
    {
        using var t = new NfTempInstance("i18n3bar");
        t.Version("fabric-loader-0.15.11-1.20.5", javaMajor: 21);
        AiI18nResidueCoreTests.BadMods(t.PathOf("mods"));
        AiI18nResidueCoreTests.BadMods(Utils.AbsPath(VersionSettings.ModsDir(t.Inst, "fabric-loader-0.15.11-1.20.5")));
        string noJava = t.PathOf("nojava", "java.exe");
        object? saved = Config.CONFIG.Get(AiPreflight.ENABLED_KEY);
        // 真跑 AiPreflight(不伪造报告):提示条前几条正是老代码出的 java_missing / mod_duplicate / mod_breaks ……
        LaunchPreflightHook.Override = (_, _, _) => AiPreflight.Check(t.Inst, "fabric-loader-0.15.11-1.20.5", 4096, noJava, PyDict.Of("ram_mb", 65536L));
        Config.CONFIG.Set(AiPreflight.ENABLED_KEY, true);
        try
        {
            TestApp.RunAsync(() => I18nUxCjk.InLangAsync("en", async () =>
            {
                var page = new LaunchPage();
                Assert.False(await page.AiPreflightGateAsync(t.Name, "fabric-loader-0.15.11-1.20.5", 4096));
                List<string> texts = I18nUxUiTexts.Of(page.PreflightBar!);
                Assert.Contains(texts, x => x.Contains("Mods were extracted into folders")); // 老 Preflight 的 mod_unzipped
                I18nUxCjk.AssertNone(texts.Select(x => x.Replace(noJava, "")), "启动页预检提示条(老 Preflight / Conflict 条目)");
            }));
            TestApp.RunAsync(() => I18nUxCjk.InLangAsync("zh_CN", async () =>
            {
                var page = new LaunchPage();
                Assert.False(await page.AiPreflightGateAsync(t.Name, "fabric-loader-0.15.11-1.20.5", 4096));
                List<string> texts = I18nUxUiTexts.Of(page.PreflightBar!);
                Assert.Contains(texts, x => x.Contains("Mods 被解压成了文件夹"));
                Assert.Contains(texts, x => x.Contains("客户端 jar 未直接找到"));
            }));
        }
        finally
        {
            LaunchPreflightHook.Override = null;
            if (saved == null) Config.CONFIG.data.Remove(AiPreflight.ENABLED_KEY);
            else Config.CONFIG.Set(AiPreflight.ENABLED_KEY, saved);
            AiPage.QueuePrompt("");
        }
    }

    private static readonly MethodInfo OnEvent = typeof(AiPage).GetMethod("OnAgentEvent", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>给 AI 页喂一组「开始 → 完成」的工具事件,返回步骤组(标题 + 每行标签 / 参数 / 悬停提示)的可见文字。</summary>
    private static (List<string> Texts, ToolLine Line, StepGroup Group) FeedSteps(AiPage page, ToolCall call)
    {
        string label = Tools.Label(call.name, call.args);
        OnEvent.Invoke(page, new object[] { new AiAgentEvent { Kind = "tool", Name = call.name, Text = label, Call = call } });
        StepGroup running = page.GetLogicalDescendants().OfType<StepGroup>().Last();
        running.Refresh();
        var texts = I18nUxUiTexts.Of(running); // 运行中:标题「正在 …」+ 步数
        texts.Add(running.TitleText);
        call.status = ToolCallStatus.COMPLETED;
        call.result = "{\"items\":[1,2,3]}";
        OnEvent.Invoke(page, new object[] { new AiAgentEvent { Kind = "tool_done", Name = call.name, Text = label, Call = call } });
        StepGroup group = page.GetLogicalDescendants().OfType<StepGroup>().Last();
        ToolLine line = group.Lines.Last();
        texts.AddRange(I18nUxUiTexts.Of(group));
        texts.Add(group.TitleText);
        return (texts, line, group);
    }

    [Fact]
    public void AiPageStepLines_InEnglish_HaveNoCjk_AndChineseIsUnchanged()
    {
        PyDict Args() => PyDict.Of("name", "sodium", "instance", "demo");
        TestApp.Run(() => I18nUxCjk.InLang("en", () =>
        {
            var page = new AiPage();
            var call = new ToolCall("s1", "install_mod", Args());
            (List<string> texts, ToolLine line, _) = FeedSteps(page, call);
            string label = Tools.Label("install_mod", call.args);
            Assert.Equal(label, line.LabelText);          // 「Done:」前缀被剥掉,标签就是工具标签本身
            Assert.Contains(texts, x => x == "3 results");  // 步骤行右侧的结果摘要
            I18nUxCjk.AssertNone(texts, "AI 页步骤组 / 步骤行");
            // 头部权限标签
            Assert.Contains(I18nUxUiTexts.Of(page), x => x.StartsWith("Permission: ", StringComparison.Ordinal) && !I18nUxCjk.Has(x));
        }));
        TestApp.Run(() => I18nUxCjk.InLang("zh_CN", () =>
        {
            var page = new AiPage();
            var call = new ToolCall("s2", "install_mod", Args());
            (List<string> texts, ToolLine line, StepGroup group) = FeedSteps(page, call);
            Assert.Equal("安装模组 sodium → demo", line.LabelText);
            Assert.Contains(texts, x => x == "3 条结果");
            // 标题是「正在…」或「执行了 N 步」(取决于本轮是否已收尾),两种都是改动前的中文原文
            Assert.True(group.TitleText.StartsWith("正在", StringComparison.Ordinal) || group.TitleText.StartsWith("执行了 ", StringComparison.Ordinal), group.TitleText);
            Assert.DoesNotContain(texts, x => x.Contains("Preparing") || x.Contains("Done:"));
            Assert.Contains(I18nUxUiTexts.Of(page), x => x.StartsWith("权限:", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public void AiFixView_StepPrefix_IsStrippedInEnglish()
    {
        // AiFixView(启动失败 / 预检的「交给 AI 修复」面板)与 AI 页同一口径:前缀走词表,StripState 认得
        TestApp.Run(() => I18nUxCjk.InLang("en", () =>
        {
            string label = Tools.Label("install_mod", PyDict.Of("name", "sodium", "instance", "demo"));
            var line = new ToolLine(I18nCore.Tr("准备:") + label, "install_mod");
            Assert.Equal(label, line.LabelText);
            I18nUxCjk.AssertNone(I18nUxUiTexts.Of(line), "工具行");
        }));
    }

    [Fact]
    public void DialogDropdowns_FollowTheLanguageWithoutRestart() => TestApp.Run(() =>
    {
        string[] isoZh = Array.Empty<string>(), gcZh = Array.Empty<string>(), mediaZh = Array.Empty<string>();
        string accZh = "";
        I18nUxCjk.InLang("zh_CN", () =>
        {
            // 先在中文下取一次:以前是静态只读数组,这一步之后就定死了
            isoZh = VersionSetupDialog.IsoChoices.Labels;
            gcZh = VersionSetupDialog.GcChoices.Labels;
            mediaZh = SavesDialog.MediaLabels;
            accZh = VersionSetupDialog.NoAccountLabel;
        });
        Assert.Equal(new[] { "跟随全局", "关闭(共用实例目录)", "隔离存档", "隔离全部" }, isoZh);
        Assert.Equal(new[] { "跟随实例 / 全局", "自动", "G1(通用)", "ZGC(大内存)", "Serial(老电脑)", "不指定" }, gcZh);
        Assert.Equal(new[] { "截图", "崩溃报告", "日志" }, mediaZh);
        Assert.Equal("(不绑定)", accZh);

        I18nUxCjk.InLang("en", () =>
        {
            var texts = new List<string>();
            texts.AddRange(VersionSetupDialog.IsoChoices.Labels);
            texts.AddRange(VersionSetupDialog.GcChoices.Labels);
            texts.AddRange(SavesDialog.MediaLabels);
            texts.Add(VersionSetupDialog.NoAccountLabel);
            I18nUxCjk.AssertNone(texts, "版本设置 / 存档对话框的下拉");
            Assert.Equal(new[] { "Screenshots", "Crash Reports", "Logs" }, SavesDialog.MediaLabels);
            // 取值与文案的条数一致(保存按下标取值)
            Assert.Equal(VersionSetupDialog.IsoChoices.Values.Length, VersionSetupDialog.IsoChoices.Labels.Length);
            Assert.Equal(VersionSetupDialog.GcChoices.Values.Length, VersionSetupDialog.GcChoices.Labels.Length);
        });

        I18nUxCjk.InLang("zh_CN", () =>
        {
            Assert.Equal(isoZh, VersionSetupDialog.IsoChoices.Labels);
            Assert.Equal(gcZh, VersionSetupDialog.GcChoices.Labels);
            Assert.Equal(mediaZh, SavesDialog.MediaLabels);
        });

        // 媒体下拉按下标映射目录,与文案语言无关(以前拿选中文字和 Tr(...) 比,下拉定在中文、界面切 en 时永远落到截图)
        Assert.Equal("screenshots", SavesDialog.MediaKindAt(0));
        Assert.Equal("crash-reports", SavesDialog.MediaKindAt(1));
        Assert.Equal("logs", SavesDialog.MediaKindAt(2));
        Assert.Equal("screenshots", SavesDialog.MediaKindAt(-1));
        Assert.Equal(SavesDialog.MediaLabels.Length, SavesDialog.MediaKinds.Length);
    });
}

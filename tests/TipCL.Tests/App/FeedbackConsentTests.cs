using System;
using System.IO;
using TipCL.App.Common;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 反馈同意门回归(照 Windows 版 FeedbackConsent.SelfTest 的三张用例表搬成 xunit):
/// 解析(键存在与否 / 非布尔值 / 坏 JSON)+ 决策(文件优先于后端)+ 状态迁移(问过一次就不再问)。
/// </summary>
public class FeedbackConsentTests
{
    [Fact]
    public void ParseRaw_MatchesWindowsCaseTable()
    {
        Assert.Equal(new FeedbackConsent.Raw(true, null), FeedbackConsent.ParseRaw("{}"));
        Assert.Equal(new FeedbackConsent.Raw(true, null), FeedbackConsent.ParseRaw("{\"feedback_consent\": null}"));
        Assert.Equal(new FeedbackConsent.Raw(true, true), FeedbackConsent.ParseRaw("{\"feedback_consent\": true, \"first_run\": false}"));
        Assert.Equal(new FeedbackConsent.Raw(true, false), FeedbackConsent.ParseRaw("{\"feedback_consent\": false}"));
        // 非布尔值 = 「问过了」(python: is not None) → 当作拒绝
        Assert.Equal(new FeedbackConsent.Raw(true, false), FeedbackConsent.ParseRaw("{\"feedback_consent\": \"yes\"}"));
        // 坏 JSON / 不是对象 → 不可读
        Assert.Equal(new FeedbackConsent.Raw(false, null), FeedbackConsent.ParseRaw("{not json"));
        Assert.Equal(new FeedbackConsent.Raw(false, null), FeedbackConsent.ParseRaw("[1,2]"));
    }

    [Fact]
    public void ShouldAsk_FileWinsOverBridge()
    {
        Assert.True(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(true, null), false));
        // 文件可读且键为 null 时,即便后端说同意过也要问(文件优先)
        Assert.True(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(true, null), true));
        Assert.False(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(true, true), true));
        Assert.False(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(true, false), false));
        // 文件读不出来才信后端
        Assert.False(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(false, null), true));
        Assert.True(FeedbackConsent.ShouldAsk(new FeedbackConsent.Raw(false, null), false));
    }

    [Fact]
    public void ShouldAsk_SecondLaunchDoesNotAsk()
    {
        foreach (bool choice in new[] { true, false })
        {
            string json = "{\"feedback_consent\": " + (choice ? "true" : "false") + "}";
            Assert.False(FeedbackConsent.ShouldAsk(FeedbackConsent.ParseRaw(json), choice));
        }
    }

    [Fact]
    public void ReadRaw_HandlesMissingAndBrokenFiles()
    {
        Assert.Equal(new FeedbackConsent.Raw(false, null), FeedbackConsent.ReadRaw(null));
        Assert.Equal(new FeedbackConsent.Raw(false, null), FeedbackConsent.ReadRaw(""));

        string dir = Path.Combine(Path.GetTempPath(), "tipcl-c-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            string missing = Path.Combine(dir, "config.json");
            Assert.Equal(new FeedbackConsent.Raw(true, null), FeedbackConsent.ReadRaw(missing));

            File.WriteAllText(missing, "{\"feedback_consent\": true}");
            Assert.Equal(new FeedbackConsent.Raw(true, true), FeedbackConsent.ReadRaw(missing));

            File.WriteAllText(missing, "{ broken");
            Assert.Equal(new FeedbackConsent.Raw(false, null), FeedbackConsent.ReadRaw(missing));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}

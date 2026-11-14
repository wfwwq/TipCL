// 来源: Windows 版参考仓库 src/TipCL/Pages/FeedbackConsent.cs(M4-H P4 移植:保留全部纯逻辑与
// MaybeAskAsync;Windows 里那份给命令行自检用的 SelfTest 是 Windows 侧 CLI 的一部分,Linux 侧改为
// xunit 用例覆盖同一批用例表,见 tests/TipCL.Tests/App/FeedbackConsentTests.cs)。
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Common;

/// <summary>
/// 首次同意门:只有 config.json 里**没有** feedback_consent 这个键时才问一次;
/// 已经问过(无论同意还是拒绝)都不再打扰。config 读不出来时退化成「信后端的值」。
/// </summary>
public static class FeedbackConsent
{
    public readonly record struct Raw(bool Readable, bool? Value);

    public const int DelayMs = 400;

    public const string Key = "feedback_consent";

    /// <summary>读原始值:Readable=false 表示文件读不了/不是 JSON 对象;Value=null 表示键不存在。</summary>
    public static Raw ReadRaw(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath)) return new Raw(false, null);
        try
        {
            if (!File.Exists(configPath)) return new Raw(true, null);
            return ParseRaw(File.ReadAllText(configPath));
        }
        catch (Exception)
        {
            return new Raw(false, null);
        }
    }

    public static Raw ParseRaw(string json)
    {
        try
        {
            object parsed = PyJson.Loads(json);
            if (parsed is not PyDict root) return new Raw(false, null);
            if (!root.ContainsKey(Key)) return new Raw(true, null);
            object v = root.Get(Key);
            if (v == null) return new Raw(true, null);
            return v is bool b ? new Raw(true, b) : new Raw(true, false);
        }
        catch (Exception)
        {
            return new Raw(false, null);
        }
    }

    /// <summary>要不要问一次:文件可读时只看「键是否存在」(文件优先于后端),不可读时信后端。</summary>
    public static bool ShouldAsk(Raw raw, bool bridgeConsent)
        => raw.Readable ? raw.Value is null : !bridgeConsent;

    /// <summary>首启询问(照 Windows MaybeAskAsync):问完把选择写进配置。</summary>
    public static async Task MaybeAskAsync(Window? owner = null)
    {
        var api = InProcessApi.Instance;
        bool bridgeConsent = Py.Truthy(Config.CONFIG.Get(Key, (object)false));
        if (!ShouldAsk(ReadRaw(Config.CONFIG_FILE), bridgeConsent)) return;
        await Task.Delay(DelayMs);
        bool agree = await Dlg.Confirm("是否上传诊断数据",
            "第一次打开需要你亲自选择。\n\n同意后才会向开发者上传:\n· 你提交的反馈内容\n· 本机配置(CPU / 内存 / 显卡 / Java / 已装版本)\n\n暂不同意则不会上传,以后可在设置里更改。",
            "同意", "暂不同意", owner: owner);
        try
        {
            await api.CallAsync<object>("save_settings", new { data = new { feedback_consent = agree } });
        }
        catch (Exception ex)
        {
            AppLog.Warn("同意门没能保存: " + ex.Message);
        }
    }
}

using System;

namespace TipCL.Smoke;

/// <summary>
/// <c>--smoke [--page &lt;id&gt;] [--theme light|dark] [--out x.png] [--ai-seed]</c> 的解析结果。
/// </summary>
internal sealed class SmokeOptions
{
    public const string Usage =
        "用法: TipCL --smoke [--page <id>] [--theme light|dark] [--out <file.png>] [--ai-seed]";

    public string Page { get; private set; } = string.Empty;

    public string Theme { get; private set; } = "light";

    public string Out { get; private set; } = "smoke.png";

    /// <summary>
    /// M7:<c>--page ai</c> 时用 AI 页自己的自测缝灌一份**确定性的对话**再截屏(见 <see cref="SmokeRunner"/>)。
    /// 默认关:**不带这个开关时,AI 页的帧与其他页一样只反映"外壳切过去 + 页面构造函数",不多喂任何数据**。
    /// 为什么要它:离屏不连后端(<c>BridgeReady=false</c>),而外壳只在后端就绪后才跑页面的
    /// <c>EnsureLoadedAsync</c>/<c>OnShown</c>(D-92 的口径,`MainWindow.Navigate` 传的就是 <c>BridgeReady</c>),
    /// 所以不进这个开关,AI 页永远只有空壳 —— 消息区、卡片、状态行都拿不到帧证据。
    /// </summary>
    public bool AiSeed { get; private set; }

    public bool Dark => string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 只有出现 <c>--smoke</c> 时才解析;缺值、未知开关、非法主题都算错误。
    /// </summary>
    public static bool TryParse(string[] args, out SmokeOptions options, out string error)
    {
        options = new SmokeOptions();
        error = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, "--page", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryTakeValue(args, ref i, "--page", out string page, out error))
                {
                    return false;
                }

                options.Page = page;
                continue;
            }

            if (string.Equals(arg, "--theme", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryTakeValue(args, ref i, "--theme", out string theme, out error))
                {
                    return false;
                }

                if (!string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"--theme 只接受 light 或 dark,收到 '{theme}'";
                    return false;
                }

                options.Theme = theme.ToLowerInvariant();
                continue;
            }

            if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryTakeValue(args, ref i, "--out", out string path, out error))
                {
                    return false;
                }

                options.Out = path;
                continue;
            }

            if (string.Equals(arg, "--ai-seed", StringComparison.OrdinalIgnoreCase))
            {
                options.AiSeed = true;
                continue;
            }

            error = $"未知参数 '{arg}'";
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(string[] args, ref int i, string name, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (i + 1 >= args.Length)
        {
            error = $"{name} 缺少取值";
            return false;
        }

        value = args[++i];
        if (value.StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{name} 缺少取值(后面直接跟了 '{value}')";
            return false;
        }

        return true;
    }
}

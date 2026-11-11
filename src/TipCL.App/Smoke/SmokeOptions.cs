using System;

namespace TipCL.Smoke;

/// <summary>
/// <c>--smoke [--page &lt;id&gt;] [--theme light|dark] [--out x.png]</c> 的解析结果。
/// </summary>
internal sealed class SmokeOptions
{
    public const string Usage =
        "用法: TipCL --smoke [--page <id>] [--theme light|dark] [--out <file.png>]";

    public string Page { get; private set; } = string.Empty;

    public string Theme { get; private set; } = "light";

    public string Out { get; private set; } = "smoke.png";

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

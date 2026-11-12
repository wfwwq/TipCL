using System.Text;

namespace TipCL.Packager;

/// <summary>
/// `Contents/MacOS/TipCL` 启动脚本的**唯一一份**文本定义(纯常量/纯函数)。
///
/// 文本逐字来自任务书 B3「Bundle layout」小节;行尾是 LF、无 BOM、结尾一个换行。
/// `uname -m` 在 Apple 芯片上是 `arm64`,在 Intel Mac 上是 `x86_64`(只按脚本里写的分支处理:
/// 等于 `arm64` 走 arm64 目录,其余一律走 x64 目录)。
/// </summary>
internal static class LauncherScript
{
    /// <summary>脚本正文(不含结尾换行),逐行列出以便测试逐行核对。</summary>
    public static readonly string[] Lines =
    {
        "#!/bin/sh",
        "DIR=\"$(cd \"$(dirname \"$0\")\" && pwd)\"",
        "case \"$(uname -m)\" in",
        "  arm64) exec \"$DIR/arm64/TipCL\" \"$@\" ;;",
        "  *) exec \"$DIR/x64/TipCL\" \"$@\" ;;",
        "esac",
    };

    /// <summary>正文:每行以 `\n` 结尾(含最后一行),没有 `\r`。</summary>
    public static string Text => string.Join("\n", Lines) + "\n";

    /// <summary>UTF-8 字节,不带 BOM。</summary>
    public static byte[] Bytes()
    {
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Text);
    }
}

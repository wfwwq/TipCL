using System;
using System.Collections.Generic;

namespace TipCL.Packager;

/// <summary>
/// `.app` 目录布局与权限位的**唯一一份定义**(纯常量 + 纯函数,不碰磁盘)。
///
/// 布局来源:任务书 B3「Bundle layout」小节 + GOAL.md 第 3 节的「架构」行(arm64 + x64 合成一个通用包,
/// `Contents/MacOS/TipCL` 是 shell 启动脚本,按 `uname -m` exec 对应架构的 apphost)。
/// tar 里的路径一律用 `/`,顶层条目是 `TipCL.app/`。
/// </summary>
internal static class BundleLayout
{
    public const string AppName = "TipCL.app";

    /// <summary>顶层目录条目(带结尾 `/`,tar 的目录条目形态)。</summary>
    public const string RootEntry = "TipCL.app/";

    public const string ContentsDir = "TipCL.app/Contents";

    public const string InfoPlistEntry = "TipCL.app/Contents/Info.plist";

    public const string MacOsDir = "TipCL.app/Contents/MacOS";

    /// <summary>CFBundleExecutable 指向的 shell 启动脚本。</summary>
    public const string LauncherEntry = "TipCL.app/Contents/MacOS/TipCL";

    public const string Arm64Dir = "TipCL.app/Contents/MacOS/arm64";

    public const string X64Dir = "TipCL.app/Contents/MacOS/x64";

    /// <summary>osx-arm64 publish 输出里的 apphost。</summary>
    public const string Arm64HostEntry = "TipCL.app/Contents/MacOS/arm64/TipCL";

    /// <summary>osx-x64 publish 输出里的 apphost。</summary>
    public const string X64HostEntry = "TipCL.app/Contents/MacOS/x64/TipCL";

    public const string ResourcesDir = "TipCL.app/Contents/Resources";

    public const string IcnsEntry = "TipCL.app/Contents/Resources/TipCL.icns";

    /// <summary>Info.plist 里的 CFBundleIconFile(AppInfo 的图标文件,不带扩展名)。</summary>
    public const string IconFileName = "TipCL";

    /// <summary>Info.plist 里的 CFBundleExecutable。</summary>
    public const string ExecutableName = "TipCL";

    // C# 没有八进制字面量(0o755 不是合法写法),所以用十进制 + 注释里的八进制写法。
    // 目录 0755
    public const int ModeDirectory = 493;

    // 普通文件 0644
    public const int ModeFile = 420;

    // 可执行文件 0755
    public const int ModeExecutable = 493;

    /// <summary>两个架构子目录的名字,顺序固定(arm64 先),供目录条目列表使用。</summary>
    public static readonly string[] ArchNames = { "arm64", "x64" };

    /// <summary>必须出现在 tar 里的目录条目(tar 里目录条目一律以 `/` 结尾)。</summary>
    public static IReadOnlyList<string> RequiredDirectoryEntries { get; } = new[]
    {
        RootEntry,
        DirEntry(ContentsDir),
        DirEntry(MacOsDir),
        DirEntry(ResourcesDir),
        DirEntry(Arm64Dir),
        DirEntry(X64Dir),
    };

    /// <summary>必须存在的文件条目(相对 bundle 根,用 `/` 分隔)。</summary>
    public static IReadOnlyList<string> RequiredFileEntries { get; } = new[]
    {
        InfoPlistEntry,
        LauncherEntry,
        Arm64HostEntry,
        X64HostEntry,
        IcnsEntry,
        ArchDirEntry("arm64", "TipCL.dll"),
        ArchDirEntry("arm64", "TipCL.Core.dll"),
        ArchDirEntry("x64", "TipCL.dll"),
        ArchDirEntry("x64", "TipCL.Core.dll"),
    };

    /// <summary>把一条路径写成 tar 的目录条目名(补齐结尾的 `/`)。</summary>
    public static string DirEntry(string path)
    {
        return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }

    public static string ArchDirEntry(string arch, string fileName)
    {
        return MacOsDir + "/" + arch + "/" + NormalizeEntryName(fileName);
    }

    /// <summary>把一个相对名字(可能带 `\`)统一成 tar 里用的 `/` 分隔形态。</summary>
    public static string NormalizeEntryName(string relativeName)
    {
        return relativeName.Replace('\\', '/');
    }

    /// <summary>
    /// 一个**文件**条目该用什么权限位。判据(任务书 B3「Tar entry modes」小节):
    /// 1. 三个写死的路径(`Contents/MacOS/TipCL` 与两个架构目录下的 apphost)→ 0755;
    /// 2. 后缀是 `.dylib` → 0755;
    /// 3. 文件头是 Mach-O 且 `filetype == 2`(可执行)→ 0755(例如 `createdump` 这类工具);
    /// 4. 其余 → 0644。
    /// 只看相对路径与文件头的头几个字节,不碰磁盘。
    /// </summary>
    public static int ModeForFile(string entryName, byte[]? headBytes)
    {
        string name = NormalizeEntryName(entryName);

        if (name == LauncherEntry || name == Arm64HostEntry || name == X64HostEntry)
        {
            return ModeExecutable;
        }

        if (name.EndsWith(".dylib", StringComparison.Ordinal))
        {
            return ModeExecutable;
        }

        if (MachO.IsExecutable(headBytes))
        {
            return ModeExecutable;
        }

        return ModeFile;
    }

    /// <summary>目录一律 0755。</summary>
    public static int ModeForDirectory()
    {
        return ModeDirectory;
    }

    /// <summary>校验用的「组/其它可写位」掩码 0022(校验里要求 `(mode &amp; GroupOtherWriteMask) == 0`)。</summary>
    public const int GroupOtherWriteMask = 18;
}

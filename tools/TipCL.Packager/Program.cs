using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TipCL.Packager;

/// <summary>
/// B3 的打包工具。**这里只做文件系统的事**(建暂存目录、复制 publish 输出、写归档、读回归档),
/// 全部判据都在别的文件里的 `internal static` 纯函数里:
///
/// - <see cref="BundleLayout"/>  —— `.app` 布局、必填条目、权限位判据
/// - <see cref="InfoPlist"/>     —— `Contents/Info.plist` 的生成与读回
/// - <see cref="LauncherScript"/>—— `Contents/MacOS/TipCL` 的正文
/// - <see cref="IcnsWriter"/>    —— `.icns` 的组装、结构读回、尺寸选择
/// - <see cref="MachO"/>         —— Mach-O 头与 `LC_CODE_SIGNATURE`
/// - <see cref="TarGz"/>         —— 确定性 tar(.gz)的写与读
/// - <see cref="BundleVerifier"/>—— 归档的结构校验
///
/// 命令行(由 `tools/package.ps1` 调):
///   --arm64 &lt;dir&gt; --x64 &lt;dir&gt; --icon &lt;ico&gt; --stage &lt;dir&gt; --out &lt;tar.gz&gt;
///
/// 退出码:0 = 全部校验通过;1 = 打包或校验失败;2 = 命令行参数不对。
/// **控制台输出一律 ASCII**(D-08;含中文的只有 Info.plist 里的显示名,那部分只出现在文件里)。
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;

    private const int ExitFailure = 1;

    private const int ExitUsage = 2;

    private static int Main(string[] args)
    {
        Options? options = Options.Parse(args);
        if (options == null)
        {
            Console.Error.WriteLine("usage: TipCL.Packager --arm64 <dir> --x64 <dir> --icon <ico> --stage <dir> --out <file.tar.gz>");
            return ExitUsage;
        }

        try
        {
            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message);
            return ExitFailure;
        }
    }

    private static int Run(Options options)
    {
        foreach (string dir in new[] { options.Arm64Dir, options.X64Dir })
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("FAIL: publish directory not found: " + dir);
                return ExitFailure;
            }
        }

        if (!File.Exists(options.IconPath))
        {
            Console.Error.WriteLine("FAIL: icon not found: " + options.IconPath);
            return ExitFailure;
        }

        // 1) 图标:.ico → .icns(不含放大;源图 < 128 px 时这里就是空数组)
        IconBuildResult icon = IconBuilder.Build(options.IconPath);
        StringBuilder sizes = new StringBuilder();
        foreach (IcoImageInfo image in icon.IcoImages)
        {
            if (sizes.Length > 0)
            {
                sizes.Append(',');
            }

            sizes.Append(image.Width);
        }

        Console.WriteLine("[packager] icon: " + icon.IcoBytes + " bytes, " + icon.IcoImages.Count
            + " images in the .ico (px: " + sizes + ", png=" + AllPng(icon.IcoImages) + ")");
        Console.WriteLine("[packager] icon: SkiaSharp decoded " + icon.SourceWidth + "x" + icon.SourceHeight
            + " (" + icon.SourceColorType + "/" + icon.SourceAlphaType + ")");

        if (icon.Slots.Count == 0)
        {
            Console.Error.WriteLine("FAIL: the icon source is smaller than 128 px (decoded "
                + icon.SourceWidth + "x" + icon.SourceHeight + "); refusing to upscale. Nothing was written.");
            return ExitFailure;
        }

        StringBuilder slots = new StringBuilder();
        foreach (IcnsSlot slot in icon.Slots)
        {
            if (slots.Length > 0)
            {
                slots.Append(' ');
            }

            slots.Append(slot.Type).Append('(').Append(slot.PixelSize).Append(')');
        }

        Console.WriteLine("[packager] icns: " + slots + ", " + icon.IcnsBytes.Length + " bytes");

        // 2) 暂存目录:组装 .app 目录树
        // 只删自己那棵 `TipCL.app`,不删整个 stage 目录:`--stage` 是外部传进来的参数,
        // 万一指到仓库根或别的目录上,递归删整棵树的后果太大;而 tar 里的条目全部来自
        // `TipCL.app/` 之下,清掉它就足以保证同一份输入两次运行结果一致。
        string appRoot = Path.Combine(options.StageDir, BundleLayout.AppName);
        if (Directory.Exists(appRoot))
        {
            Directory.Delete(appRoot, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(appRoot, "Contents", "MacOS", "arm64"));
        Directory.CreateDirectory(Path.Combine(appRoot, "Contents", "MacOS", "x64"));
        Directory.CreateDirectory(Path.Combine(appRoot, "Contents", "Resources"));

        WriteFile(Path.Combine(options.StageDir, RelPath(BundleLayout.InfoPlistEntry)), InfoPlist.BuildBytes(InfoPlist.ForTipCL()));
        WriteFile(Path.Combine(options.StageDir, RelPath(BundleLayout.LauncherEntry)), LauncherScript.Bytes());
        WriteFile(Path.Combine(options.StageDir, RelPath(BundleLayout.IcnsEntry)), icon.IcnsBytes);

        int arm64Files = CopyPublishOutput(
            options.Arm64Dir, Path.Combine(appRoot, "Contents", "MacOS", "arm64"), out int arm64Pdb);
        int x64Files = CopyPublishOutput(
            options.X64Dir, Path.Combine(appRoot, "Contents", "MacOS", "x64"), out int x64Pdb);
        Console.WriteLine("[packager] staged: arm64=" + arm64Files + " files, x64=" + x64Files + " files"
            + " (.pdb skipped: " + arm64Pdb + " + " + x64Pdb + ")");

        // 3) 归档条目:固定条目 + 两个架构目录下的实际文件
        List<TarSourceEntry> entries = new List<TarSourceEntry>();
        foreach (string dir in BundleLayout.RequiredDirectoryEntries)
        {
            entries.Add(TarSourceEntry.Dir(dir, BundleLayout.ModeDirectory));
        }

        foreach (string arch in BundleLayout.ArchNames)
        {
            string binRoot = Path.Combine(appRoot, "Contents", "MacOS", arch);
            foreach (string sub in Directory.GetDirectories(binRoot, "*", SearchOption.AllDirectories))
            {
                entries.Add(TarSourceEntry.Dir(RelPathFrom(options.StageDir, sub), BundleLayout.ModeDirectory));
            }

            foreach (string file in Directory.GetFiles(binRoot, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = RelPathFrom(options.StageDir, file);
                long length = new FileInfo(file).Length;
                byte[] head = ReadHead(file, 4096);
                entries.Add(TarSourceEntry.File(
                    name, BundleLayout.ModeForFile(name, head), length, () => File.OpenRead(file)));
            }
        }

        entries.Add(TarSourceEntry.FromBytes(BundleLayout.InfoPlistEntry, BundleLayout.ModeFile,
            InfoPlist.BuildBytes(InfoPlist.ForTipCL())));
        entries.Add(TarSourceEntry.FromBytes(BundleLayout.LauncherEntry, BundleLayout.ModeExecutable,
            LauncherScript.Bytes()));
        entries.Add(TarSourceEntry.FromBytes(BundleLayout.IcnsEntry, BundleLayout.ModeFile, icon.IcnsBytes));

        // 4) 写归档
        string outDir = Path.GetDirectoryName(Path.GetFullPath(options.OutPath)) ?? ".";
        Directory.CreateDirectory(outDir);
        using (FileStream output = File.Create(options.OutPath))
        {
            TarGz.WriteTarGz(entries, output);
        }

        // 5) 从磁盘读回并校验(不拿内存里的东西自证)
        long archiveBytes = new FileInfo(options.OutPath).Length;
        string sha256 = Sha256OfFile(options.OutPath);
        List<TarEntrySnapshot> readBack;
        using (FileStream input = File.OpenRead(options.OutPath))
        {
            readBack = TarGz.ReadTarGz(input, BundleVerifier.HeadBytes,
                name => name == BundleLayout.InfoPlistEntry
                    || name == BundleLayout.IcnsEntry
                    || name == BundleLayout.LauncherEntry);
        }

        VerifyResult result = BundleVerifier.Verify(readBack, InfoPlist.ForTipCL(), icon.Slots);

        Console.WriteLine("[packager] verification of " + options.OutPath + " (re-read from disk):");
        foreach (CheckResult check in result.Checks)
        {
            Console.WriteLine("  " + BundleVerifier.FormatLine(check));
        }

        foreach (string line in result.Report)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine("[packager] entries=" + result.EntryCount
            + " uncompressed=" + result.TotalUncompressedBytes + " bytes"
            + " archive=" + archiveBytes + " bytes"
            + " sha256=" + sha256);
        Console.WriteLine("[packager] mach-o files=" + result.MachOCount
            + " with LC_CODE_SIGNATURE=" + result.MachOWithCodeSignature
            + " without=" + result.MachOWithoutCodeSignature);

        int failed = 0;
        foreach (CheckResult check in result.Checks)
        {
            if (check.Required && !check.Ok)
            {
                failed++;
            }
        }

        Console.WriteLine("[packager] " + (failed == 0 ? "ALL CHECKS PASSED" : failed + " CHECK(S) FAILED"));
        return failed == 0 ? ExitOk : ExitFailure;
    }

    private static bool AllPng(IReadOnlyList<IcoImageInfo> images)
    {
        foreach (IcoImageInfo image in images)
        {
            if (!image.IsPng)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>把 emit 目录的内容复制进 bundle(逐字节;`.pdb` 不复制)。返回复制的文件数,`skipped` 是跳过的 .pdb 数。</summary>
    private static int CopyPublishOutput(string sourceDir, string targetDir, out int skipped)
    {
        Directory.CreateDirectory(targetDir);
        int count = 0;
        skipped = 0;
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            count++;
        }

        return count;
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string RelPath(string bundleEntryName)
    {
        // bundle 条目名用的是 '/',转回当前平台的分隔符(只在 Windows 上跑)。
        return BundleLayout.NormalizeEntryName(bundleEntryName).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string RelPathFrom(string root, string fullPath)
    {
        return BundleLayout.NormalizeEntryName(Path.GetRelativePath(root, fullPath));
    }

    private static byte[] ReadHead(string path, int max)
    {
        using FileStream stream = File.OpenRead(path);
        int length = (int)Math.Min(max, stream.Length);
        byte[] head = new byte[length];
        stream.ReadExactly(head, 0, length);
        return head;
    }

    private static string Sha256OfFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    /// <summary>命令行参数。</summary>
    private sealed class Options
    {
        public required string Arm64Dir { get; init; }

        public required string X64Dir { get; init; }

        public required string IconPath { get; init; }

        public required string StageDir { get; init; }

        public required string OutPath { get; init; }

        public static Options? Parse(string[] args)
        {
            string? arm64 = null;
            string? x64 = null;
            string? icon = null;
            string? stage = null;
            string? output = null;
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                string key = args[i];
                string value = args[i + 1];
                switch (key)
                {
                    case "--arm64":
                        arm64 = value;
                        break;
                    case "--x64":
                        x64 = value;
                        break;
                    case "--icon":
                        icon = value;
                        break;
                    case "--stage":
                        stage = value;
                        break;
                    case "--out":
                        output = value;
                        break;
                    default:
                        return null;
                }
            }

            if (arm64 == null || x64 == null || icon == null || stage == null || output == null)
            {
                return null;
            }

            return new Options
            {
                Arm64Dir = arm64,
                X64Dir = x64,
                IconPath = icon,
                StageDir = stage,
                OutPath = output,
            };
        }
    }
}

using System;
using TipCL.Packager;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B3:tar 条目权限位的判据(`BundleLayout.ModeForFile` / `ModeForDirectory`)。
/// 判据来自任务书 B3 的「Tar entry modes」小节:目录一律 0755;`Contents/MacOS/TipCL`、
/// 两个 apphost、`*.dylib`、以及文件头是 Mach-O 且 filetype == 2 的文件一律 0755;其余 0644;
/// 且没有任何条目对 group/other 可写。
/// 名字里出现的 `createdump` / `libSkiaSharp.dylib` / `TipCL.dll` 是本机真实 publish 输出里
/// 实际存在的文件(见 PROGRESS.md 的 B3 小节)。
/// </summary>
public class B3ModeClassificationTests
{
    private const int Mode755 = 493;   // 0o755

    private const int Mode644 = 420;   // 0o644

    [Theory]
    [InlineData("TipCL.app/Contents/MacOS/TipCL")]
    [InlineData("TipCL.app/Contents/MacOS/arm64/TipCL")]
    [InlineData("TipCL.app/Contents/MacOS/x64/TipCL")]
    public void The_launcher_and_both_apphosts_are_0755(string name)
    {
        Assert.Equal(Mode755, BundleLayout.ModeForFile(name, B3PackagerFixtures.MachOImage(
            B3PackagerFixtures.CpuArm64, 2, 0x1D)));
    }

    [Theory]
    [InlineData("TipCL.app/Contents/MacOS/arm64/libSkiaSharp.dylib")]
    [InlineData("TipCL.app/Contents/MacOS/x64/libSkiaSharp.dylib")]
    [InlineData("TipCL.app/Contents/MacOS/arm64/libcoreclr.dylib")]
    public void Dylibs_are_0755_even_without_inspecting_the_head(string name)
    {
        Assert.Equal(Mode755, BundleLayout.ModeForFile(name, null));
    }

    [Theory]
    [InlineData("TipCL.app/Contents/MacOS/arm64/TipCL.dll")]
    [InlineData("TipCL.app/Contents/MacOS/arm64/TipCL.Core.dll")]
    [InlineData("TipCL.app/Contents/MacOS/x64/TipCL.runtimeconfig.json")]
    public void Managed_assemblies_and_json_are_0644(string name)
    {
        Assert.Equal(Mode644, BundleLayout.ModeForFile(
            name, B3PackagerFixtures.Filler(64, 0x40)));
    }

    [Theory]
    [InlineData("TipCL.app/Contents/MacOS/arm64/createdump")]
    [InlineData("TipCL.app/Contents/MacOS/x64/some-tool")]
    public void A_mach_o_executable_with_any_name_is_0755(string name)
    {
        Assert.Equal(Mode755, BundleLayout.ModeForFile(
            name, B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 2, 0x1D)));
    }

    [Fact]
    public void A_mach_o_dylib_image_under_a_non_dylib_name_is_not_treated_as_executable()
    {
        // filetype 6 = dylib,任务书只说 filetype == 2 算可执行;这个文件不以 .dylib 结尾,所以是 0644
        Assert.Equal(Mode644, BundleLayout.ModeForFile(
            "TipCL.app/Contents/MacOS/arm64/odd.bin",
            B3PackagerFixtures.MachOImage(B3PackagerFixtures.CpuArm64, 6)));
    }

    [Fact]
    public void Windows_separators_are_normalized_before_classifying()
    {
        Assert.Equal(Mode755, BundleLayout.ModeForFile("TipCL.app\\Contents\\MacOS\\TipCL", null));
    }

    [Fact]
    public void Directories_are_0755()
    {
        Assert.Equal(Mode755, BundleLayout.ModeForDirectory());
        Assert.Equal(Mode755, BundleLayout.ModeDirectory);
        Assert.Equal(Mode755, BundleLayout.ModeExecutable);
        Assert.Equal(Mode644, BundleLayout.ModeFile);
    }

    [Fact]
    public void No_entry_may_have_group_or_other_write_bits()
    {
        Assert.Equal(18, BundleLayout.GroupOtherWriteMask);          // 0o022
        Assert.Equal(0, Mode755 & BundleLayout.GroupOtherWriteMask);
        Assert.Equal(0, Mode644 & BundleLayout.GroupOtherWriteMask);
        Assert.NotEqual(0, 438 & BundleLayout.GroupOtherWriteMask);  // 0o666 会被判出来
    }

    [Fact]
    public void Required_entries_cover_everything_the_brief_lists()
    {
        Assert.Contains(BundleLayout.InfoPlistEntry, BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.LauncherEntry, BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.Arm64HostEntry, BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.X64HostEntry, BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.IcnsEntry, BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.ArchDirEntry("arm64", "TipCL.dll"), BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.ArchDirEntry("arm64", "TipCL.Core.dll"), BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.ArchDirEntry("x64", "TipCL.dll"), BundleLayout.RequiredFileEntries);
        Assert.Contains(BundleLayout.ArchDirEntry("x64", "TipCL.Core.dll"), BundleLayout.RequiredFileEntries);

        Assert.Contains(BundleLayout.RootEntry, BundleLayout.RequiredDirectoryEntries);
        Assert.Contains("TipCL.app/Contents/", BundleLayout.RequiredDirectoryEntries);
        Assert.Contains("TipCL.app/Contents/MacOS/", BundleLayout.RequiredDirectoryEntries);
        Assert.Contains("TipCL.app/Contents/Resources/", BundleLayout.RequiredDirectoryEntries);
        Assert.Contains("TipCL.app/Contents/MacOS/arm64/", BundleLayout.RequiredDirectoryEntries);
        Assert.Contains("TipCL.app/Contents/MacOS/x64/", BundleLayout.RequiredDirectoryEntries);
    }

    [Fact]
    public void Directory_entries_always_end_with_a_slash()
    {
        Assert.Equal("TipCL.app/", BundleLayout.DirEntry("TipCL.app"));
        Assert.Equal("TipCL.app/", BundleLayout.DirEntry("TipCL.app/"));
        Assert.Equal("TipCL.app/Contents/MacOS/arm64/", TarSourceEntry.Dir(BundleLayout.Arm64Dir).Name);
    }

    [Fact]
    public void Entry_names_are_normalized_to_forward_slashes()
    {
        Assert.Equal("a/b/c", BundleLayout.NormalizeEntryName("a\\b\\c"));
        Assert.Equal("a/b", BundleLayout.NormalizeEntryName("a/b"));
        Assert.Equal("TipCL.app/Contents/MacOS/arm64/TipCL.dll",
            BundleLayout.ArchDirEntry("arm64", "TipCL.dll"));
    }
}

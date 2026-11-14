using TipCL.Core;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>PPath 跨平台路径逻辑单测（M1-A5）。Windows 语义用显式 windows:true 分支覆盖，不依赖宿主平台。</summary>
public class PPathTests
{
    [Fact]
    public void WindowsPath_Basic_Properties()
    {
        var p = new PPath(true, "C:\\", "Users", "Angus", "a.txt");
        Assert.True(p.Windows);
        Assert.Equal("C:", p.Drive);
        Assert.Equal("a.txt", p.Name);
        Assert.Equal(".txt", p.Suffix);
        Assert.Equal("a", p.Stem);
        Assert.True(p.IsAbsolute);
    }

    [Fact]
    public void PosixPath_Basic_Properties()
    {
        var p = new PPath(false, "/home/user/a.txt");
        Assert.False(p.Windows);
        Assert.Equal("/", p.Root);
        Assert.Equal("a.txt", p.Name);
        Assert.Equal(".txt", p.Suffix);
        Assert.True(p.IsAbsolute);
    }

    [Fact]
    public void SlashOperator_AppendsSegment()
    {
        var p = new PPath(true, "C:\\minecraft") / "mods" / "jei.jar";
        Assert.Equal("jei.jar", p.Name);
        Assert.Equal("mods", p.Parent.Name);
    }

    [Fact]
    public void Parent_OfRoot_IsSelf()
    {
        var p = new PPath(false, "/");
        Assert.Equal(p, p.Parent);
        var root = new PPath(false, "/root");
        Assert.Equal(new PPath(false, "/"), root.Parent);
    }

    [Fact]
    public void RelativePath_NotAbsolute()
    {
        var p = new PPath(true, "rel", "sub");
        Assert.False(p.IsAbsolute);
        Assert.Equal("", p.Drive);
    }

    [Fact]
    public void WithSuffix_ReplacesExtension()
    {
        var p = new PPath(true, "C:\\dir\\mod.jar");
        Assert.Equal(".disabled", p.WithSuffix(".disabled").Suffix);
        Assert.Equal("mod", p.Stem);
    }

    [Fact]
    public void Equality_SameParts_AreEqual()
    {
        var a = new PPath(false, "/home", "user", "x");
        var b = new PPath(false, "/home/user/x");
        Assert.Equal(a, b);
        Assert.True(a == b || a.Equals(b));
    }

    [Fact]
    public void IsRelativeTo_TrueForAncestor()
    {
        var baseDir = new PPath(false, "/home/user/.minecraft");
        var child = baseDir / "mods" / "a.jar";
        Assert.True(child.IsRelativeTo(baseDir));
        Assert.False(baseDir.IsRelativeTo(child));
    }

    [Fact]
    public void NullSegment_ThrowsTypeError()
    {
        Assert.ThrowsAny<Exception>(() => new PPath(true, (object)null!));
    }
}

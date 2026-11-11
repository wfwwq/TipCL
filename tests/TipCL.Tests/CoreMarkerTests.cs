using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A1 的骨架测试:证明测试工程能编译、能引用 TipCL.Core、断言是真的。
/// A6 会搬 Windows 版真正的不依赖 WPF 的测试进来。
/// </summary>
public sealed class CoreMarkerTests
{
    [Fact]
    public void CoreMarker_Namespace_Is_TipCL_Core()
    {
        Assert.Equal("TipCL.Core", typeof(CoreMarker).Namespace);
    }

    [Fact]
    public void CoreMarker_Name_Matches_AssemblyName()
    {
        Assert.Equal("TipCL.Core", CoreMarker.Name);
        Assert.Equal("TipCL.Core", typeof(CoreMarker).Assembly.GetName().Name);
    }

    [Fact]
    public void CoreMarker_PlaceholderVersion_Is_Not_Empty()
    {
        Assert.Equal("0.0.0-a1", CoreMarker.PlaceholderVersion);
        Assert.NotEmpty(CoreMarker.PlaceholderVersion);
    }

    [Theory]
    [InlineData("TipCL.Core")]
    [InlineData("TipCL")]
    public void CoreMarker_Name_Contains_TipCL(string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix, CoreMarker.Name);
    }
}

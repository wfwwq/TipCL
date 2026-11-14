using TipCL.Core;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 占位测试（M1-A1）：验证测试基建可用。A5 充实后可移除。
/// </summary>
public class SmokeTests
{
    [Fact]
    public void CoreVersion_Is_M3()
    {
        Assert.Equal("0.1.0-m3", CoreInfo.Version);
    }
}

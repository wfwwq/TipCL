using Xunit;

namespace TipCL.Tests.Smoke;

/// <summary>
/// 冒烟集成测试（M1-A5）。`--smoke` 需要启动 Avalonia Headless 渲染主窗口，
/// TipCL.App 的 Program/SmokeRunner 均为 internal，无法进程内调用；
/// 因此进程级冒烟由 tools/build.ps1 -Smoke / tools/build.sh --smoke 承担（见 docs/README.md）。
/// 此处保留 Skip 占位注明原因；若后续 App 侧提供公开的可编程入口（依赖申请见 progress/M1-A5.md），再补进程内测试。
/// </summary>
public class SmokeIntegrationTests
{
    [Fact(Skip = "冒烟由 tools/build.* 的 --smoke 流程执行：Program/SmokeRunner 为 internal 且依赖 Headless 平台启动，测试进程内不可达")]
    public void SmokeArg_Renders_MainWindow_To_SmokePng()
    {
    }
}

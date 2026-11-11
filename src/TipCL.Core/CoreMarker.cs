namespace TipCL.Core;

/// <summary>
/// A1 脚手架阶段的占位类型:证明 TipCL.Core 类库能编译、能被引用、能被测试。
/// A2 起会把 Windows 版 <c>src/TipCL/Core/</c> 的真实代码复制进本工程,本类型保留不影响它。
/// </summary>
public static class CoreMarker
{
    /// <summary>与 AssemblyName 保持一致,测试用它做真实断言。</summary>
    public const string Name = "TipCL.Core";

    /// <summary>A1 占位版本号。真实版本号在 A2 由 AppInfo.APP_VERSION 提供。</summary>
    public const string PlaceholderVersion = "0.0.0-a1";
}

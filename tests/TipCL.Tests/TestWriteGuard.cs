// 测试写盘守卫(R31-CIFLAKE)。
//
// 为什么需要它 —— 这条是 ubuntu CI 上一条间歇红的根因所在:
//   * 自更新的"就地替换"层 `Updater.ApplyStaged()` 会真的写文件,目标来自 `Updater.UpdateTarget()`;
//     在 `dotnet test` 下,`UpdateTarget()` 的兜底(没有 `$APPIMAGE` 时)是
//     `Process.GetCurrentProcess().MainModule.FileName` —— 那**就是测试宿主自己**(Linux runner 上
//     实测被写坏的文件是 `/usr/share/dotnet/dotnet`,Windows 上是 `tests/.../bin/Debug/net8.0/testhost.exe`),
//     不是被测的那个程序;
//   * App 批次(CI 的 batch 1/2)里 `SelfUpdateFeedbackTests` 的"正常路径"用例会 emit 一次进程级的
//     `update_staged`,包里装的正是 `TipCL-fake-package-bytes-...` 这段假字节;而**别的用例留下的
//     MainWindow 实例**仍然订阅着这个事件(`MainWindow.axaml.cs:46-54`)——它是全仓唯一的订阅者,
//     于是 `OfferRestartForUpdateAsync()`(`MainWindow.axaml.cs:244`)→ `apply_update`
//     → `Api.cs:529-533` → `ApplyStaged()` → 写 `UpdateTarget()`;
//   * 写下去的那一刻:`Dlg.Confirm` 在无头会话里解析不到宿主(`Dlg.Resolve()` 的
//     `Dlg.Owner ?? desktop.MainWindow` 都是 null,`Dlg.cs:58-59` 直接返回 false)⇒ 不等用户点任何东西,
//     立刻走到 `apply_update(restart: false)`,就地替换照样执行。
//
// 本机实测(Windows,`--filter` 只跑两个类,顺序由 xUnit 定,每轮不同):
//   NavigateTests(= 先造出一个 MainWindow)→ SelfUpdateFeedbackTests  ⇒ 更新目标被写入假包字节;
//   SelfUpdateFeedbackTests → NavigateTests(= 事件发出时还没有 MainWindow)⇒ 什么都不发生。
//   同一个提交,红/绿只取决于**测试类的执行顺序**(xUnit 每个进程都不一样)。
//   Linux 上"换掉正在运行的二进制"是允许的(rename 成功,进程继续用旧 inode)
//   ⇒ 被写坏的是 runner 的全局 dotnet 宿主,下一条 `dotnet test` 直接
//     `/usr/share/dotnet/dotnet: line 1: TipCL-fake-package-bytes-...: command not found`,exit 127;
//   Windows 上目标是**正在运行**的 testhost.exe,`File.Move(..., overwrite: true)` 被系统拒绝
//   (UnauthorizedAccessException)⇒ 只留一条被 `TryCallAsync` 吞掉的 WARN,不破坏任何东西
//   —— 这就是"本机 Windows 跑不出来"的原因。
//
// 于是本文件做两件事(都只在测试侧,不动产品行为、不放松任何断言):
//   1. **进程级沙箱**:测试进程里 `Updater.UpdateTarget()` 的注入点(`TargetOverride`,产品自带的
//      测试注入点,`Updater.cs:108-109、118-121`)在**模块初始化时**就被钉在隔离根内的一个临时文件上。
//      这样无论哪个用例、哪条泄漏出来的异步反应触发 `ApplyStaged()`,它都只能写到隔离根里,
//      永远不可能落到测试宿主、更不可能落到 runner 的 `dotnet` 上。
//      **必须是进程级、且从进程开头就生效**:该反应是 `Dispatcher.UIThread.Post` 出去的续体
//      (`Api.cs:99-107`),常常在**发出事件的那个用例已经结束之后**(由后面某个用例的 pump / 关窗动作)
//      才落地,所以用例内作用域的 try/finally 覆盖不到它。
//   2. **越界即抛错的守卫**:`AssertInsideIsolation()` 用来在任何写盘前后校验目标路径落在隔离根内,
//      越界当场抛 `OutsideIsolationWriteException`(而不是悄悄把 runner 写坏)。让这类事故
//      以后"当场红",而不是表现为几十秒后另一条命令的 127。
//
// 覆盖边界(如实说明):.NET 没有"拦截所有 File.* 调用"的钩子,所以本守卫的**强制**点只有产品侧
// 唯一那个写盘入口 `Updater.ApplyStaged()`(它读 `TargetOverride`);其余地方要靠用例**显式**调用
// `AssertInsideIsolation()`。本仓自更新的写盘路径只有这一条(全仓只有 `Updater.ApplyStaged` 用
// `UpdateTarget()`,`Download()` 落到 `Utils.ROOT/cache`,而 `Utils.ROOT` 已被 `<see cref="TestIsolation"/>` 钉住)。
using System;
using System.IO;
using TipCL.Core.Launcher;

namespace TipCL.Tests;

/// <summary>测试把文件写到隔离根之外(典型:写到测试宿主 / runner 自己的文件上)。</summary>
internal sealed class OutsideIsolationWriteException : Exception
{
    public OutsideIsolationWriteException(string message) : base(message) { }
}

/// <summary>
/// 测试进程的写盘边界:隔离根 = <see cref="TestIsolation"/> 建的临时 TIPCL_HOME。
/// 见文件头的来龙去脉(ubuntu CI 间歇红)。
/// </summary>
internal static class TestWriteGuard
{
    /// <summary>隔离根(绝对路径,不带尾部分隔符)。模块初始化时由 <see cref="Install"/> 设定。</summary>
    public static string IsolationRoot { get; private set; } = "";

    /// <summary>进程级更新目标沙箱:隔离根内的一个临时文件,产品做"就地替换"时只该碰它。</summary>
    public static string UpdateTargetSandbox { get; private set; } = "";

    /// <summary>
    /// 在**任何测试体之前**装好沙箱(由 <see cref="TestIsolation"/> 的模块初始化器调用)。
    /// 必须早于任何可能跑自更新链路的用例:进程里一旦有 MainWindow 实例活着,泄漏的
    /// `update_staged` 反应随时可能在别的用例的 pump 里落地。
    /// </summary>
    public static void Install(string isolationRoot)
    {
        IsolationRoot = Trim(Path.GetFullPath(isolationRoot));
        string dir = Path.Combine(IsolationRoot, "update-target");
        Directory.CreateDirectory(dir);
        UpdateTargetSandbox = Path.Combine(dir, "TipCL.App");
        if (!File.Exists(UpdateTargetSandbox))
            File.WriteAllText(UpdateTargetSandbox, "TipCL test isolation sandbox");

        // 产品自带的注入点:UpdateTarget() 首选它(见 Updater.cs:116-130)。这里先断言它确实落在隔离根内,
        // 免得以后有人在别处把 TargetOverride 指到外面(那就等于把 runner 交出去)。
        AssertInsideIsolation(UpdateTargetSandbox, "进程级更新目标沙箱");
        string existing = Updater.TargetOverride;
        if (existing.Length > 0)
            AssertInsideIsolation(existing, "别人预先设好的 Updater.TargetOverride");
        RestoreProcessSandbox();
    }

    /// <summary>把进程级沙箱装回去(别的用例清过注入点之后用它复位,见 UpdaterTests.Dispose)。</summary>
    public static void RestoreProcessSandbox()
    {
        if (UpdateTargetSandbox.Length == 0) return;
        AssertInsideIsolation(UpdateTargetSandbox, "进程级更新目标沙箱");
        Updater.TargetOverride = UpdateTargetSandbox;
    }

    /// <summary>路径是否落在隔离根内(根自身也算)。空路径 = 不落内。</summary>
    public static bool IsInsideIsolation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsolationRoot.Length == 0) return false;
        string full;
        try { full = Trim(Path.GetFullPath(path)); }
        catch (Exception) { return false; }
        string root = IsolationRoot;
        if (OperatingSystem.IsWindows())
        {
            full = full.ToLowerInvariant();
            root = root.ToLowerInvariant();
        }
        return full == root || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>越界当场抛错。<paramref name="what"/> 说明这次写的是什么(错误信息里要能看懂)。</summary>
    public static void AssertInsideIsolation(string path, string what)
    {
        if (IsInsideIsolation(path)) return;
        throw new OutsideIsolationWriteException(
            "测试把文件写到了隔离根之外 —— 这是 ubuntu CI 那条 exit 127 的成因,不允许发生。\n" +
            "  要写的东西:" + what + "\n" +
            "  目标路径  :" + (string.IsNullOrEmpty(path) ? "<空>" : path) + "\n" +
            "  隔离根    :" + (IsolationRoot.Length == 0 ? "<未安装,TestIsolation 没跑?>" : IsolationRoot) + "\n" +
            "  提示      :自更新的就地替换目标来自 Updater.UpdateTarget();它在没有 $APPIMAGE 时会兜底到\n" +
            "              Process.GetCurrentProcess().MainModule.FileName(= 测试宿主,Linux runner 上是\n" +
            "              /usr/share/dotnet/dotnet)。测试里必须走隔离根内的沙箱(TestWriteGuard),\n" +
            "              绝不能让产品的回落值决定写哪。");
    }

    /// <summary>
    /// 临时退出进程级沙箱(只有"专测产品回落分支"的用例需要,例如断言 `$APPIMAGE` 优先的
    /// <c>UpdaterTests.UpdateTarget_PrefersTheAppImagePath</c>)。用完立刻还原成沙箱。
    /// </summary>
    public static IDisposable SuppressSandbox() => new Suppression();

    private sealed class Suppression : IDisposable
    {
        public Suppression() => Updater.TargetOverride = "";

        public void Dispose() => RestoreProcessSandbox();
    }

    private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

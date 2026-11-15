// R30-WP6d G1(源自 veri-docs 核对建议 D8,见 progress/R30-REWORK.md:36):把「两个 csproj 的 <Version>」
// 与运行时常量绑成守卫。
//
// 背景(建议原文):13 个版本落点(docs/RELEASE-DRAFT.md §1.1)目前**全靠一次手工文本替换**保持一致。
// 唯一的既有守卫是 `SmokeTests.cs:19`,它只断言**两个编译期常量之间**的相等
// (`AppInfo.APP_VERSION == CoreInfo.Version`),而两个 csproj 的 `<Version>` —— 也就是**程序集版本 /
// 文件属性 / 打包产物里内嵌的那个号** —— 全树没有任何用例读过(`git grep 'csproj' HEAD -- tests` 的
// 命中全是注释与 ProjectReference)。只要有人只改一处,就重演 1.1 那次"两个源打架"的事故
// (应用内显示一个号、产物属性另一个号)。
//
// 本文件的做法(**刻意不抄第二份常量,避免自我印证**):
//   1) 从测试输出目录往上找到仓库根,真读**磁盘上**那两个 csproj,取 `<Version>` 的文本;
//   2) 真读**编译产物**的程序集属性(`typeof(CoreInfo).Assembly` 与 TipCL.App 程序集);
//   3) 让上面三者与运行时常量 `AppInfo.APP_VERSION` / `CoreInfo.Version` 互相相等。
// 于是:只改 csproj ⇒ 第 1、2 条红;只改常量 ⇒ 第 1 条红;两边都改但不一致 ⇒ 全红。
//
// 与 `SmokeTests.cs` 的分工:那条盯"两个常量不许打架",本文件盯"常量与磁盘上的发版声明不许打架"。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Core;

public class VersionLandingPointsTests
{
    private const string CoreCsproj = "src/TipCL.Core/TipCL.Core.csproj";
    private const string AppCsproj = "src/TipCL.App/TipCL.App.csproj";

    /// <summary>仓库根目录:从测试输出目录往上找 `TipCL.Linux.sln` 当锚点。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TipCL.Linux.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException(
            "从 " + AppContext.BaseDirectory + " 往上找不到 TipCL.Linux.sln —— 这个守卫用例必须能读到磁盘上的 csproj");
    }

    private static string CsprojPath(string relative)
        => Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>真读磁盘上 csproj 里的 `&lt;Version&gt;` 文本;读不到就当场判红(守卫的前提没了,不能静默跳过)。</summary>
    private static string CsprojVersion(string relative)
    {
        string path = CsprojPath(relative);
        Assert.True(File.Exists(path), "找不到 " + path + " —— 这个守卫用例必须能读到磁盘上的 csproj");
        XElement? version = XDocument.Load(path).Root?.Elements("PropertyGroup").Elements("Version").FirstOrDefault();
        Assert.False(version is null, relative + " 里没有 <Version> 元素 —— 守卫的前提没了");
        return version!.Value.Trim();
    }

    /// <summary>取编译产物的程序集属性值(优先版本名解析,退化到输出目录里的 dll)。</summary>
    private static Assembly LoadProductAssembly(string simpleName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(simpleName));
        }
        catch (Exception e)
        {
            string path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
            Assert.True(File.Exists(path), "既加载不到 " + simpleName + " 程序集,输出目录里也没有 " + path + "(原因:" + e.Message + ")");
            return Assembly.LoadFrom(path);
        }
    }

    /// <summary>`<Version>1.2.0</Version>` 生成的 `AssemblyInformationalVersion` 形如 `1.2.0+&lt;commit sha&gt;`
    /// (SourceLink 追加),所以比较前把 `+` 后缀切掉;`AssemblyFileVersion` 是四段 `1.2.0.0`。</summary>
    private static string InformationalVersion(Assembly asm)
    {
        string raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        int plus = raw.IndexOf('+');
        return (plus >= 0 ? raw.Substring(0, plus) : raw).Trim();
    }

    private static string FileVersion(Assembly asm)
        => asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "";

    /// <summary>
    /// 两个 csproj 的 `<Version>` 必须等于**运行时的版本真源** `AppInfo.APP_VERSION`
    /// (更新判据 / 关于页 / 插件引擎兼容 / 反馈载荷 / sysinfo 都读它),也必须等于 `CoreInfo.Version`
    /// (启动横幅;`tools/deb.sh:49-51` 还从它取 .deb 版本号)。
    /// </summary>
    [Fact]
    public void CsprojVersion_MatchesAppVersionAndCoreInfo()
    {
        string core = CsprojVersion(CoreCsproj);
        string app = CsprojVersion(AppCsproj);

        Assert.Equal(AppInfo.APP_VERSION, core);   // src/TipCL.Core/TipCL.Core.csproj 的 <Version>
        Assert.Equal(AppInfo.APP_VERSION, app);    // src/TipCL.App/TipCL.App.csproj 的 <Version>
        Assert.Equal(CoreInfo.Version, core);      // 与 SmokeTests 的同一条约定,但比的是磁盘上的声明
        Assert.Equal(AppInfo.APP_VERSION, CoreInfo.Version);
    }

    /// <summary>
    /// 13 个落点里另外两个纯版本常量(`LAUNCHER_VERSION` 拼 `--launcher_version` 传给 Minecraft、
    /// `__version__` 是照参考版留的占位)同样不许单飞。
    /// 说明:`docs/RELEASE-DRAFT.md` §1.1 把它们与 `APP_VERSION` 一起当作"同一个产品版本"处理;
    /// 若将来确实需要分叉,请在这条用例里把它显式豁免,而不是让它悄悄漂走。
    /// </summary>
    [Fact]
    public void OtherVersionConstants_FollowTheSameNumber()
    {
        Assert.Equal(AppInfo.APP_VERSION, AppInfo.LAUNCHER_VERSION);
        Assert.Equal(AppInfo.APP_VERSION, AppInfo.__version__);
    }

    /// <summary>
    /// csproj 那个号真正的**消费者**是编译产物本身:两个程序集的 `AssemblyInformationalVersion`
    /// (关于页之外的"文件属性")与 `AssemblyFileVersion` 必须携带同一个号。
    /// 这一条读的是**真实程序集**,不是文本 —— 于是"只改常量、不改 csproj"也会在这里被抓到。
    /// </summary>
    [Fact]
    public void CompiledAssemblyAttributes_CarryTheSameVersion()
    {
        Assembly core = typeof(CoreInfo).Assembly;
        Assembly app = LoadProductAssembly("TipCL.App");

        Assert.Equal(AppInfo.APP_VERSION, InformationalVersion(core));
        Assert.Equal(AppInfo.APP_VERSION, InformationalVersion(app));

        // <Version>1.2.0</Version> ⇒ AssemblyFileVersion 自动补成四段 1.2.0.0
        Assert.Equal(AppInfo.APP_VERSION + ".0", FileVersion(core));
        Assert.Equal(AppInfo.APP_VERSION + ".0", FileVersion(app));
    }
}

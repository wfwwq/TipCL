using System.Collections.Generic;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.Tests;

/// <summary>
/// B2d 的版本 JSON 夹具。形状抄自 B2b 的 <c>B2bMacNativeSupportTests</c>(那里 8 个版本的取证结论见
/// PROGRESS.md 的 B2b 第 0 步;响应体在 <c>artifacts/b2b/</c>,不入库),但这里额外允许指定
/// natives jar 的 <c>path</c>,方便在 <see cref="TestHome"/> 之下造出真实文件,验证
/// <c>Installer._natives_to_extract</c> 这类会查文件的代码。
/// </summary>
internal static class B2dFixtures
{
    /// <summary>1.16.5 形态的 LWJGL natives jar(路径按 maven 布局自造,只用在本仓库测试目录里)。</summary>
    internal const string LwjglNativesJar = "org/lwjgl/lwjgl/3.2.1/lwjgl-3.2.1-natives-macos.jar";

    /// <summary>1.16.5 形态的 java-objc-bridge natives jar。</summary>
    internal const string ObjcBridgeJar = "ca/weblite/java-objc-bridge/1.0.0/java-objc-bridge-1.0.0-natives-osx.jar";

    internal static PyDict Allow(string osName) => PyDict.Of("action", "allow", "os", PyDict.Of("name", osName));

    internal static PyDict AllowAll() => PyDict.Of("action", "allow");

    internal static PyDict Disallow(string osName) => PyDict.Of("action", "disallow", "os", PyDict.Of("name", osName));

    /// <summary>按 "k1","v1","k2","v2"... 建字典(<see cref="PyDict.Of"/> 只收固定数量的键值对,这里要变长)。</summary>
    internal static PyDict Dict(params string[] pairs)
    {
        PyDict pyDict = new PyDict();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            pyDict[pairs[i]] = pairs[i + 1];
        }
        return pyDict;
    }

    internal static PyDict Lib(string name, PyDict natives, string[] classifierKeys, params PyDict[] rules)
    {
        return Lib(name, natives, classifierKeys, null, rules);
    }

    /// <summary>一条 library 条目。<paramref name="classifierPaths"/> 为 null 时用 "org/example/&lt;键&gt;.jar"。</summary>
    internal static PyDict Lib(string name, PyDict natives, string[] classifierKeys, string[] classifierPaths, params PyDict[] rules)
    {
        PyDict pyDict = PyDict.Of("name", name);
        if (natives != null && natives.Count > 0)
        {
            pyDict["natives"] = natives;
        }
        if (classifierKeys != null && classifierKeys.Length > 0)
        {
            PyDict pyDict2 = new PyDict();
            for (int i = 0; i < classifierKeys.Length; i++)
            {
                string text = ((classifierPaths != null && classifierPaths.Length > i && classifierPaths[i] != null) ? classifierPaths[i] : ("org/example/" + classifierKeys[i] + ".jar"));
                pyDict2[classifierKeys[i]] = PyDict.Of("path", text);
            }
            pyDict["downloads"] = PyDict.Of("classifiers", pyDict2);
        }
        if (rules != null && rules.Length > 0)
        {
            List<object> list = new List<object>();
            foreach (PyDict pyDict3 in rules)
            {
                list.Add(pyDict3);
            }
            pyDict["rules"] = list;
        }
        return pyDict;
    }

    internal static PyDict Version(params PyDict[] libs)
    {
        List<object> list = new List<object>();
        foreach (PyDict pyDict in libs)
        {
            list.Add(pyDict);
        }
        return PyDict.Of("libraries", list);
    }

    private static PyDict OsxNatives(string osxKey)
    {
        return Dict("linux", "natives-linux", "osx", osxKey, "windows", "natives-windows");
    }

    private static PyDict NonMacNatives()
    {
        return Dict("linux", "natives-linux", "windows", "natives-windows");
    }

    private static string[] WithOsx(string osxKey)
    {
        return new[] { "natives-linux", osxKey, "natives-windows" };
    }

    /// <summary>1.7.10:LWJGL 2.9.1,老式 <c>natives-osx</c>。</summary>
    internal static PyDict V1710() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.1", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("tv.twitch:twitch-platform:5.16", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("linux")),
        Lib("tv.twitch:twitch-external-platform:4.5", Dict("windows", "natives-windows-${arch}"), null, Allow("windows")));

    /// <summary>1.8.9:含一条 disallow osx 的 nightly。</summary>
    internal static PyDict V189() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("osx")),
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.2-nightly-20140822", OsxNatives("natives-osx"), WithOsx("natives-osx"), Allow("osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("tv.twitch:twitch-platform:6.5", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("linux")),
        Lib("tv.twitch:twitch-external-platform:4.5", Dict("windows", "natives-windows-${arch}"), null, Allow("windows")));

    /// <summary>1.12.2:老式参数版本(<c>minecraftArguments</c>),natives 仍是 <c>natives-osx</c>。</summary>
    internal static PyDict V1122() => Version(
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209", OsxNatives("natives-osx"), WithOsx("natives-osx"), AllowAll(), Disallow("osx")),
        Lib("org.lwjgl.lwjgl:lwjgl-platform:2.9.2-nightly-20140822", OsxNatives("natives-osx"), WithOsx("natives-osx"), Allow("osx")),
        Lib("net.java.jinput:jinput-platform:2.0.5", OsxNatives("natives-osx"), WithOsx("natives-osx")),
        Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.13.2:LWJGL 3.1.6,用 <c>natives-macos</c>。</summary>
    internal static PyDict V1132() => Version(
        Lib("org.lwjgl:lwjgl:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-jemalloc:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-openal:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-opengl:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-glfw:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("org.lwjgl:lwjgl-stb:3.1.6", OsxNatives("natives-macos"), WithOsx("natives-macos")),
        Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.16.5:3.2.1 给 osx,3.2.2 给 linux/windows 且 disallow osx。</summary>
    internal static PyDict V1165() => Version(
        Lib("org.lwjgl:lwjgl:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl:3.2.2", NonMacNatives(), new[] { "natives-linux", "natives-windows" }, AllowAll(), Disallow("osx")),
        Lib("org.lwjgl:lwjgl-jemalloc:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl-glfw:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("com.mojang:text2speech:1.11.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>1.16.5 形态,但 mac natives jar 指定了具体路径(供「要不要解压」这类查文件的用例使用)。</summary>
    internal static PyDict V1165WithJars() => Version(
        Lib("org.lwjgl:lwjgl:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, new[] { LwjglNativesJar }, Allow("osx")),
        Lib("org.lwjgl:lwjgl:3.2.2", NonMacNatives(), new[] { "natives-linux", "natives-windows" }, AllowAll(), Disallow("osx")),
        Lib("com.mojang:text2speech:1.11.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, new[] { ObjcBridgeJar }, Allow("osx")));

    /// <summary>1.18.2:与 1.16.5 同形。</summary>
    internal static PyDict V1182() => Version(
        Lib("org.lwjgl:lwjgl:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("org.lwjgl:lwjgl:3.2.2", NonMacNatives(), new[] { "natives-linux", "natives-windows" }, AllowAll(), Disallow("osx")),
        Lib("org.lwjgl:lwjgl-stb:3.2.1", Dict("osx", "natives-macos"), new[] { "natives-macos" }, Allow("osx")),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    private static PyDict ModernMacLib(string groupArtifactVersion, string classifier)
    {
        return Lib(groupArtifactVersion + ":" + classifier, null, null, Allow("osx"));
    }

    private static PyDict VersionWithModernPairs(string version, params string[] groupArtifacts)
    {
        List<object> list = new List<object>();
        foreach (string text in groupArtifacts)
        {
            list.Add(ModernMacLib(text + ":" + version, "natives-macos"));
            list.Add(ModernMacLib(text + ":" + version, "natives-macos-arm64"));
        }
        return PyDict.Of("libraries", list);
    }

    /// <summary>1.19.4:7 个原生库,每个都有 macos 与 macos-arm64 两条(新式写法,没有 <c>natives</c> 字段)。</summary>
    internal static PyDict V1194() => VersionWithModernPairs(
        "3.3.1",
        "org.lwjgl:lwjgl-glfw",
        "org.lwjgl:lwjgl-jemalloc",
        "org.lwjgl:lwjgl-openal",
        "org.lwjgl:lwjgl-opengl",
        "org.lwjgl:lwjgl-stb",
        "org.lwjgl:lwjgl-tinyfd",
        "org.lwjgl:lwjgl");

    /// <summary>26.3(latest.release):12 个原生库,写法同 1.19.4。</summary>
    internal static PyDict V263() => VersionWithModernPairs(
        "3.4.3",
        "com.mojang:jtracy",
        "org.lwjgl:lwjgl-freetype",
        "org.lwjgl:lwjgl-jemalloc",
        "org.lwjgl:lwjgl-openal",
        "org.lwjgl:lwjgl-opengl",
        "org.lwjgl:lwjgl-sdl",
        "org.lwjgl:lwjgl-shaderc",
        "org.lwjgl:lwjgl-spvc",
        "org.lwjgl:lwjgl-stb",
        "org.lwjgl:lwjgl-vma",
        "org.lwjgl:lwjgl-vulkan",
        "org.lwjgl:lwjgl");

    /// <summary>合成夹具:一部分原生库有 arm64、一部分只有 x86_64(LWJGL 3.3 与 java-objc-bridge 混在一起)。</summary>
    internal static PyDict MixedIsolated() => Version(
        ModernMacLib("org.lwjgl:lwjgl-glfw:3.3.1", "natives-macos"),
        ModernMacLib("org.lwjgl:lwjgl-glfw:3.3.1", "natives-macos-arm64"),
        Lib("ca.weblite:java-objc-bridge:1.0.0", Dict("osx", "natives-osx"), new[] { "natives-osx" }, Allow("osx")));

    /// <summary>合成夹具:只有 windows / linux 的 natives。</summary>
    internal static PyDict NoMacNativesBuilder() => Version(
        Lib("com.mojang:text2speech:1.10.3", NonMacNatives(), new[] { "natives-linux", "natives-windows" }),
        Lib("org.lwjgl:lwjgl-glfw:3.3.1:natives-windows", null, null, Allow("windows")));

    /// <summary>合成夹具:mixed 「一半有 arm64」的整条继承链(子版本 + 1.16.5 形态的父版本)。</summary>
    internal static List<PyDict> ChainMixed()
    {
        return new List<PyDict>
        {
            Version(
                Lib("com.example:mod-with-arm64-natives:1.0", null, null, Allow("osx")),
                ModernMacLib("com.example:mod-with-arm64-natives:1.0", "natives-macos-arm64")),
            V1165()
        };
    }

    /// <summary>合成夹具:只含 1.19.4 形态的继承链(链上没有任何老式 natives 分类器)。</summary>
    internal static List<PyDict> ChainModern()
    {
        return new List<PyDict> { Version(Lib("com.example:mod:1.0", null, null, Allow("osx"))), V1194() };
    }

    /// <summary>
    /// 带一个「只有键、没有 path」的 classifiers 条目的库。
    /// 用来验证 <c>Installer.NativesJarRelpath</c> 的 maven 兜底分支 —— 那条路径只能由传入的 arch
    /// 替换 ${arch} 之后算出来,而「classifiers 里所有 path」那一段覆盖不到它。
    /// </summary>
    internal static PyDict LibWithPathlessClassifier(string name, PyDict natives, string classifierKey)
    {
        PyDict pyDict = PyDict.Of("name", name);
        pyDict["natives"] = natives;
        PyDict pyDict2 = new PyDict();
        pyDict2[classifierKey] = new PyDict();
        pyDict["downloads"] = PyDict.Of("classifiers", pyDict2);
        return pyDict;
    }

    /// <summary>8 个取证版本按 id 取夹具。</summary>
    internal static PyDict ById(string id)
    {
        return id switch
        {
            "1.7.10" => V1710(),
            "1.8.9" => V189(),
            "1.12.2" => V1122(),
            "1.13.2" => V1132(),
            "1.16.5" => V1165(),
            "1.18.2" => V1182(),
            "1.19.4" => V1194(),
            _ => V263(),
        };
    }

    /// <summary>8 个版本的 id(每个都有一行 5 种平台组合的断言)。</summary>
    internal static readonly string[] LiveIds = new[] { "1.7.10", "1.8.9", "1.12.2", "1.13.2", "1.16.5", "1.18.2", "1.19.4", "26.3" };

    /// <summary>Apple 芯片上会退到 x64 的 6 个版本(1.18.2 及更老)。</summary>
    internal static readonly string[] X64OnlyIds = new[] { "1.7.10", "1.8.9", "1.12.2", "1.13.2", "1.16.5", "1.18.2" };

    /// <summary>生产代码里的 <c>Cleaner.Low</c>:把 (libs 目录 + 相对路径) 规范化后转小写。</summary>
    internal static string Low(string libs, string rel)
    {
        return Py.Lower(new PPath(PPath.Join(libs, rel)).Resolve().ToString());
    }
}

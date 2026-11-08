// M1-A4：Core 复用层的最小自证测试（internal，随后由 tests/TipCL.Tests（M1-A5）引用或接手；本工程不引 xunit，
// 这里用内联断言，A5 接手时翻译成 [Fact]）。只覆盖纯逻辑数据类，不做网络/磁盘副作用。
using System;
using TipCL.Core.Launcher;

namespace TipCL.Core.Tests;

/// <summary>极简断言工具：失败抛异常，A5 迁移为 xunit 时逐条对应 [Fact]。</summary>
internal static class Check
{
	public static void True(bool cond, string msg = "")
	{
		if (!cond)
		{
			throw new Exception("断言失败: " + msg);
		}
	}

	public static void Equal(object a, object b, string msg = "")
	{
		if (!object.Equals(a, b))
		{
			throw new Exception($"断言失败: {a} != {b} {msg}");
		}
	}
}

internal static class CoreReuseSmokeTests
{
	/// <summary>PyJson 往返：解析→序列化→再解析 结果一致。</summary>
	public static void PyJson_Roundtrip_Object()
	{
		string src = "{\"a\":1,\"b\":[true,null,\"x\\ny\"],\"c\":{\"d\":2.5}}";
		object obj = PyJson.Loads(src);
		string back = PyJson.Dumps(obj);
		object again = PyJson.Loads(back);
		Check.Equal(Py.Str(((PyDict)obj).Req("a")), Py.Str(((PyDict)again).Req("a")));
	}

	/// <summary>默认配置：instances_dir 为 .minecraft，DEFAULT_CONFIG 可深拷贝。</summary>
	public static void Config_HasDefaults()
	{
		var cfg = (PyDict)Py.DeepCopy(Config.DEFAULT_CONFIG);
		Check.True(Py.Truthy(cfg));
		Check.Equal(".minecraft", Py.Str(cfg.Req("instances_dir")));
	}

	/// <summary>实例名归一：非法字符被替换、Windows 保留名被规避。</summary>
	public static void Instances_ResolveName_Sanitizes()
	{
		Check.Equal("a-b", Instances.ResolveName("a/b"));
		Check.True(!Instances.ResolveName("CON").Equals("CON", StringComparison.OrdinalIgnoreCase), "保留名须改写");
		Check.Equal("Survival 1.20", Instances.ResolveName("Survival 1.20"));
	}

	/// <summary>Installer 版本号解析：1.20.1 → [1,20,1]。</summary>
	public static void Installer_ParsesMavenVersions()
	{
		long[] v = Installer._mc_tuple("1.20.1");
		Check.True(v.Length >= 3 && v[0] == 1L && v[1] == 20L && v[2] == 1L);
	}

	/// <summary>Java 探测：平台键非空（跨平台分支已编译进二进制）。</summary>
	public static void Java_PlatformKey_NotEmpty()
	{
		Check.True(!string.IsNullOrEmpty(Java.PLATFORM_KEY));
	}

	/// <summary>入口：全部自证用例顺序执行，失败即抛。</summary>
	public static string RunAll()
	{
		PyJson_Roundtrip_Object();
		Config_HasDefaults();
		Instances_ResolveName_Sanitizes();
		Installer_ParsesMavenVersions();
		Java_PlatformKey_NotEmpty();
		return "CoreReuseSmokeTests: 5/5 passed";
	}
}

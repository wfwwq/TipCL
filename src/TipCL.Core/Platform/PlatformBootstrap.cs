using System;

namespace TipCL.Core.Platform;

/// <summary>
/// 平台服务接线(桌面启动与 <c>--smoke</c> 都先调 <see cref="Init()"/>)。
///
/// 这个方法<b>只给 <see cref="PlatformServices"/> 的两个静态属性赋值</b>:
/// 不做任何 I/O、不起进程、不读写数据目录。所有代价与副作用都留到真正用到编解码器 / 凭据库的时候。
///
/// 放在 <c>TipCL.Core</c> 而不是 <c>TipCL.App</c>:平台判定是一个纯函数,测试工程只引用 Core,
/// 这样单测不需要把 Avalonia 的界面初始化拉进测试进程(见 PROGRESS.md 的 B1a 接线一节)。
/// </summary>
public static class PlatformBootstrap
{
	/// <summary>按当前操作系统接线。</summary>
	public static void Init()
	{
		Init(OperatingSystem.IsMacOS());
	}

	/// <summary>按给定的平台判定接线(可注入,便于单测)。</summary>
	public static void Init(bool isMacOs)
	{
		PlatformServices.ImageCodec = CreateImageCodec();
		PlatformServices.SecretStore = CreateSecretStore(isMacOs);
	}

	/// <summary>图片编解码所有平台都接 Skia(WebP 也走 Skia)。</summary>
	public static IImageCodec CreateImageCodec()
	{
		return new SkiaImageCodec();
	}

	/// <summary>
	/// 凭据库只有 mac 接 Keychain(走 <c>/usr/bin/security</c>);
	/// 其它平台保持 null —— Windows 版是 WPF 的 <c>WinCredSecretStore</c>,mac 线不许碰 Windows 凭据管理器。
	/// </summary>
	public static ISecretStore CreateSecretStore(bool isMacOs)
	{
		if (!isMacOs)
		{
			return null;
		}
		return new MacKeychainSecretStore();
	}
}

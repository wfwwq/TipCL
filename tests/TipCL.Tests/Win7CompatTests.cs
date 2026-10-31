using System;
using System.IO;
using System.Linq;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

// Win7 兼容回归测试：Win7/8.1 的 WIC 没有 WebP 解码器，模组/整合包封面大多是 WebP。
public class Win7CompatTests
{
	const string LossyWebp = "UklGRl4AAABXRUJQVlA4WAoAAAAQAAAABwAABwAAQUxQSAoAAAABB1DAiAhERP8DVlA4IC4AAACwAQCdASoIAAgAAUAmJaACdLoABDAAAP7x3I/4DdfFtMv/vYL/3YL/3YL/WwAA";
	const string LosslessWebp = "UklGRh4AAABXRUJQVlA4TBEAAAAvB8ABEAdQjyLXo4CBiOh/AAA=";
	static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

	[Theory]
	[InlineData(LossyWebp)]
	[InlineData(LosslessWebp)]
	public void WebpConvertsToPngWithoutSystemCodec(string b64)
	{
		byte[] webp = Convert.FromBase64String(b64);
		Assert.True(WebpFallback.IsWebp(webp));
		byte[] png = WebpFallback.ToPng(webp);
		Assert.Equal(PngSig, png.Take(8).ToArray());
	}

	[Fact]
	public void NonWebpIsPassedThrough()
	{
		byte[] data = { 1, 2, 3 };
		Assert.False(WebpFallback.IsWebp(data));
		Assert.Same(data, WebpFallback.ToPng(data));
	}

	[Fact]
	public void DecodeRetriesWithPngWhenSystemDecoderFails()
	{
		byte[] webp = Convert.FromBase64String(LossyWebp);
		// 模拟 Win7：系统解码器遇到 WebP 就抛 NotSupportedException
		byte[] got = WebpFallback.Decode(webp, b => WebpFallback.IsWebp(b) ? throw new NotSupportedException("no WIC codec") : b);
		Assert.Equal(PngSig, got.Take(8).ToArray());
	}

	// 防止复发：新写的图片解码（new BitmapImage）必须走 WebpFallback；
	// 只解码内置 PNG 的地方列在白名单里。
	[Fact]
	public void EveryBitmapDecodeGoesThroughWebpFallback()
	{
		string[] pngOnly = { "Lucide.cs", "SkinPng.cs" };
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !File.Exists(Path.Combine(d.FullName, "TipCL.sln"))) d = d.Parent;
		Assert.NotNull(d);
		var offenders = Directory.EnumerateFiles(Path.Combine(d.FullName, "src", "TipCL"), "*.cs", SearchOption.AllDirectories)
			.Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
			.Where(p => !pngOnly.Contains(Path.GetFileName(p)))
			.Where(p => { string t = File.ReadAllText(p); return (t.Contains("new BitmapImage(") || t.Contains("BitmapDecoder.Create(")) && !t.Contains("WebpFallback"); })
			.Select(Path.GetFileName).ToList();
		Assert.True(offenders.Count == 0, "这些文件直接用 WIC 解码图片，Win7 上 WebP 会失败，请改用 WebpFallback.Decode：" + string.Join(", ", offenders));
	}
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SixLabors.ImageSharp;
using TipCL.Services;
using Xunit;

// ImageSharp 2.1.13 审计豁免的回归守卫（docs/security/imagesharp-audit.md / DECISIONS D-20）：
// 豁免前提是「全仓库只经 WebpOnly 配置解码 WebP」。一旦有人把入口扩成默认 Configuration
// （注册 TIFF/JPEG 等全套格式），TIFF/ICC 漏洞路径就变得可达——本测试会立刻失败。
public class WebpFallbackTests
{
	[Fact]
	public void DecoderRegistersOnlyWebp()
	{
		FieldInfo? fld = typeof(WebpFallback).GetField("WebpOnly", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(fld);
		var cfg = (Configuration)fld!.GetValue(null)!;
		string[] formats = cfg.ImageFormatsManager.ImageFormats.Select(f => f.Name).ToArray();
		Assert.Equal(new[] { "Webp" }, formats);
	}

	[Fact]
	public void NonWebpBytesPassThroughUnchanged()
	{
		byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
		Assert.Same(png, WebpFallback.ToPng(png));
		Assert.False(WebpFallback.IsWebp(png));
		Assert.False(WebpFallback.IsWebp(null));
		Assert.False(WebpFallback.IsWebp(new byte[4]));
	}

	[Fact]
	public void CorruptWebpFallsBackToOriginal()
	{
		// 合法 RIFF....WEBP 头 + 垃圾体：解码必失败，契约是原样返回而不是抛异常
		byte[] bad = new byte[64];
		byte[] header = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
		Array.Copy(header, bad, header.Length);
		Assert.True(WebpFallback.IsWebp(bad));
		Assert.Same(bad, WebpFallback.ToPng(bad));
	}

	[Fact]
	public void RealWebpDecodesToPng()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "webp-sample.webp");
		byte[] webp = File.ReadAllBytes(path);
		Assert.True(WebpFallback.IsWebp(webp));
		byte[] png = WebpFallback.ToPng(webp);
		Assert.NotSame(webp, png);
		Assert.True(png.Length > 8);
		// PNG 签名
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
	}
}

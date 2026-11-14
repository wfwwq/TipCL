using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace TipCL.Services;

// WebView2Loader.dll 是原生库，Costura 打不进托管程序集里。
// 做法：csproj 把 NuGet 包里三个架构的 WebView2Loader.dll 作为内嵌资源（TipCL.WebView2Loader.<arch>.dll），
// 第一次用 WebView 前按当前进程架构释放到 %TIPCL_HOME%/runtime/webview2/loader/<arch>/，
// 再调用 CoreWebView2Environment.SetLoaderDllFolderPath 指过去。这样单 exe 分发也能用 WebView2。
public static partial class WebViewHost
{
	private static bool _loaderReady;

	private static string? _loaderError;

	internal static string ArchName()
	{
		try
		{
			switch (RuntimeInformation.ProcessArchitecture)
			{
			case Architecture.Arm64:
				return "arm64";
			case Architecture.X86:
				return "x86";
			case Architecture.X64:
				return "x64";
			}
		}
		catch
		{
		}
		return Environment.Is64BitProcess ? "x64" : "x86";
	}

	/// <summary>释放并登记 WebView2Loader.dll。必须在任何其它 WebView2 API 之前调用；只做一次。</summary>
	internal static void EnsureLoader()
	{
		if (_loaderReady)
		{
			return;
		}
		_loaderReady = true;
		try
		{
			string arch = ArchName();
			string name = "TipCL.WebView2Loader." + arch + ".dll";
			using Stream? res = typeof(WebViewHost).Assembly.GetManifestResourceStream(name);
			if (res == null)
			{
				// 开发版（没有内嵌资源）：让 WebView2 自己在 exe 旁边的 runtimes\win-<arch>\native 里找
				return;
			}
			string dir = Path.Combine(RuntimeDir, "loader", arch);
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "WebView2Loader.dll");
			byte[] bytes;
			using (MemoryStream ms = new MemoryStream())
			{
				res.CopyTo(ms);
				bytes = ms.ToArray();
			}
			bool same = false;
			try
			{
				same = File.Exists(path) && new FileInfo(path).Length == bytes.Length;
			}
			catch
			{
			}
			if (!same)
			{
				// 旧文件可能被另一个 TipCL 进程占用：写临时文件再替换，失败就沿用旧的
				string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
				File.WriteAllBytes(tmp, bytes);
				try
				{
					if (File.Exists(path))
					{
						File.Delete(path);
					}
					File.Move(tmp, path);
				}
				catch
				{
					try
					{
						File.Delete(tmp);
					}
					catch
					{
					}
				}
			}
			CoreWebView2Environment.SetLoaderDllFolderPath(dir);
		}
		catch (Exception ex)
		{
			_loaderError = ex.Message;
		}
	}
}

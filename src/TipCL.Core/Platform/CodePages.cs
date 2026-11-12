using System.Text;

namespace TipCL.Core.Platform;

/// <summary>
/// 代码页注册(B1b)。
///
/// net8.0 默认只带 UTF-8 / UTF-16 / ASCII 等少数编码,`Encoding.GetEncoding(437)`(CP437)、
/// `936`(GBK)、`54936`(GB18030)都会抛 ArgumentException;Windows 版是 net48,这些代码页本来就有,
/// 所以「取不到代码页」是移植引入的行为差异。而源码里取代码页的地方
/// (<c>Utils.Cp437</c>、<c>Java.Gbk</c>、<c>Crash.ReadText</c>、<c>NetResponse.Text</c>、
/// <c>Terracotta.GbkBytes</c>)都带 catch 兜底 —— 不注册就会被静默降级成 UTF-8 / ASCII,
/// 中文与 ZIP 里的 CP437 条目名从此乱码(见 PROGRESS.md 的 A3「运行时差异清点」E1–E5)。
///
/// <see cref="Encoding.RegisterProvider"/> 是**纯内存操作**:不读磁盘、不联网、不起进程;
/// 本方法幂等,可以重复调用。
/// </summary>
public static class CodePages
{
	private static readonly object _gate = new object();

	private static bool _registered;

	/// <summary>注册代码页提供程序(只注册一次,之后调用直接返回)。</summary>
	public static void EnsureRegistered()
	{
		if (_registered)
		{
			return;
		}
		lock (_gate)
		{
			if (_registered)
			{
				return;
			}
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			// 最后才置位:先完成注册再公开状态,别的线程不会看到「已注册」但实际没注册。
			_registered = true;
		}
	}
}

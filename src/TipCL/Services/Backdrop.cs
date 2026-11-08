using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TipCL.Services;

public static class Backdrop
{
	private enum AccentState
	{
		Disabled = 0,
		EnableBlurBehind = 3,
		EnableAcrylicBlurBehind = 4
	}

	private struct Margins
	{
		public int Left;

		public int Right;

		public int Top;

		public int Bottom;
	}

	private struct AccentPolicy
	{
		public AccentState AccentState;

		public int AccentFlags;

		public int GradientColor;

		public int AnimationId;
	}

	private struct WindowCompositionAttributeData
	{
		public int Attribute;

		public IntPtr Data;

		public int SizeOfData;
	}

	private const int DwmwaUseImmersiveDarkMode = 20;

	private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

	private const int DwmwaSystemBackdropType = 38;

	private const int DwmwaWindowCornerPreference = 33;

	// WP-S12：DWM 边框色；恒定 0x00262626 深灰（DWMWA_COLOR_DEFAULT 在 Win11 浅色主题下是白边）
	private const int DwmwaBorderColor = 34;

	private const int DwmwcpRound = 2;

	/// <summary>Win11 (build ≥ 22000)：系统级圆角窗口；调用失败（旧系统/无 DWM）返回 false 由调用方回退自绘。</summary>
	public static bool TryRoundWin11(IntPtr hwnd)
	{
		try
		{
			int value = DwmwcpRound;
			return DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref value, 4) == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>窗口圆角（设备无关像素）：Win11 返回 0（DWM 系统圆角）；Win10 返回 8（MainWindow 用 SetWindowRgn 裁 HWND + WPF Clip）；Win7 直角。</summary>
	public static int RootCornerRadius
	{
		get
		{
			try
			{
				Version v = Environment.OSVersion.Version;
				if (v.Build >= 22000)
				{
					return 0;
				}
				if (v.Build >= 10240)
				{
					return 8;
				}
				return 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("user32.dll")]
	private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr h);

	/// <summary>Win10：亚克力（ApplyLegacyBlur）铺满整个 HWND，WPF 侧的 Clip 挡不住四角，
	/// 只能用窗口区域把 HWND 本身裁圆。w/h/r 为设备像素；r≤0 清除区域（最大化直角）。
	/// 成功后区域归系统所有，失败才自己删。</summary>
	public static void SetRoundRegion(IntPtr hwnd, int w, int h, int r)
	{
		try
		{
			IntPtr rgn = ((r > 0 && w > 0 && h > 0) ? CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2) : IntPtr.Zero);
			if (SetWindowRgn(hwnd, rgn, redraw: true) == 0 && rgn != IntPtr.Zero)
			{
				DeleteObject(rgn);
			}
		}
		catch
		{
		}
	}

	private static int _pmAccentState = 4;

	private static bool _pmLegacyUsed;

	[DllImport("dwmapi.dll")]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

	public static void Apply(Window? window)
	{
		if (window == null)
		{
			return;
		}
		try
		{
			IntPtr handle = new WindowInteropHelper(window).Handle;
			if (!(handle == IntPtr.Zero))
			{
				HwndSource hwndSource = HwndSource.FromHwnd(handle);
				if (hwndSource != null)
				{
					hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
				}
				Margins margins = new Margins
				{
					Left = -1,
					Right = -1,
					Top = -1,
					Bottom = -1
				};
				DwmExtendFrameIntoClientArea(handle, ref margins);
				int value = (App.IsDark ? 1 : 0);
				if (DwmSetWindowAttribute(handle, 20, ref value, 4) != 0)
				{
					DwmSetWindowAttribute(handle, 19, ref value, 4);
				}
				// WP-S12：有壁纸时关闭系统 backdrop（DWMSBT_NONE），壁纸本身就是背景，
				// 避免 Mica/亚克力在失焦时退成实色盖住壁纸；无壁纸用 Mica
				int value2 = (Wallpaper.Active ? 1 : 2);
				if (DwmSetWindowAttribute(handle, 38, ref value2, 4) != 0)
				{
					ApplyLegacyBlur(handle, App.IsDark);
				}
				// 边框色：恒定深灰细线。交还系统默认时 Win11 浅色主题会描一圈白边（2026-11-08 用户报告），
				// Win10 上调用失败无副作用
				int value3 = unchecked((int)2500134u);
				DwmSetWindowAttribute(handle, 34, ref value3, 4);
			}
		}
		catch (DllNotFoundException)
		{
		}
		catch (EntryPointNotFoundException)
		{
		}
		catch
		{
		}
	}

	public static void Refresh(Window? window)
	{
		Apply(window);
	}

	private static void ApplyLegacyBlur(IntPtr hwnd, bool dark)
	{
		_pmLegacyUsed = true;
		try
		{
			Color c = (dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243));
			AccentPolicy structure = new AccentPolicy
			{
				AccentState = (AccentState)_pmAccentState,
				AccentFlags = 2,
				GradientColor = PackAbgr(c, 176)
			};
			int num = Marshal.SizeOf<AccentPolicy>();
			IntPtr intPtr = Marshal.AllocHGlobal(num);
			try
			{
				Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
				WindowCompositionAttributeData data = new WindowCompositionAttributeData
				{
					Attribute = 19,
					Data = intPtr,
					SizeOfData = num
				};
				if (!SetWindowCompositionAttribute(hwnd, ref data))
				{
					structure.AccentState = AccentState.EnableBlurBehind;
					Marshal.StructureToPtr(structure, intPtr, fDeleteOld: false);
					SetWindowCompositionAttribute(hwnd, ref data);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}
		catch
		{
		}
	}

	private static int PackAbgr(Color c, byte alpha)
	{
		return (alpha << 24) | (c.B << 16) | (c.G << 8) | c.R;
	}

	public static void PmSetMoving(IntPtr hwnd, bool moving)
	{
		Wallpaper.SetWindowActive(!moving);
		if (_pmLegacyUsed)
		{
			_pmAccentState = (moving ? 3 : 4);
			ApplyLegacyBlur(hwnd, App.IsDark);
		}
	}
}

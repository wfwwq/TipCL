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
				int value2 = 3;
				if (DwmSetWindowAttribute(handle, 38, ref value2, 4) != 0)
				{
					ApplyLegacyBlur(handle, App.IsDark);
				}
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

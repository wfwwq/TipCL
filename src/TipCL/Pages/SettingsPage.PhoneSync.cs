using System;
using System.Windows.Controls;
using System.Windows.Threading;
using TipCL.Core.Launcher.Sync;
using TipCL.Services;

namespace TipCL.Pages;

public sealed partial class SettingsPage
{
	/// <summary>设置页「手机同步」卡片：开关、配对码、本机地址、清除已配对设备。</summary>
	private Border BuildPhoneSyncCard()
	{
		TextBlock status = Ui.Muted("");
		status.TextWrapping = System.Windows.TextWrapping.Wrap;
		CheckBox sw = null;
		void Refresh()
		{
			if (SyncServer.Running)
			{
				int n = SyncServer.Devices().Count;
				status.Text = I18n.L("配对码") + " " + SyncServer.PairCode + "　·　" + string.Join("  ", SyncServer.Addresses()) + "　·　" + I18n.L("已配对设备") + " " + n;
			}
			else
			{
				status.Text = string.IsNullOrEmpty(SyncServer.LastError) ? I18n.L("未开启") : I18n.L("启动失败：") + SyncServer.LastError;
			}
		}
		sw = Ui.Switch(SyncServer.Enabled && SyncServer.Running, delegate
		{
			bool on = sw.IsChecked == true;
			if (on && !SyncServer.SetEnabled(true))
			{
				sw.IsChecked = false;
			}
			else if (!on)
			{
				SyncServer.SetEnabled(false);
			}
			Refresh();
		});
		Button code = Ui.Btn(I18n.L("换一个配对码"), BtnKind.Chip, delegate
		{
			if (SyncServer.Running)
			{
				SyncServer.NewCode();
			}
			Refresh();
		});
		Button forget = Ui.Btn(I18n.L("清除已配对设备"), BtnKind.Ghost, delegate
		{
			SyncServer.ForgetDevices();
			Refresh();
		});
		// 配对成功后配对码会自动更换，定时刷新一下显示
		DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.0) };
		timer.Tick += delegate
		{
			Refresh();
		};
		base.Loaded += delegate
		{
			timer.Start();
		};
		base.Unloaded += delegate
		{
			timer.Stop();
		};
		Refresh();
		return Ui.Card(Ui.V(2.0, Ui.Section(I18n.L("手机同步"), I18n.L("在同一个 Wi-Fi 下和手机启动器互传存档")), Ui.Field(I18n.L("局域网同步"), Ui.H(8.0, sw, code.VCenter(), forget.VCenter()), I18n.L("打开后手机端会自动发现这台电脑，输入配对码即可；覆盖前都会先自动备份")), Ui.Field("", status)));
	}
}

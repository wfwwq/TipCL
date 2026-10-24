using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

public sealed class LaunchPage : PageBase
{
	private readonly ComboBox _inst = Ui.Combo();

	private readonly ComboBox _ver = Ui.Combo();

	private readonly ComboBox _acc = Ui.Combo();

	private readonly ComboBox _java = Ui.Combo();

	private readonly TextBox _user = Ui.Input("Player");

	private readonly TextBox _server = Ui.Input(I18n.L("直连服务器 ip:port（可空）"));

	private readonly TextBox _wBox = Ui.Input("854", "", 74.0);

	private readonly TextBox _hBox = Ui.Input("480", "", 74.0);

	private readonly Slider _mem = Ui.Sld(1024.0, 32768.0, 4096.0, 256.0);

	private readonly TextBlock _memLbl = Ui.Txt("4096 MB", 12.0, bold: true);

	private readonly Button _launchBtn = Ui.Btn(I18n.L("启动游戏"), BtnKind.Primary, null, "\ue768");

	private readonly Button _stopBtn = Ui.Btn(I18n.L("停止"), BtnKind.Danger, null, "\ue71a");

	private readonly ProgressBar _prog = Ui.Prog();

	private readonly TextBlock _status = Ui.Small(I18n.L("就绪"));

	private int _progGen;

	private readonly TextBox _log = Ui.LogBox();

	private readonly TextBlock _bTitle = new TextBlock
	{
		FontSize = 27.0,
		FontWeight = FontWeights.Bold
	};

	private readonly TextBlock _bSub = new TextBlock
	{
		FontSize = 12.5,
		Opacity = 0.82
	};

	private readonly SPanel _newsHost = Ui.V(6.0);

	private readonly SPanel _taskHost = Ui.V(6.0);

	private readonly TextBlock _playTotal = new TextBlock
	{
		FontSize = 26.0,
		FontWeight = FontWeights.Bold
	};

	private readonly TextBox _notes = Ui.Multi(I18n.L("随手记点什么…"));

	private readonly DashHost _dash = new DashHost();

	private readonly List<string> _logLines = new List<string>();

	private List<JavaOption> _javaOpts = new List<JavaOption>();

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private LayoutState _layoutState = new LayoutState();

	private readonly DispatcherTimer _layoutSaveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(300.0)
	};

	private string? _taskId;

	private string? _loginTask;

	private bool _syncing;

	private bool _crashShown;

	private bool _editMode;

	private TextBlock? _loginHint;

	private TextBlock? _loginCode;

	private string _loginUri = "";

	private Dlg.Layer? _loginLayer;

	private string? _preferredInstance;

	private string? _preferredVersion;

	private readonly DispatcherTimer _memSaveTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(650.0)
	};

	private static readonly (string Key, string Title)[] CardTypes = new(string, string)[8]
	{
		("banner", I18n.L("启动横幅")),
		("config", I18n.L("启动配置")),
		("log", I18n.L("实时日志")),
		("news", I18n.L("新闻主页")),
		("quick", I18n.L("快捷入口")),
		("notes", I18n.L("便签")),
		("playtime", I18n.L("游戏时长")),
		("tasks", I18n.L("任务摘要"))
	};

	private static readonly (string Key, string Glyph, string Label)[] QuickTargets = new(string, string, string)[12]
	{
		("version", "\ue7b8", I18n.L("下载游戏")),
		("mod", "\uea86", I18n.L("装模组")),
		("modpack", "\ue7b8", I18n.L("整合包")),
		("instance", "\ue80a", I18n.L("实例")),
		("account", "\ue77b", I18n.L("账号")),
		("ai", "\ue99a", I18n.L("问 AI")),
		("multiplayer", "\ue701", I18n.L("联机")),
		("settings", "\ue713", I18n.L("设置")),
		("tasks", "\ue896", I18n.L("下载任务")),
		("saves", "\ue74e", I18n.L("存档管理")),
		("folder", "\ue8b7", I18n.L("实例目录")),
		("playtime", "\ue823", I18n.L("游戏时长"))
	};

	private static readonly string[] QuickDefault = new string[6] { "mod", "modpack", "saves", "multiplayer", "ai", "playtime" };

	private readonly SPanel _quickHost = Ui.V(0.0);

	private bool _layoutLoaded;

	private int _layoutGen;

	private bool _layoutReady;

	private Task _saveInFlight = Task.CompletedTask;

	private const int MaxLines = 2500;

	public override string Title => I18n.L("启动");

	private async void HideProgressSoon(int delayMs)
	{
		int gen = _progGen;
		if (delayMs > 0)
		{
			await Task.Delay(delayMs);
		}
		if (gen == _progGen)
		{
			_prog.Visibility = Visibility.Collapsed;
		}
	}

	public LaunchPage()
	{
		_bTitle.SetResourceReference(TextBlock.ForegroundProperty, "B.OnBanner");
		_bSub.SetResourceReference(TextBlock.ForegroundProperty, "B.OnBanner");
		_dash.ContentFactory = BuildCard;
		_dash.TitleFactory = (string t) => CardTypes.FirstOrDefault(((string Key, string Title) c) => c.Key == t).Title ?? t;
		_dash.Changed += PersistLayout;
		_layoutSaveTimer.Tick += delegate
		{
			_layoutSaveTimer.Stop();
			Task pending = (_saveInFlight = SaveLayoutNowAsync());
			PageBase.Run(() => pending, I18n.L("布局未能保存"));
		};
		Grid grid = Ui.G("Auto,*");
		grid.Add(BuildToolbar(), 0);
		grid.Add(new Border
		{
			Child = _dash,
			Margin = new Thickness(18.0, 4.0, 18.0, 16.0)
		}, 1);
		base.Content = grid;
		_stopBtn.IsEnabled = false;
		_launchBtn.Click += delegate
		{
			PageBase.Run(LaunchAsync, I18n.L("启动失败"));
		};
		_stopBtn.Click += delegate
		{
			PageBase.Run(StopAsync);
		};
		_inst.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(OnInstanceChanged);
			}
		};
		_ver.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				SyncBanner();
			}
		};
		_java.SelectionChanged += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(SaveJavaAsync);
			}
		};
		_mem.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_memLbl.Text = $"{(int)e.NewValue} MB";
			if (!_syncing)
			{
				_memSaveTimer.Stop();
				_memSaveTimer.Start();
			}
		};
		_memSaveTimer.Tick += delegate
		{
			_memSaveTimer.Stop();
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_wBox.LostFocus += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_hBox.LostFocus += delegate
		{
			if (!_syncing)
			{
				PageBase.Run(SaveDisplayPrefsAsync);
			}
		};
		_notes.LostFocus += delegate
		{
			if (PageBase.Win != null)
			{
				PageBase.Win.Prefs.Notes = _notes.Text;
				PageBase.Win.SaveUiPrefs();
			}
		};
	}

	private UIElement BuildToolbar()
	{
		Button edit = Ui.Btn(I18n.L("编辑布局"), BtnKind.Chip, null, "\ue70f");
		Button add = Ui.Btn(I18n.L("添加卡片"), BtnKind.Chip, null, "\ue710");
		Button snap = Ui.Btn(I18n.L("网格吸附"), BtnKind.Chip, null, "\ue80a");
		Button scheme = Ui.Btn(I18n.L("方案"), BtnKind.Chip, null, "\ue8fd");
		Button io = Ui.Btn(I18n.L("导入 / 导出"), BtnKind.Chip, null, "\uede1");
		Button button = Ui.Btn(I18n.L("恢复默认"), BtnKind.Chip, null, "\ue72c");
		TextBlock hint = Ui.Small(I18n.L("拖动卡片移动，边角八向缩放；布局按比例自适应窗口"));
		hint.Visibility = Visibility.Collapsed;
		edit.Click += delegate
		{
			_editMode = !_editMode;
			_dash.EditMode = _editMode;
			edit.Content = Ui.H(6.0, Ui.Glyph(_editMode ? "\ue73e" : "\ue70f", 12.5), Ui.Txt(_editMode ? I18n.L("完成编辑") : I18n.L("编辑布局")));
			hint.Visibility = ((!_editMode) ? Visibility.Collapsed : Visibility.Visible);
			Button button3 = add;
			bool flag3 = (snap.IsEnabled = _editMode);
			bool isEnabled2 = flag3;
			button3.IsEnabled = isEnabled2;
		};
		Button button2 = add;
		bool flag = (snap.IsEnabled = false);
		bool isEnabled = flag;
		button2.IsEnabled = isEnabled;
		add.Click += delegate
		{
			ContextMenu contextMenu = new ContextMenu();
			(string, string)[] cardTypes = CardTypes;
			for (int i = 0; i < cardTypes.Length; i++)
			{
				var (key, header) = cardTypes[i];
				if (!_dash.Has(key))
				{
					MenuItem menuItem = new MenuItem
					{
						Header = header
					};
					menuItem.Click += delegate
					{
						_dash.AddCard(key);
						PageBase.Run(FillCardData);
					};
					contextMenu.Items.Add(menuItem);
				}
			}
			if (contextMenu.Items.Count == 0)
			{
				contextMenu.Items.Add(new MenuItem
				{
					Header = I18n.L("卡片都在画布上了"),
					IsEnabled = false
				});
			}
			contextMenu.PlacementTarget = add;
			contextMenu.IsOpen = true;
		};
		snap.Click += delegate
		{
			DashLayout layout = _dash.Layout;
			layout.Grid = ((!layout.Snap) ? 8 : 0);
			_dash.EditMode = false;
			_dash.EditMode = _editMode;
			PersistLayout();
			PageBase.Toast(I18n.L("网格吸附"), layout.Snap ? I18n.L("已开启（{0}px）", layout.Grid) : I18n.L("已关闭"));
		};
		scheme.Click += delegate
		{
			ShowSchemeMenu(scheme);
		};
		io.Click += delegate
		{
			ShowIoMenu(io);
		};
		button.Click += delegate
		{
			PageBase.Run(async delegate
			{
				string body = (string.IsNullOrEmpty(_layoutState.Profile) ? I18n.L("回到内置默认布局。") : I18n.L("退出方案「{0}」，回到内置默认布局（方案本身保留）。", _layoutState.Profile));
				if (await Dlg.Confirm(I18n.L("恢复默认布局"), body))
				{
					await FlushLayoutAsync();
					JsonElement state = await PageBase.Api.CallAsync("reset_layout");
					ApplyLayoutState(state);
					await FillCardData();
				}
			});
		};
		SPanel sPanel = Ui.H(8.0, edit, add, snap, scheme, io, button, hint);
		sPanel.Margin = new Thickness(18.0, 14.0, 18.0, 0.0);
		foreach (UIElement child in sPanel.Children)
		{
			child.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		}
		return sPanel;
	}

	private void ShowSchemeMenu(FrameworkElement anchor)
	{
		ContextMenu contextMenu = new ContextMenu();
		string active = _layoutState.Profile;
		MenuItem menuItem = new MenuItem
		{
			Header = ((active.Length == 0) ? "● " : I18n.L("\u3000")) + I18n.L("默认布局")
		};
		menuItem.Click += delegate
		{
			PageBase.Run(() => SwitchProfileAsync(""));
		};
		contextMenu.Items.Add(menuItem);
		foreach (string name in _layoutState.Profiles)
		{
			MenuItem menuItem2 = new MenuItem
			{
				Header = ((name == active) ? "● " : I18n.L("\u3000")) + name
			};
			menuItem2.Click += delegate
			{
				PageBase.Run(() => SwitchProfileAsync(name));
			};
			contextMenu.Items.Add(menuItem2);
		}
		contextMenu.Items.Add(new Separator());
		MenuItem menuItem3 = new MenuItem
		{
			Header = I18n.L("把当前布局存为方案…")
		};
		menuItem3.Click += delegate
		{
			PageBase.Run(async delegate
			{
				string name3 = (await Dlg.Prompt(I18n.L("新建布局方案"), I18n.L("方案名称"), I18n.L("方案 {0}", _layoutState.Profiles.Count + 1)))?.Trim();
				if (!string.IsNullOrEmpty(name3))
				{
					bool flag = _layoutState.Profiles.Contains(name3);
					if (flag)
					{
						flag = !(await Dlg.Confirm(I18n.L("覆盖方案"), I18n.L("已有同名方案「{0}」，覆盖它？", name3), I18n.L("覆盖"), I18n.L("取消"), danger: true));
					}
					if (!flag)
					{
						await FlushLayoutAsync();
						JsonElement state = await PageBase.Api.CallAsync("save_layout_profile", new
						{
							name = name3,
							doc = _dash.Layout.ToDict()
						});
						ApplyLayoutState(state);
						await FillCardData();
						PageBase.Toast(I18n.L("已保存方案"), name3, ToastKind.Success);
					}
				}
			});
		};
		contextMenu.Items.Add(menuItem3);
		MenuItem menuItem4 = new MenuItem
		{
			Header = I18n.L("重命名当前方案…"),
			IsEnabled = (active.Length > 0)
		};
		menuItem4.Click += delegate
		{
			PageBase.Run(async delegate
			{
				string name2 = (await Dlg.Prompt(I18n.L("重命名方案"), I18n.L("方案名称"), active))?.Trim();
				if (!string.IsNullOrEmpty(name2) && !(name2 == active))
				{
					await FlushLayoutAsync();
					await PageBase.Api.CallAsync("save_layout_profile", new
					{
						name = name2,
						doc = _dash.Layout.ToDict()
					});
					JsonElement state = await PageBase.Api.CallAsync("delete_layout_profile", new
					{
						name = active
					});
					ApplyLayoutState(state);
					if (_layoutState.Profile != name2)
					{
						state = await PageBase.Api.CallAsync("activate_layout_profile", new
						{
							name = name2
						});
						ApplyLayoutState(state);
					}
					await FillCardData();
				}
			});
		};
		contextMenu.Items.Add(menuItem4);
		MenuItem menuItem5 = new MenuItem
		{
			Header = I18n.L("删除当前方案"),
			IsEnabled = (active.Length > 0)
		};
		menuItem5.Click += delegate
		{
			PageBase.Run(async delegate
			{
				if (await Dlg.Confirm(I18n.L("删除方案"), I18n.L("删除「{0}」？画布会回到内置默认布局。", active), I18n.L("删除"), I18n.L("取消"), danger: true))
				{
					await FlushLayoutAsync();
					JsonElement state = await PageBase.Api.CallAsync("delete_layout_profile", new
					{
						name = active
					});
					ApplyLayoutState(state);
					await FillCardData();
				}
			});
		};
		contextMenu.Items.Add(menuItem5);
		contextMenu.PlacementTarget = anchor;
		contextMenu.IsOpen = true;
	}

	private async Task SwitchProfileAsync(string name)
	{
		if (!(name == _layoutState.Profile))
		{
			await FlushLayoutAsync();
			ApplyLayoutState(await PageBase.Api.CallAsync("activate_layout_profile", new { name }));
			await FillCardData();
		}
	}

	private void ShowIoMenu(FrameworkElement anchor)
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("导出布局 JSON…")
		};
		menuItem.Click += delegate
		{
			string text = Dlg.SaveFile(I18n.L("布局 (*.json)|*.json"), "tipcl-layout.json", I18n.L("导出布局"));
			if (text == null)
			{
				return;
			}
			try
			{
				File.WriteAllText(text, _dash.Layout.ToJson());
				PageBase.Toast(I18n.L("已导出"), text, ToastKind.Success);
			}
			catch (Exception ex)
			{
				PageBase.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
			}
		};
		contextMenu.Items.Add(menuItem);
		MenuItem menuItem2 = new MenuItem
		{
			Header = I18n.L("导入布局 JSON…")
		};
		menuItem2.Click += delegate
		{
			PageBase.Run(async delegate
			{
				string text = Dlg.PickFile(I18n.L("布局 (*.json)|*.json"), I18n.L("导入布局"));
				if (text != null)
				{
					JsonElement doc;
					try
					{
						using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(text));
						doc = jsonDocument.RootElement.Clone();
					}
					catch (Exception ex)
					{
						PageBase.Toast(I18n.L("导入失败"), I18n.L("不是有效的 JSON：") + ex.Message, ToastKind.Error);
						return;
					}
					await FlushLayoutAsync();
					ApplyLayoutState(await PageBase.Api.CallAsync("import_layout", new { doc }));
					await FillCardData();
					PageBase.Toast(I18n.L("已导入"), I18n.L("{0} 张卡片", _dash.Layout.Items.Count), ToastKind.Success);
				}
			}, I18n.L("导入失败"));
		};
		contextMenu.Items.Add(menuItem2);
		MenuItem menuItem3 = new MenuItem
		{
			Header = I18n.L("吸附网格")
		};
		int[] gridChoices = DashLayout.GridChoices;
		foreach (int step in gridChoices)
		{
			MenuItem menuItem4 = new MenuItem
			{
				Header = ((step == 0) ? I18n.L("自由") : $"{step}px"),
				IsCheckable = true,
				IsChecked = (_dash.Layout.Grid == step)
			};
			menuItem4.Click += delegate
			{
				_dash.Layout.Grid = step;
				_dash.EditMode = false;
				_dash.EditMode = _editMode;
				_dash.Rebuild();
				PageBase.Run(FillCardData);
				PersistLayout();
			};
			menuItem3.Items.Add(menuItem4);
		}
		contextMenu.Items.Add(menuItem3);
		contextMenu.PlacementTarget = anchor;
		contextMenu.IsOpen = true;
	}

	private void ApplyLayoutState(JsonElement state)
	{
		_layoutSaveTimer.Stop();
		_layoutGen++;
		_layoutReady = false;
		_layoutState = LayoutState.FromJson(state);
		_dash.MinSizes = _layoutState.MinSizes;
		_dash.Load(_layoutState.Doc);
		_layoutSaveTimer.Stop();
		_layoutReady = true;
	}

	private void PersistLayout()
	{
		if (_layoutReady)
		{
			_layoutSaveTimer.Stop();
			_layoutSaveTimer.Start();
		}
	}

	private async Task FlushLayoutAsync()
	{
		if (_layoutSaveTimer.IsEnabled)
		{
			_layoutSaveTimer.Stop();
			await (_saveInFlight = SaveLayoutNowAsync());
			return;
		}
		try
		{
			await _saveInFlight;
		}
		catch
		{
		}
	}

	private async Task SaveLayoutNowAsync()
	{
		if (!AppServices.Ready || !_layoutReady)
		{
			return;
		}
		int gen = _layoutGen;
		JsonElement jsonElement = await PageBase.Api.CallAsync("save_layout", new
		{
			doc = _dash.Layout.ToDict(),
			profile = _layoutState.Profile
		});
		if (gen == _layoutGen && jsonElement.ValueKind == JsonValueKind.Object)
		{
			JsonElement value2;
			if (jsonElement.TryGetProperty("saved", out var value) && value.ValueKind == JsonValueKind.False)
			{
				ApplyLayoutState(await PageBase.Api.CallAsync("get_layout"));
				await FillCardData();
				PageBase.Toast(I18n.L("布局方案已变更"), I18n.L("已同步为当前方案，未覆盖"), ToastKind.Warning);
			}
			else if (jsonElement.TryGetProperty("profile", out value2) && value2.ValueKind == JsonValueKind.String)
			{
				_layoutState.Profile = value2.GetString() ?? "";
			}
		}
	}

	private UIElement? BuildCard(string type)
	{
		return type switch
		{
			"banner" => BuildBanner(), 
			"config" => BuildConfig(), 
			"log" => BuildLog(), 
			"news" => Ui.Scroll(_newsHost), 
			"quick" => BuildQuick(), 
			"notes" => _notes, 
			"playtime" => Ui.V(4.0, _playTotal, Ui.Muted(I18n.L("累计游玩时间"))), 
			"tasks" => Ui.Scroll(_taskHost), 
			_ => null, 
		};
	}

	private UIElement BuildBanner()
	{
		Border border = new Border
		{
			Width = 180.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = new LinearGradientBrush(new GradientStopCollection
			{
				new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
				new GradientStop(Color.FromArgb(90, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.5),
				new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0)
			}, 0.0),
			IsHitTestVisible = false,
			Opacity = 0.0
		};
		Motion.Shine(border, new TranslateTransform(-200.0, 0.0));
		SPanel el = Ui.H(10.0, _launchBtn, _stopBtn);
		_launchBtn.Style = Ui.S("Btn.OnBanner");
		_launchBtn.Padding = new Thickness(26.0, 10.0, 26.0, 11.0);
		_launchBtn.Content = Ui.H(8.0, Ui.Glyph("\ue768", 15.0), Ui.Txt(I18n.L("启动游戏"), 15.0, bold: true));
		_stopBtn.Padding = new Thickness(16.0, 10.0, 16.0, 11.0);
		Motion.HoverLift(_launchBtn, 1.05, 1.0, 16.0);
		_prog.Height = 5.0;
		_prog.SetResourceReference(Control.ForegroundProperty, "B.OnBanner");
		_prog.Opacity = 0.92;
		_prog.Visibility = Visibility.Collapsed;
		SPanel el2 = Ui.V(4.0, _bTitle, _bSub);
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(el2.VCenter(), 0);
		grid.Add(el.VCenter(), 0, 1);
		SPanel sPanel = Ui.V(5.0, _prog, _status);
		_status.SetResourceReference(TextBlock.ForegroundProperty, "B.OnBanner");
		_status.Opacity = 0.84;
		SPanel sPanel2 = Ui.V(12.0, grid, sPanel);
		sPanel2.Margin = new Thickness(24.0, 18.0, 24.0, 18.0);
		Border border2 = new Border();
		border2.CornerRadius = new CornerRadius(12.0);
		border2.ClipToBounds = true;
		border2.SetResourceReference(Control.BackgroundProperty, "B.BannerFill");
		border2.Child = new Grid
		{
			Children = 
			{
				(UIElement)border,
				(UIElement)sPanel2
			}
		};
		return border2;
	}

	private UIElement BuildConfig()
	{
		SPanel control = Ui.H(6.0, _wBox, Ui.Txt("×").VCenter(), _hBox);
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(_mem.VCenter(), 0);
		grid.Add(_memLbl.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Button button = Ui.Btn(I18n.L("微软登录"), BtnKind.Soft, delegate
		{
			PageBase.Run(MicrosoftLoginAsync);
		}, "\ue77b");
		Button button2 = Ui.Btn(I18n.L("皮肤站"), BtnKind.Chip, delegate
		{
			PageBase.Run(AuthlibLoginAsync);
		});
		Button button3 = Ui.Btn(I18n.L("版本设置"), BtnKind.Chip, delegate
		{
			PageBase.Run(VersionSettingsAsync);
		}, "\ue713");
		Button button4 = Ui.Btn(I18n.L("刷新新闻"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadNewsAsync);
		}, "\ue72c");
		Button button5 = Ui.IconBtn("\ue712", I18n.L("更多操作"), delegate(object s, RoutedEventArgs _)
		{
			ShowMoreMenu((FrameworkElement)s);
		});
		return Ui.Scroll(Ui.V(9.0, Field(I18n.L("实例"), _inst), Field(I18n.L("版本"), _ver), Field(I18n.L("用户"), _acc), Field("Java", _java), Field(I18n.L("内存"), grid), Field(I18n.L("分辨率"), control), Field(I18n.L("直连"), _server), Ui.Sep().M(0.0, 4.0, 0.0, 2.0), Ui.H(7.0, button, button2, button3, button4, button5)));
	}

	private static UIElement Field(string label, UIElement control)
	{
		Grid grid = Ui.G(null, "58,*");
		grid.Add(Ui.Txt(label, 12.0, bold: false, "B.InkMuted").VCenter(), 0);
		control.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		grid.Add(control, 0, 1);
		return grid;
	}

	private void ShowMoreMenu(FrameworkElement anchor)
	{
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("查看启动命令"), ShowLaunchCommandAsync);
		Item(I18n.L("导出启动脚本 (.bat)"), ExportScriptAsync);
		Item(I18n.L("创建桌面快捷方式"), ShortcutAsync);
		Item(I18n.L("修复当前版本"), RepairAsync);
		Item(I18n.L("打开实例文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_folder", new
			{
				name = _inst.Str()
			});
		});
		menu.PlacementTarget = anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = head
			};
			menuItem.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(menuItem);
		}
	}

	private UIElement BuildLog()
	{
		SPanel el = Ui.H(6.0, Ui.Btn(I18n.L("复制启动命令"), BtnKind.Chip, delegate
		{
			PageBase.Run(ShowLaunchCommandAsync, I18n.L("生成失败"));
		}, "\ue8c8"), Ui.IconBtn("\ue8fd", I18n.L("复制日志"), delegate
		{
			try
			{
				Clipboard.SetText(_log.Text);
				PageBase.Toast(I18n.L("已复制"), I18n.L("日志已放进剪贴板"), ToastKind.Success);
			}
			catch
			{
			}
		}), Ui.IconBtn("\ue74d", I18n.L("清空"), delegate
		{
			_logLines.Clear();
			_log.Clear();
		})).Right();
		Grid grid = Ui.G("Auto,*");
		grid.Add(el, 0);
		grid.Add(_log.M(0.0, 4.0), 1);
		_log.BorderThickness = new Thickness(0.0);
		_log.Background = Brushes.Transparent;
		_log.Padding = new Thickness(0.0);
		return grid;
	}

	private UIElement BuildQuick()
	{
		RenderQuick();
		return _quickHost;
	}

	private List<string> QuickPicked()
	{
		JsonElement? jsonElement = _dash.Layout.Items.FirstOrDefault((DashCard c) => c.Type == "quick")?.Settings;
		if (jsonElement.HasValue)
		{
			JsonElement valueOrDefault = jsonElement.GetValueOrDefault();
			if (valueOrDefault.ValueKind == JsonValueKind.Object && valueOrDefault.TryGetProperty("targets", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				List<string> list = (from k in value.EnumerateArray().Where(delegate(JsonElement x)
					{
						JsonElement jsonElement2 = x;
						return jsonElement2.ValueKind == JsonValueKind.String;
					}).Select(delegate(JsonElement x)
					{
						JsonElement jsonElement2 = x;
						return jsonElement2.GetString() ?? "";
					})
					where QuickTargets.Any(((string Key, string Glyph, string Label) q) => q.Key == k)
					select k).ToList();
				if (list.Count > 0)
				{
					return list;
				}
			}
		}
		return QuickDefault.ToList();
	}

	private void RenderQuick()
	{
		_quickHost.Children.Clear();
		WrapPanel wrapPanel = new WrapPanel();
		foreach (string key in QuickPicked())
		{
			(string, string, string) tuple = QuickTargets.FirstOrDefault(((string Key, string Glyph, string Label) q) => q.Key == key);
			if (tuple.Item1 != null)
			{
				string i = tuple.Item1;
				Button button = Ui.Btn(tuple.Item3, BtnKind.Chip, delegate
				{
					QuickGo(i);
				}, tuple.Item2);
				button.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
				wrapPanel.Children.Add(button);
			}
		}
		Button button2 = Ui.Btn(I18n.L("选择入口…"), BtnKind.Ghost, delegate
		{
			PageBase.Run(PickQuickAsync);
		}, "\ue70f");
		button2.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
		wrapPanel.Children.Add(button2);
		_quickHost.Children.Add(wrapPanel);
	}

	private void QuickGo(string key)
	{
		if (!(key == "saves"))
		{
			if (key == "folder")
			{
				PageBase.Run(async delegate
				{
					await PageBase.Api.CallAsync("open_instance_folder", new
					{
						name = _inst.Str()
					});
				});
			}
			else
			{
				PageBase.Win?.Navigate(key);
			}
		}
		else
		{
			PageBase.Run(async delegate
			{
				await SavesDialog.ShowAsync(_inst.Str(), _ver.Str());
			});
		}
	}

	private async Task PickQuickAsync()
	{
		HashSet<string> hashSet = QuickPicked().ToHashSet<string>(StringComparer.Ordinal);
		List<(CheckBox Box, string Key)> boxes = new List<(CheckBox, string)>();
		SPanel sPanel = Ui.V(4.0, Ui.Muted(I18n.L("勾选要显示在卡片上的入口：")));
		(string, string, string)[] quickTargets = QuickTargets;
		for (int i = 0; i < quickTargets.Length; i++)
		{
			(string, string, string) tuple = quickTargets[i];
			string item = tuple.Item1;
			CheckBox checkBox = Ui.Check(tuple.Item3, hashSet.Contains(item));
			boxes.Add((checkBox, item));
			sPanel.Children.Add(checkBox);
		}
		if (!(await Dlg.Ask(I18n.L("选择快捷入口"), sPanel, I18n.L("确定"), I18n.L("取消"), danger: false, 380.0)))
		{
			return;
		}
		List<string> list = (from b in boxes
			where b.Box.IsChecked == true
			select b.Key).ToList();
		if (list.Count == 0)
		{
			return;
		}
		DashCard dashCard = _dash.Layout.Items.FirstOrDefault((DashCard c) => c.Type == "quick");
		if (dashCard != null)
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(JsonSerializer.Serialize(new
			{
				targets = list
			}));
			dashCard.Settings = jsonDocument.RootElement.Clone();
			PersistLayout();
		}
		RenderQuick();
	}

	protected override async Task LoadAsync()
	{
		if (!_layoutLoaded)
		{
			_layoutLoaded = true;
			try
			{
				ApplyLayoutState(await PageBase.Api.CallAsync("get_layout"));
			}
			catch (Exception ex)
			{
				_layoutLoaded = false;
				_layoutReady = false;
				_dash.Load(DashLayout.Default());
				PageBase.Toast(I18n.L("布局读取失败"), ex.Message, ToastKind.Warning);
			}
			if (PageBase.Win != null)
			{
				_notes.Text = PageBase.Win.Prefs.Notes;
			}
		}
		await FillCardData();
	}

	private async Task FillCardData()
	{
		_syncing = true;
		try
		{
			_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
			_inst.Fill(_instances.Select((InstanceInfo i) => i.Name));
			string keepAcc = _acc.Str();
			ComboBox acc = _acc;
			ComboBox c = acc;
			c.Fill(await AccountGate.ListAsync(), (keepAcc.Length > 0) ? keepAcc : PageBase.Win?.Prefs.LastUser);
			Dictionary<string, JsonElement> dictionary = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
			if (dictionary != null)
			{
				if (dictionary.TryGetValue("default_memory_mb", out var value) && value.TryGetInt32(out var value2))
				{
					_mem.Value = Clamp.Of(value2, 1024, 32768);
				}
				if (dictionary.TryGetValue("default_resolution", out var value3) && value3.ValueKind == JsonValueKind.Array && value3.GetArrayLength() >= 2)
				{
					_wBox.Text = value3[0].ToString();
					_hBox.Text = value3[1].ToString();
				}
			}
			_memLbl.Text = $"{(int)_mem.Value} MB";
			if (PageBase.Win != null && !string.IsNullOrEmpty(PageBase.Win.Prefs.Notes))
			{
				_notes.Text = PageBase.Win.Prefs.Notes;
			}
			if (string.IsNullOrWhiteSpace(_user.Text))
			{
				_user.Text = PageBase.Win?.Prefs.LastUser ?? "";
			}
		}
		finally
		{
			_syncing = false;
		}
		await ReloadVersionsAsync();
		await ReloadJavaAsync(scan: false);
		ReloadJavaAsync(scan: true);
		SyncBanner();
		await ApplyPreferenceAsync();
		LoadNewsAsync();
		LoadPlaytimeAsync();
		SyncTaskCard();
	}

	private async Task OnInstanceChanged()
	{
		await ReloadVersionsAsync();
		await ReloadJavaAsync(scan: false);
		SyncBanner();
	}

	private async Task ReloadVersionsAsync()
	{
		string text = _inst.Str();
		if (!string.IsNullOrEmpty(text))
		{
			List<string> list = (await PageBase.Api.TryCallAsync("get_installed_versions", new
			{
				instance = text
			}, new List<string>())) ?? new List<string>();
			_syncing = true;
			_ver.Fill(list);
			_syncing = false;
			_launchBtn.IsEnabled = list.Count > 0 && _taskId == null;
			if (list.Count == 0)
			{
				_status.Text = I18n.L("这个实例还没有版本，先去「原版游戏」装一个");
				_bSub.Text = I18n.L("还没有可启动的版本 · 去下载页安装");
			}
		}
	}

	private async Task ReloadJavaAsync(bool scan)
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = inst,
			scan_system = scan
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (!(_inst.Str() != inst) && opts.Count != 0)
		{
			string keep = await PageBase.Api.TryCallAsync("java_combo_label_for", new
			{
				instance = inst,
				options = opts
			}, "自动选择");
			_syncing = true;
			_javaOpts = opts;
			_java.Fill(opts.Select((JavaOption o) => o.Label), keep);
			_syncing = false;
		}
	}

	private async Task SaveJavaAsync()
	{
		string text = _inst.Str();
		if (!string.IsNullOrEmpty(text))
		{
			await PageBase.Api.TryCallAsync<object>("set_instance_java", new
			{
				name = text,
				java = SelectedJava()
			});
		}
	}

	private async Task SaveDisplayPrefsAsync()
	{
		await PageBase.Api.TryCallAsync<object>("update_settings", new
		{
			settings = new
			{
				default_memory_mb = (int)_mem.Value,
				default_resolution = new int[2]
				{
					Int(_wBox.Text, 854),
					Int(_hBox.Text, 480)
				}
			}
		});
	}

	public void PreferInstance(string name, string? version = null)
	{
		_preferredInstance = name;
		_preferredVersion = version;
		if (_instances.Count > 0)
		{
			PageBase.Run(ApplyPreferenceAsync);
		}
	}

	private async Task ApplyPreferenceAsync()
	{
		string name = _preferredInstance;
		if (string.IsNullOrEmpty(name) || _instances.All((InstanceInfo i) => i.Name != name))
		{
			return;
		}
		_preferredInstance = null;
		if (_inst.Str() != name)
		{
			_syncing = true;
			_inst.Fill(_instances.Select((InstanceInfo i) => i.Name), name);
			_syncing = false;
			await ReloadVersionsAsync();
			await ReloadJavaAsync(scan: false);
		}
		string preferredVersion = _preferredVersion;
		_preferredVersion = null;
		if (!string.IsNullOrEmpty(preferredVersion) && _ver.Items.Contains(preferredVersion))
		{
			_syncing = true;
			_ver.Fill(_ver.Items.Cast<string>(), preferredVersion);
			_syncing = false;
		}
		SyncBanner();
	}

	private string SelectedJava()
	{
		string label = _java.Str();
		object obj = _javaOpts.FirstOrDefault((JavaOption o) => o.Label == label)?.Value;
		if (obj == null)
		{
			if (!string.IsNullOrEmpty(label))
			{
				return label;
			}
			obj = "自动选择";
		}
		return (string)obj;
	}

	private void SyncBanner()
	{
		string version = _ver.Str();
		string instance = _inst.Str();
		InstanceInfo instanceInfo = _instances.FirstOrDefault((InstanceInfo x) => x.Name == instance);
		if (instanceInfo != null && !string.IsNullOrEmpty(instanceInfo.Pack))
		{
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(instanceInfo.PackVersion))
			{
				list.Add(instanceInfo.PackVersion);
			}
			if (!string.IsNullOrEmpty(instanceInfo.McVersion))
			{
				list.Add("Minecraft " + instanceInfo.McVersion);
			}
			list.Add(I18n.L("实例 ") + instance);
			_bTitle.Text = instanceInfo.Pack;
			_bSub.Text = string.Join(" · ", list);
			return;
		}
		_bTitle.Text = (string.IsNullOrEmpty(version) ? I18n.L("未选择版本") : version);
		_bSub.Text = (string.IsNullOrEmpty(version) ? I18n.L("先到「原版游戏」安装一个版本") : I18n.L("实例 {0} · 点「启动游戏」进入世界", instance));
		if (!string.IsNullOrEmpty(version))
		{
			PageBase.Run(() => TagLoaderAsync(version));
		}
	}

	private async Task TagLoaderAsync(string version)
	{
		List<string> list = await PageBase.Api.TryCallAsync("loader_of", new
		{
			version_id = version
		}, new List<string>());
		if (list != null && list.Count >= 1 && !(_ver.Str() != version))
		{
			string text = list[0];
			if (!string.IsNullOrWhiteSpace(text))
			{
				_bSub.Text = text + " · " + _bSub.Text;
			}
		}
	}

	private async Task LoadNewsAsync()
	{
		_newsHost.Children.Clear();
		_newsHost.Children.Add(Ui.Skeleton(14.0));
		_newsHost.Children.Add(Ui.Skeleton(12.0, 180.0));
		string text = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "homepage_mode",
			@default = "news"
		}, "news");
		if (text == "blank")
		{
			_newsHost.Children.Clear();
			_newsHost.Children.Add(Ui.Muted(I18n.L("主页已设为空白")));
			return;
		}
		if (text == "custom")
		{
			string path = await PageBase.Api.TryCallAsync("get_setting", new
			{
				key = "custom_homepage",
				@default = ""
			}, "");
			_newsHost.Children.Clear();
			try
			{
				string text2 = ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) ? I18n.L("未设置自定义主页。到设置 → 启动页主页 填本地 HTML 路径。") : (await Task.Run(() => File.ReadAllText(path))));
				string text3 = text2;
				_newsHost.Children.Add(Ui.Muted((text3.Length > 2000) ? (text3.Substring(0, 2000) + "…") : text3));
				return;
			}
			catch (Exception ex)
			{
				_newsHost.Children.Add(Ui.Muted(I18n.L("读不了自定义主页：") + ex.Message));
				return;
			}
		}
		List<NewsRow> rows = (await PageBase.Api.TryCallAsync("cached_news", null, new List<NewsRow>())) ?? new List<NewsRow>();
		FillNews(rows);
		List<NewsRow> list = await PageBase.Api.TryCallAsync<List<NewsRow>>("fetch_news");
		if (list != null && list.Count > 0)
		{
			FillNews(list);
		}
	}

	private void FillNews(List<NewsRow> rows)
	{
		_newsHost.Children.Clear();
		if (rows.Count == 0)
		{
			_newsHost.Children.Add(Ui.Muted(I18n.L("暂无新闻")));
			return;
		}
		foreach (NewsRow item in rows.Take(6))
		{
			_newsHost.Children.Add(Ui.Txt(item.Title, 12.5, bold: true).Wrap());
			if (!string.IsNullOrEmpty(item.Body))
			{
				_newsHost.Children.Add(Ui.Small(item.Body).Wrap());
			}
		}
		Motion.Stagger(_newsHost, 20.0, 200.0, 8.0);
	}

	private async Task LoadPlaytimeAsync()
	{
		long seconds = await PageBase.Api.TryCallAsync("get_total_playtime", null, 0L);
		_playTotal.Text = Fmt.Duration(seconds);
	}

	private void SyncTaskCard()
	{
		_taskHost.Children.Clear();
		List<TaskRow> list = TaskStore.Rows.Reverse().Take(5).ToList();
		if (list.Count == 0)
		{
			_taskHost.Children.Add(Ui.Muted(I18n.L("暂无下载任务")));
			return;
		}
		foreach (TaskRow item in list)
		{
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.Txt(item.Title, 12.0).Trim(), 0);
			grid.Add(Ui.Small(item.Finished ? (item.Success ? I18n.L("完成") : I18n.L("失败")) : $"{item.Progress:0}%").M(8.0, 0.0, 0.0, 0.0), 0, 1);
			_taskHost.Children.Add(grid);
		}
	}

	private async Task<string?> EnsureLaunchAccountAsync()
	{
		string account = await AccountGate.EnsureAsync(_acc.Str());
		if (account != null && _acc.Str() != account)
		{
			_syncing = true;
			try
			{
				ComboBox acc = _acc;
				ComboBox c = acc;
				c.Fill(await AccountGate.ListAsync(), account);
			}
			finally
			{
				_syncing = false;
			}
		}
		return account;
	}

	private async Task LaunchAsync()
	{
		string version = _ver.Str();
		if (string.IsNullOrEmpty(version))
		{
			PageBase.Toast(I18n.L("没有版本"), I18n.L("请先到「原版游戏」安装"), ToastKind.Warning);
			PageBase.Win?.Navigate("version");
			return;
		}
		string account = await EnsureLaunchAccountAsync();
		if (account == null)
		{
			return;
		}
		string instance = _inst.Str();
		int memory = (int)_mem.Value;
		string java = SelectedJava();
		if (!(await CheckMultiInstanceAsync()))
		{
			return;
		}
		PreflightResult preflightResult = await PageBase.Api.TryCallAsync<PreflightResult>("preflight_launch", new
		{
			instance = instance,
			version = version,
			memory_mb = memory,
			java = java
		});
		bool force = false;
		if (preflightResult == null)
		{
			if (!(await Dlg.Confirm(I18n.L("启动预检不可用"), I18n.L("preflight_launch 调用失败，无法检查 Java / 内存 / 文件完整性。\n跳过预检直接启动？"), I18n.L("继续启动"), I18n.L("取消"))))
			{
				return;
			}
		}
		else
		{
			List<PreflightItem> list = preflightResult.Items.Where((PreflightItem i) => i.Level == "error").ToList();
			List<PreflightItem> list2 = preflightResult.Items.Where((PreflightItem i) => i.Level == "warn").ToList();
			if (list.Count > 0)
			{
				string text = string.Join("\n\n", list.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail));
				if (list2.Count > 0)
				{
					text = text + "\n\n" + string.Join("\n\n", list2.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail));
				}
				text = text + "\n\n" + I18n.L("这些问题可能导致启动失败。仍要强制启动？");
				if (!(await Dlg.Confirm(I18n.L("启动预检未通过"), text, I18n.L("仍要启动"))))
				{
					return;
				}
				force = true;
			}
			else if (list2.Count > 0)
			{
				string body = string.Join("\n\n", list2.Select((PreflightItem i) => "· " + i.Title + "\n" + i.Detail)) + I18n.L("\n\n仍要继续启动？");
				if (!(await Dlg.Confirm(I18n.L("启动预检有警告"), body, I18n.L("继续启动"))))
				{
					return;
				}
			}
		}
		_logLines.Clear();
		_log.Clear();
		_prog.BeginAnimation(RangeBase.ValueProperty, null);
		_prog.Value = 0.0;
		_prog.Visibility = Visibility.Visible;
		_progGen++;
		_status.Text = I18n.L("准备启动…");
		_launchBtn.IsEnabled = false;
		_stopBtn.IsEnabled = true;
		_crashShown = false;
		Motion.Pulse(_launchBtn);
		if (PageBase.Win != null)
		{
			PageBase.Win.Prefs.LastUser = account;
			PageBase.Win.SaveUiPrefs();
		}
		try
		{
			_taskId = await PageBase.Api.StartTaskAsync("launch_game", new
			{
				instance = instance,
				version = version,
				account = account,
				username = account,
				memory_mb = memory,
				width = Int(_wBox.Text, 854),
				height = Int(_hBox.Text, 480),
				java = java,
				extra_game_args = ExtraArgs(),
				force = force
			});
		}
		catch (Exception ex)
		{
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			PageBase.Toast(I18n.L("启动失败"), ex.Message, ToastKind.Error);
		}
	}

	private async Task<bool> CheckMultiInstanceAsync()
	{
		if (!(await PageBase.Api.TryCallAsync("is_game_running", null, fallback: false)))
		{
			return true;
		}
		if (await PageBase.Api.TryCallAsync("allow_multi_instance", null, fallback: false))
		{
			return true;
		}
		if (await Dlg.Choose(I18n.L("游戏已经在运行"), Ui.Muted(I18n.L("当前设置不允许多开。可以先关掉正在跑的那个，或者打开多开后再启动一个。")).Wrap().MinW(360.0), new string[2]
		{
			I18n.L("取消"),
			I18n.L("打开多开并启动")
		}, 520.0) != 1)
		{
			return false;
		}
		await PageBase.Api.CallAsync<object>("set_multi_instance", new
		{
			allow = true
		});
		PageBase.Toast(I18n.L("已允许多开"), I18n.L("可以在设置页再关掉"), ToastKind.Success);
		return true;
	}

	private static int Int(string? s, int fallback)
	{
		if (!int.TryParse(s?.Trim(), out var result) || result <= 0)
		{
			return fallback;
		}
		return result;
	}

	private string[]? ExtraArgs()
	{
		string text = _server.Text?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		int num = text.LastIndexOf(':');
		if (num > 0)
		{
			return new string[4]
			{
				"--server",
				text.Substring(0, num),
				"--port",
				text.Substring(num + 1)
			};
		}
		return new string[4] { "--server", text, "--port", "25565" };
	}

	private async Task StopAsync()
	{
		if (_taskId != null)
		{
			await PageBase.Api.TryCallAsync<object>("cancel_task", new
			{
				task_id = _taskId
			});
		}
	}

	private async Task MicrosoftLoginAsync()
	{
		if (_loginLayer != null)
		{
			return;
		}
		_loginHint = Ui.Muted(I18n.L("正在获取登录代码…"));
		_loginCode = new TextBlock
		{
			Text = "------",
			FontSize = 26.0,
			FontWeight = FontWeights.Bold
		};
		_loginCode.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentText");
		Button button = Ui.Btn(I18n.L("复制代码"), BtnKind.Chip, delegate
		{
			try
			{
				Clipboard.SetText(_loginCode.Text);
				PageBase.Toast(I18n.L("已复制"), I18n.L("代码已放进剪贴板"), ToastKind.Success);
			}
			catch
			{
			}
		}, "\ue8c8");
		Button button2 = Ui.Btn(I18n.L("打开浏览器"), BtnKind.Primary, delegate
		{
			if (!string.IsNullOrEmpty(_loginUri))
			{
				Ui.OpenUrl(_loginUri);
			}
		}, "\ue71b");
		SPanel body = Ui.V(10.0, _loginHint, _loginCode, Ui.H(8.0, button, button2));
		_loginLayer = Dlg.Panel(I18n.L("微软账号登录"), body, 460.0, delegate
		{
			_loginLayer = null;
		});
		_loginTask = await PageBase.Api.StartTaskAsync("start_microsoft_login");
	}

	private async Task AuthlibLoginAsync()
	{
		List<AuthlibPreset> presets = (await PageBase.Api.TryCallAsync("authlib_presets", null, new List<AuthlibPreset>())) ?? new List<AuthlibPreset>();
		TextBox api = Ui.Input("https://littleskin.cn/api/yggdrasil", presets.FirstOrDefault()?.Api ?? "");
		TextBox user = Ui.Input(I18n.L("邮箱 / 用户名"));
		PasswordBox pw = Ui.Pw();
		ComboBox pick = Ui.Combo(presets.Select((AuthlibPreset p) => p.Name));
		pick.SelectionChanged += delegate
		{
			AuthlibPreset authlibPreset = presets.FirstOrDefault((AuthlibPreset p) => p.Name == pick.Str());
			if (authlibPreset != null)
			{
				api.Text = authlibPreset.Api;
			}
		};
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("选择皮肤站，或直接填 Yggdrasil API")), pick, api, user, pw);
		if (await Dlg.Ask(I18n.L("皮肤站登录"), body, I18n.L("登录")))
		{
			await PageBase.Api.StartTaskAsync("start_authlib_login", new
			{
				api = (api.Text?.Trim() ?? ""),
				username = (user.Text?.Trim() ?? ""),
				password = (pw.Password ?? "")
			});
		}
	}

	private async Task VersionSettingsAsync()
	{
		string instance = _inst.Str();
		string text = _ver.Str();
		if (string.IsNullOrEmpty(text))
		{
			PageBase.Toast(I18n.L("未选择版本"), I18n.L("先安装并选中一个版本"), ToastKind.Warning);
		}
		else
		{
			await VersionSetupDialog.ShowAsync(instance, text);
		}
	}

	private async Task ShowLaunchCommandAsync()
	{
		string ver = _ver.Str();
		if (string.IsNullOrEmpty(ver))
		{
			return;
		}
		string account = await EnsureLaunchAccountAsync();
		if (account == null)
		{
			return;
		}
		using (Dlg.Busy(I18n.L("正在生成启动命令…")))
		{
			string cmd;
			try
			{
				BridgeClient api = PageBase.Api;
				string instance = _inst.Str();
				List<string> list = await api.CallAsync<List<string>>("build_launch_command", new
				{
					instance = instance,
					version = ver,
					account = account,
					username = account,
					memory_mb = (int)_mem.Value,
					width = Int(_wBox.Text, 854),
					height = Int(_hBox.Text, 480),
					java = SelectedJava()
				});
				cmd = ((list != null && list.Count > 0) ? string.Join(" ", list) : null);
			}
			catch (BridgeCallException)
			{
				cmd = await PageBase.Api.CallAsync<string>("get_launch_command", new
				{
					instance = _inst.Str(),
					version = ver,
					account = account,
					username = account,
					memory_mb = (int)_mem.Value
				});
			}
			await Dlg.Alert(I18n.L("启动命令"), cmd ?? "");
		}
	}

	private async Task ExportScriptAsync()
	{
		string text = _ver.Str();
		if (!string.IsNullOrEmpty(text))
		{
			string text2 = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + _inst.Str() + "-" + text + ".bat", I18n.L("导出启动脚本"));
			if (text2 != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = _inst.Str(),
					version = text,
					dest = text2
				});
			}
		}
	}

	private async Task ShortcutAsync()
	{
		string text = _ver.Str();
		string text2 = (string.IsNullOrEmpty(text) ? null : (await EnsureLaunchAccountAsync()));
		string text3 = text2;
		if (text3 != null)
		{
			string text4 = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = _inst.Str(),
				version = text,
				username = text3,
				account = text3
			});
			PageBase.Toast(I18n.L("已创建快捷方式"), text4 ?? "", ToastKind.Success);
		}
	}

	private async Task RepairAsync()
	{
		string ver = _ver.Str();
		bool flag = !string.IsNullOrEmpty(ver);
		if (flag)
		{
			flag = await Dlg.Confirm(I18n.L("修复版本"), I18n.L("重新校验并补齐「{0}」的依赖库与资源？", ver));
		}
		if (flag)
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = _inst.Str(),
				version = ver
			});
			PageBase.Win?.FlyToTasks(_launchBtn, I18n.L("修复中"));
		}
	}

	public override void OnEvent(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "login_code":
			if (_loginLayer != null)
			{
				_loginUri = ev.Uri;
				if (_loginCode != null)
				{
					_loginCode.Text = ev.Code;
				}
				if (_loginHint != null)
				{
					_loginHint.Text = I18n.L("在浏览器打开下面的地址并输入代码：\n") + ev.Uri;
				}
			}
			break;
		case "login_status":
			if (_loginHint != null)
			{
				_loginHint.Text = ev.Text;
			}
			break;
		case "finished":
			if (!(ev.TaskId != _taskId))
			{
				break;
			}
			goto case "task_added";
		case "progress":
			if (!(ev.TaskId != _taskId))
			{
				break;
			}
			goto case "task_added";
		case "task_added":
			SyncTaskCard();
			break;
		}
		if (ev.Event == "finished" && ev.TaskId == _loginTask)
		{
			if (ev.Success)
			{
				_loginLayer?.Close();
				_loginLayer = null;
				PageBase.Run(FillCardData);
			}
			else if (_loginHint != null)
			{
				_loginHint.Text = ev.Message;
			}
		}
		if (ev.Event == "crash" && (string.IsNullOrEmpty(ev.TaskId) || ev.TaskId == _taskId))
		{
			_crashShown = true;
			CrashReport report = ev.Crash ?? new CrashReport
			{
				Title = ev.Title,
				Detail = ev.Message,
				TaskId = ev.TaskId
			};
			PageBase.Run(() => HandleCrashAsync(report, gameError: true));
		}
		if (_taskId != null)
		{
			string text = ev.Event;
			if (!(text == "game_started"))
			{
				if (text == "game_exited")
				{
					JsonElement value;
					int? num = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("code", out value) && value.ValueKind == JsonValueKind.Number) ? new int?(value.GetInt32()) : ((int?)null));
					_status.Text = ((!num.HasValue) ? I18n.L("游戏已退出") : I18n.L("游戏已退出（退出码 {0}）", num));
					HideProgressSoon(0);
				}
			}
			else
			{
				_status.Text = I18n.L("游戏进程已启动，正在加载世界…");
				Motion.Progress(_prog, Math.Max(_prog.Value, 92.0));
			}
		}
		if (ev.TaskId != _taskId)
		{
			return;
		}
		switch (ev.Event)
		{
		case "progress":
		{
			Motion.Progress(_prog, (ev.Total > 0) ? ((double)ev.Current * 100.0 / (double)ev.Total) : 0.0);
			Fmt.SplitMsg(ev.Message, out string status, out string speed);
			_status.Text = (string.IsNullOrEmpty(status) ? I18n.L("处理中…") : status) + (string.IsNullOrEmpty(speed) ? "" : ("    " + speed));
			break;
		}
		case "log":
			AppendLog(ev.Text);
			break;
		case "finished":
			_launchBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			_status.Text = ((!string.IsNullOrEmpty(ev.Message)) ? ev.Message : (ev.Success ? I18n.L("已完成") : I18n.L("✘ 失败")));
			if (ev.Success)
			{
				Motion.Progress(_prog, 100.0);
			}
			HideProgressSoon(ev.Success ? 1200 : 0);
			if (!ev.Success && !_crashShown && ev.Message != "已取消")
			{
				_crashShown = true;
				PageBase.Run(() => HandleCrashAsync(new CrashReport
				{
					Title = I18n.L("启动失败"),
					Headline = ev.Message,
					Detail = ev.Message,
					Instance = _inst.Str(),
					Version = _ver.Str()
				}, gameError: true));
			}
			_taskId = null;
			LoadPlaytimeAsync();
			break;
		}
	}

	private async Task HandleCrashAsync(CrashReport report, bool gameError = false)
	{
		if (await CrashUi.ShowAsync(report, gameError))
		{
			await LaunchAsync();
		}
	}

	private void AppendLog(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			_logLines.Add(text);
			if (_logLines.Count > 2500)
			{
				_logLines.RemoveRange(0, _logLines.Count - 2500);
			}
			bool num = _log.VerticalOffset >= _log.ExtentHeight - _log.ViewportHeight - 24.0;
			_log.Text = string.Join("\n", _logLines);
			if (num)
			{
				_log.ScrollToEnd();
			}
		}
	}

	public override void OnShown()
	{
		SyncTaskCard();
		_dash.RefreshThemeVisuals();
	}
}

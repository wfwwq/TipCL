using System;
using TipCL.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C5b:Windows 版 <c>Pages\AccountPage.cs</c>(17904 字节 / 582 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「账号」页(导航表里 id = <c>account</c>,标题「账号」,图标 <c>\ue77b</c>)。
///
/// 形状(逐条照抄 Windows):左上一张 240 宽的头像卡(<see cref="ThumbTile"/> + 正身像 + 名字 / 类型)/
/// 右边一列账号卡(每张 = 头像 + 名字与标签 + UUID 副行 + 「皮肤 / 设为当前 / 删除」);
/// 上面一行是五种入口:添加离线账号 / 微软登录 / 皮肤站登录 / 统一通行证 / 刷新 + 「共 N 个」。
///
/// 数据源与调用名一字未改:<c>get_account_rows</c> / <c>get_account_skin</c> / <c>skin_urls</c> /
/// <c>set_active_account</c> / <c>remove_account</c> / <c>add_offline_account</c> / <c>set_account_skin</c> /
/// <c>authlib_presets</c> / <c>start_microsoft_login</c> / <c>start_authlib_login</c> / <c>start_nide8_login</c> /
/// <c>cancel_task</c>;皮肤正身像走 <see cref="SkinPng"/>。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>Visibility</c> / <c>UIElement</c> / <c>FrameworkElement</c> → <c>IsVisible</c> / <c>Control</c>
///   (本文件只经 <c>Ui</c> 拿控件;隐藏一律用既有门面 <c>Show(false)</c> / <c>IsVisible</c>);</item>
/// <item><c>BitmapSource</c> / <c>Image.Source</c> → Avalonia 的 <c>Bitmap</c>(<c>Image.Source</c> 收 <c>IImage</c>);
///   <c>RenderOptions.SetBitmapScalingMode(…, NearestNeighbor)</c> →
///   <c>RenderOptions.SetBitmapInterpolationMode(…, BitmapInterpolationMode.None)</c>(与 <c>Wallpaper.cs</c> 同一口径);</item>
/// <item><c>SetResourceReference(TextBlock.ForegroundProperty, "B.AccentText")</c> → <c>Dyn(...)</c>;</item>
/// <item><c>PasswordBox</c> → <c>Ui.Pw()</c>(Avalonia 11 没有 <c>PasswordBox</c>,见 D-75),
///   所以读的是 <c>pw.Text</c> 而不是 <c>pw.Password</c>;</item>
/// <item><c>Clipboard.SetText(…)</c> + 空 <c>catch</c> → <c>CrashUi.CopyAsync(this, …)</c>
///   (C2a/C3b 的既有注入缝,离屏不碰真剪贴板;写进去了才 Toast「已复制」,与 Windows 的 try/catch 等价);</item>
/// <item><c>AppServices.Window?.GetPage("account") is AccountPage</c> → <c>PageBase.Win?.GetPage("account")</c>;</item>
/// <item><b>有意不搬的一句</b>:Windows 的 <c>LoadAsync</c> 头一行 <c>_ = AppServices.Window?.SyncAccountChipAsync();</c>
///   —— mac 外壳在 C2b 就**故意删掉了顶栏那个账号圆片**(见 <c>MainWindow.cs:40</c> 的记录,Lead 已裁决),
///   这里不为一个不存在的圆片新增外壳成员,也不假装同步过(如实登记在 DECISIONS);</item>
/// <item>局部变量名照 Windows 的 <c>textBlock2</c> / <c>text2</c> 那套反编译器风格**改成语义名**
///   (类型、顺序、判据、闭包捕获的时机一字不改)。</item>
/// </list>
/// </summary>
public sealed class AccountPage : PageBase
{
	private readonly SPanel _list = Ui.V(10.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly ThumbTile _heroAvatar = new ThumbTile("?", 96.0, 14.0);

	private readonly Image _heroBody = new Image
	{
		Stretch = Stretch.Uniform,
		Height = 190.0,
	};

	private readonly TextBlock _heroName = Ui.Txt(I18n.L("未登录"), 15.0, bold: true).Trim();

	private readonly TextBlock _heroKind = Ui.Small("");

	private string? _loginTask;

	private string? _authlibTask;

	private string? _nide8Task;

	private Dlg.Layer? _loginLayer;

	private TextBlock? _loginHint;

	private TextBlock? _loginCode;

	private string _loginUri = "";

	/// <summary>【Windows 源】<c>:46</c>:标题「账号」。</summary>
	public override string Title => I18n.L("账号");

	/// <summary>【Windows 源】<c>:48-87</c>:五个入口 + 头像卡 + 两个区块的 <c>BlockHost("account")</c>。</summary>
	public AccountPage()
	{
		Button offline = Ui.Btn(I18n.L("添加离线账号"), BtnKind.Primary, delegate
		{
			PageBase.Run(AddOfflineAsync, I18n.L("添加失败"));
		}, "\ue710");
		Button microsoft = Ui.Btn(I18n.L("微软登录"), BtnKind.Soft, delegate
		{
			PageBase.Run(MicrosoftLoginAsync, I18n.L("微软登录失败"));
		}, "\ue77b");
		Button authlib = Ui.Btn(I18n.L("皮肤站登录"), BtnKind.Chip, delegate
		{
			PageBase.Run(AuthlibLoginAsync, I18n.L("皮肤站登录失败"));
		}, "\uea18");
		Button nide8 = Ui.Btn(I18n.L("统一通行证"), BtnKind.Chip, delegate
		{
			PageBase.Run(Nide8LoginAsync, I18n.L("统一通行证登录失败"));
		}, "\ue71b");
		Button refresh = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
		{
			PageBase.Run(LoadAsync);
		});
		Grid header = Ui.Section(I18n.L("账号"), I18n.L("启动游戏时使用标记为「当前」的账号"), Ui.H(8.0, _count.VCenter(), refresh, nide8, authlib, microsoft, offline));
		Grid heroRow = Ui.G(null, "Auto,*");
		heroRow.Add(_heroAvatar, 0);
		heroRow.Add(Ui.V(2.0, _heroName, _heroKind).VCenter().M(10.0, 0.0, 0.0, 0.0), 0, 1);
		Border heroCard = Ui.Card(Ui.V(10.0, _heroBody, heroRow));
		heroCard.Width = 240.0;
		// 【Windows 源】:正身像默认藏着,拿到图才 Show(true)(见 RenderHeroAsync)
		_heroBody.IsVisible = false;
		RenderOptions.SetBitmapInterpolationMode(_heroBody, BitmapInterpolationMode.None);
		// 【C9a】两栏交给共用的 PageLayoutHost(与 Windows `AccountPage.cs:82` 同一处声明:
		// `"account"` / `"hero"` / `Auto` + 右栏 `*`)。间距沿用现状 —— 左卡自带 14px 右外边距,
		// 所以 gap 传 0,像素与切前的写死版一致(帧逐字节不变);Windows 的 per-site 间距在未移植的 SplitCatalog 里。
		PageLayoutHost cards = new PageLayoutHost("account", "hero", GridLength.Auto, new GridLength(1.0, GridUnitType.Star), gap: 0.0);
		cards.SetLeft(heroCard.M(0.0, 0.0, 14.0).VTop());
		cards.SetRight(_list);
		// 布局自定义:账号操作与账号卡各成一个区块,默认顺序与可见性和旧版完全一致
		Content = new BlockHost("account", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("账号操作"), Content = header },
			new BlockSpec { Id = "list", Title = I18n.L("账号卡"), Content = cards },
		}).Root;
	}

	/// <summary>
	/// 【Windows 源】<c>:89-107</c>:拉账号表 → 逐张画卡 → 给「当前」那个(没有就第 0 个)渲染头像卡。
	///
	/// 有意不搬的一句见类注释第 7 条(<c>SyncAccountChipAsync</c>,mac 侧没有那个圆片)。
	/// </summary>
	protected override async Task LoadAsync()
	{
		List<AccountRow> rows = (await PageBase.Api.TryCallAsync("get_account_rows", null, new List<AccountRow>())) ?? new List<AccountRow>();
		_list.Children.Clear();
		_count.Text = I18n.L("共 {0} 个", rows.Count);
		if (rows.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue77b", I18n.L("还没有账号"), I18n.L("添加离线账号,或用微软 / 皮肤站 / 统一通行证登录")));
			await RenderHeroAsync(null);
			return;
		}

		foreach (AccountRow row in rows)
		{
			_list.Children.Add(Row(row));
		}

		Motion.Stagger(_list, 22.0, 200.0, 10.0);
		await RenderHeroAsync(rows.FirstOrDefault((AccountRow r) => r.Active) ?? rows[0]);
	}

	/// <summary>【Windows 源】<c>:109-152</c>:有本地皮肤文件就画正身像,否则退回 <c>skin_urls</c> + <see cref="Thumbs"/>。</summary>
	private async Task RenderHeroAsync(AccountRow? account)
	{
		_heroBody.Source = null;
		_heroBody.IsVisible = false;
		if (account == null)
		{
			_heroName.Text = I18n.L("未登录");
			_heroKind.Text = I18n.L("先添加一个账号");
			_heroAvatar.SetUrl(null);
			return;
		}

		_heroName.Text = account.Name;
		_heroKind.Text = TypeLabel(account);
		if (!string.IsNullOrEmpty(account.SkinFile))
		{
			AccountSkin? skin = await PageBase.Api.TryCallAsync<AccountSkin>("get_account_skin", new
			{
				name = account.Name
			});
			byte[]? bytes = SkinPng.Decode(skin?.DataUrl);
			if (bytes != null)
			{
				// bytes 非空 ⇒ skin 一定非空(Decode 的入参是 skin?.DataUrl)
				Bitmap? front = SkinPng.FrontView(bytes, skin!.Slim);
				if (front != null)
				{
					_heroBody.Source = front;
					_heroBody.IsVisible = true;
					_heroAvatar.SetUrl(null);
					return;
				}
			}
		}

		SkinUrls? urls = await PageBase.Api.TryCallAsync<SkinUrls>("skin_urls", new
		{
			account_name = account.Name
		});
		_heroAvatar.SetUrl(urls?.Avatar ?? account.Avatar);
		string body = urls?.Body ?? account.Body;
		if (!string.IsNullOrWhiteSpace(body))
		{
			Bitmap? bitmap = await Thumbs.LoadAsync(body, 190);
			if (bitmap != null)
			{
				_heroBody.Source = bitmap;
				_heroBody.IsVisible = true;
			}
		}
	}

	/// <summary>【Windows 源】<c>:154-164</c>:四种已知类型给中文名;认不出来的按 Api / Type 兜。</summary>
	private static string TypeLabel(AccountRow account)
	{
		return account.Type switch
		{
			"microsoft" => I18n.L("微软"),
			"offline" => I18n.L("离线"),
			"authlib" => I18n.L("皮肤站"),
			"nide8" => I18n.L("统一通行证"),
			_ => (!string.IsNullOrEmpty(account.Api)) ? I18n.L("皮肤站") : (string.IsNullOrEmpty(account.Type) ? I18n.L("其他") : account.Type),
		};
	}

	/// <summary>【Windows 源】<c>:166-204</c>:一张账号卡(头像 + 名字与标签 + 副行 + 皮肤 / 设为当前 / 删除)。</summary>
	private Control Row(AccountRow account)
	{
		ThumbTile tile = new ThumbTile(account.Name, 40.0, 20.0);
		tile.SetUrl(string.IsNullOrEmpty(account.SkinFile) ? account.Avatar : null);
		string type = TypeLabel(account);
		SPanel head = Ui.H(7.0, Ui.Txt(account.Name, 13.5, bold: true).Trim().VCenter(), account.Active ? Ui.Tag(I18n.L("当前"), "B.OnAccentStrong", "B.AccentStrong") : null, Ui.Tag(type), string.IsNullOrEmpty(account.SkinFile) ? null : Ui.Tag(I18n.L("自定义皮肤"), "B.OnOk", "B.Ok"));
		// 【Windows 源】:没有 UUID 就显示「<类型>账号」;超过 40 个字截断加省略号
		string sub = (string.IsNullOrEmpty(account.Uuid) ? (type + I18n.L("账号")) : ("UUID " + account.Uuid));
		if (sub.Length > 40)
		{
			sub = sub.Substring(0, 40) + "...";
		}

		SPanel actions = Ui.H(6.0);
		if (account.Type == "offline")
		{
			actions.Children.Add(Ui.Btn(I18n.L("皮肤"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => EditSkinAsync(account), I18n.L("皮肤没能保存"));
			}, "\ueb9f"));
		}

		if (!account.Active)
		{
			actions.Children.Add(Ui.Btn(I18n.L("设为当前"), BtnKind.Soft, delegate
			{
				PageBase.Run(() => SetActiveAsync(account.Name), I18n.L("切换失败"));
			}, "\ue73e"));
		}

		actions.Children.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteAsync(account), I18n.L("删除失败"));
		}));
		actions.VCenter();
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(tile.M(0.0, 0.0, 12.0), 0);
		grid.Add(Ui.V(3.0, head, Ui.Small(sub)).VCenter(), 0, 1);
		grid.Add(actions, 0, 2);
		Border card = Ui.RowCard(grid);
		Motion.HoverLift(card, 1.004, 1.0, 16.0);
		return card;
	}

	/// <summary>【Windows 源】<c>:206-211</c>:切当前账号。</summary>
	private async Task SetActiveAsync(string name)
	{
		await PageBase.Api.CallAsync<object>("set_active_account", new { name });
		PageBase.Toast(I18n.L("已切换"), I18n.L("当前账号:{0}", name), ToastKind.Success);
		await LoadAsync();
	}

	/// <summary>【Windows 源】<c>:213-224</c>:删账号要确认(微软账号刷新令牌一起丢)。</summary>
	private async Task DeleteAsync(AccountRow account)
	{
		if (await Dlg.Confirm(I18n.L("删除账号"), I18n.L("将删除「{0}」。微软账号的刷新令牌也会一并丢失,需要重新走设备码登录。", account.Name), I18n.L("删除"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync<object>("remove_account", new
			{
				name = account.Name
			});
			PageBase.Toast(I18n.L("已删除"), account.Name, ToastKind.Success);
			await LoadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:226-255</c>:加离线账号(名字为空只提示,不写后端)。</summary>
	private async Task AddOfflineAsync()
	{
		TextBox name = Ui.Input(I18n.L("例如 Steve"));
		ComboBox skin = Ui.Combo(new string[3]
		{
			I18n.L("默认"),
			"Steve",
			"Alex",
		});
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("离线模式,无正版验证")), name, Ui.Field(I18n.L("默认皮肤"), skin, null, 78.0));
		if (await Dlg.Ask(I18n.L("添加离线账号"), body, I18n.L("添加")))
		{
			if (string.IsNullOrWhiteSpace(name.Text))
			{
				PageBase.Toast(I18n.L("缺少名字"), I18n.L("请填写离线角色名"), ToastKind.Warning);
				return;
			}

			BridgeClient api = PageBase.Api;
			string username = name.Text.Trim();
			string picked = skin.Str();
			string model = ((picked == "Steve") ? "steve" : ((picked == "Alex") ? "alex" : "default"));
			await api.CallAsync<object>("add_offline_account", new
			{
				username = username,
				skin = model
			});
			PageBase.Toast(I18n.L("已添加"), name.Text.Trim(), ToastKind.Success);
			await LoadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:257-265</c>:卡片上的「皮肤」按钮(存好了才 Toast,回来一律重拉一次)。</summary>
	private async Task EditSkinAsync(AccountRow account)
	{
		string message = await EditSkinCoreAsync(account, "");
		if (!string.IsNullOrEmpty(message))
		{
			PageBase.Toast(I18n.L("皮肤已更新"), message, ToastKind.Success);
		}

		await LoadAsync();
	}

	/// <summary>
	/// 【Windows 源】<c>:267-275</c>:给外部调用者用的入口(Windows 的「设置 → 换个皮肤」那条路),
	/// 存完顺手把账号页刷新一次。
	/// </summary>
	internal static async Task<string> EditSkinExternalAsync(AccountRow account, string presetPath)
	{
		string message = await EditSkinCoreAsync(account, presetPath);
		if (PageBase.Win?.GetPage("account") is AccountPage page)
		{
			await page.RefreshAsync();
		}

		return message;
	}

	/// <summary>【Windows 源】<c>:277-397</c>:选 PNG / 清除 / 宽臂细臂 + 预览,确认后写 <c>set_account_skin</c>。</summary>
	private static async Task<string> EditSkinCoreAsync(AccountRow account, string presetPath)
	{
		BridgeClient api = AppServices.Client;
		AccountSkin current = (await api.TryCallAsync<AccountSkin>("get_account_skin", new
		{
			name = account.Name
		})) ?? new AccountSkin();
		string picked = presetPath ?? "";
		bool clear = false;
		Image preview = new Image
		{
			Height = 200.0,
			Stretch = Stretch.Uniform,
		};
		RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.None);
		TextBlock previewHint = Ui.Muted(I18n.L("还没设皮肤")).Center();
		TextBlock fileLabel = Ui.Small(string.IsNullOrEmpty(current.SkinFile) ? I18n.L("游戏默认皮肤") : current.SkinFile).Center();
		ComboBox model = Ui.Combo(new string[2]
		{
			I18n.L("宽臂(Steve)"),
			I18n.L("细臂(Alex)"),
		}, current.Slim ? I18n.L("细臂(Alex)") : I18n.L("宽臂(Steve)"));
		Button clearButton = Ui.Btn(I18n.L("清除,用游戏默认"), BtnKind.Chip, null, "\ue74d");
		clearButton.IsEnabled = !string.IsNullOrEmpty(current.SkinFile) || picked.Length > 0;
		if (picked.Length > 0)
		{
			fileLabel.Text = picked;
		}

		Button choose = Ui.Btn(I18n.L("选择 PNG"), BtnKind.Primary, delegate
		{
			string? file = Dlg.PickFile(I18n.L("PNG 图片 (*.png)|*.png"), I18n.L("选择皮肤 PNG"));
			if (file != null)
			{
				picked = file;
				clear = false;
				clearButton.IsEnabled = true;
				fileLabel.Text = file;
				Refresh();
			}
		}, "\ueb9f");
		clearButton.Click += delegate
		{
			picked = "";
			clear = true;
			clearButton.IsEnabled = false;
			fileLabel.Text = I18n.L("保存后清除,恢复游戏默认皮肤");
			Refresh();
		};
		model.SelectionChanged += delegate
		{
			Refresh();
		};
		Refresh();
		Border previewPane = Ui.Pane(new Grid
		{
			Children =
			{
				previewHint,
				preview,
			},
		}, "B.Paper2", 10.0, 10.0);
		previewPane.Width = 150.0;
		SPanel body = Ui.V(10.0, Ui.Muted(I18n.L("64x64 或 64x32 的 PNG。进游戏后由启动器自带的本地皮肤服务发给游戏,不需要联网。")).Wrap(), previewPane.Center(), fileLabel, Ui.H(8.0, choose, clearButton), Ui.Field(I18n.L("手臂模型"), model, null, 78.0));
		if (!await Dlg.Ask(I18n.L("「{0}」的皮肤", account.Name), body, I18n.L("保存"), I18n.L("取消"), danger: false, 460.0))
		{
			return "";
		}

		if (clear)
		{
			await api.CallAsync<object>("set_account_skin", new
			{
				name = account.Name
			});
			return I18n.L("已清除,回到游戏默认皮肤");
		}

		string path = picked;
		if (path.Length == 0 && current.SkinFile.Length == 0)
		{
			return "";
		}

		await api.CallAsync<object>("set_account_skin", new
		{
			name = account.Name,
			path = path,
			model = ((model.SelectedIndex == 1) ? "slim" : "classic"),
			data = ((path.Length == 0) ? current.DataUrl : "")
		});
		return I18n.L("下次启动游戏时生效");

		void Refresh()
		{
			if (clear)
			{
				preview.Source = null;
				previewHint.Text = I18n.L("保存后清除,恢复游戏默认皮肤");
				previewHint.IsVisible = true;
				return;
			}

			byte[]? bytes = SourcePng();
			Bitmap? bitmap = ((bytes == null) ? null : SkinPng.FrontView(bytes, model.SelectedIndex == 1));
			preview.Source = bitmap;
			previewHint.IsVisible = (bitmap == null);
			previewHint.Text = ((bytes == null) ? I18n.L("还没设皮肤") : I18n.L("这张图读不出来"));
		}

		byte[]? SourcePng()
		{
			if (picked.Length > 0)
			{
				try
				{
					return File.ReadAllBytes(picked);
				}
				catch
				{
					return null;
				}
			}

			return SkinPng.Decode(current.DataUrl);
		}
	}

	/// <summary>【Windows 源】<c>:399-452</c>:微软设备码登录(浮层不可关、关掉就取消那一次登录任务)。</summary>
	private async Task MicrosoftLoginAsync()
	{
		if (_loginLayer != null)
		{
			return;
		}

		TextBlock hint = Ui.Muted(I18n.L("正在获取登录代码..."));
		hint.TextWrapping = TextWrapping.Wrap;
		TextBlock code = new TextBlock
		{
			FontSize = 26.0,
			FontWeight = FontWeight.Bold,
			Text = "------",
		};
		code.Dyn(TextBlock.ForegroundProperty, "B.AccentText");
		_loginHint = hint;
		_loginCode = code;
		_loginUri = "";
		Button copy = Ui.Btn(I18n.L("复制代码"), BtnKind.Chip, delegate
		{
			// 【Windows 源】是 Clipboard.SetText + 空 catch;mac 走既有剪贴板注入缝(离屏不碰真剪贴板),
			// 写进去了才 Toast「已复制」—— 与 Windows 的 try { …; Toast(已复制); } catch { } 等价。
			PageBase.Run(async delegate
			{
				TextBlock? codeblock = _loginCode;
				if (codeblock != null && await CrashUi.CopyAsync(this, codeblock.Text ?? ""))
				{
					PageBase.Toast(I18n.L("已复制"), I18n.L("代码已放进剪贴板"), ToastKind.Success);
				}
			});
		}, "\ue8c8");
		Button browse = Ui.Btn(I18n.L("打开浏览器"), BtnKind.Primary, delegate
		{
			if (!string.IsNullOrEmpty(_loginUri))
			{
				Ui.OpenUrl(_loginUri);
			}
		}, "\ue71b");
		SPanel body = Ui.V(10.0, hint, code, Ui.H(8.0, copy, browse));
		_loginLayer = Dlg.Panel(I18n.L("微软账号登录"), body, 460.0, delegate
		{
			_loginLayer = null;
			if (_loginTask != null)
			{
				_ = PageBase.Api.TryCallAsync<object>("cancel_task", new
				{
					task_id = _loginTask
				});
			}
		}, dismissable: false);
		_loginTask = await PageBase.Api.StartTaskAsync("start_microsoft_login");
	}

	/// <summary>【Windows 源】<c>:454-485</c>:皮肤站登录(选预设会带出 API 地址;地址或用户名为空只提示)。</summary>
	private async Task AuthlibLoginAsync()
	{
		List<AuthlibPreset> presets = (await PageBase.Api.TryCallAsync("authlib_presets", null, new List<AuthlibPreset>())) ?? new List<AuthlibPreset>();
		TextBox apiBox = Ui.Input("https://littleskin.cn/api/yggdrasil", presets.FirstOrDefault()?.Api ?? "");
		TextBox user = Ui.Input(I18n.L("邮箱 / 用户名"));
		TextBox pw = Ui.Pw();
		ComboBox pick = Ui.Combo(presets.Select((AuthlibPreset p) => p.Name));
		pick.SelectionChanged += delegate
		{
			AuthlibPreset? preset = presets.FirstOrDefault((AuthlibPreset p) => p.Name == pick.Str());
			if (preset != null)
			{
				apiBox.Text = preset.Api;
			}
		};
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("选择皮肤站,或直接填 Yggdrasil API 地址")), pick, apiBox, user, pw);
		if (await Dlg.Ask(I18n.L("皮肤站登录"), body, I18n.L("登录")))
		{
			if (string.IsNullOrWhiteSpace(apiBox.Text) || string.IsNullOrWhiteSpace(user.Text))
			{
				PageBase.Toast(I18n.L("填写不完整"), I18n.L("API 地址和用户名都不能为空"), ToastKind.Warning);
				return;
			}

			_authlibTask = await PageBase.Api.StartTaskAsync("start_authlib_login", new
			{
				api = apiBox.Text.Trim(),
				username = user.Text.Trim(),
				password = (pw.Text ?? "")
			});
			PageBase.Toast(I18n.L("正在登录"), I18n.L("皮肤站验证中,结果看下载任务"));
		}
	}

	/// <summary>【Windows 源】<c>:487-511</c>:统一通行证登录(服务器 ID 为空时抖一下那个输入框)。</summary>
	private async Task Nide8LoginAsync()
	{
		TextBox sid = Ui.Input(I18n.L("服务器 ID / 链接"));
		TextBox user = Ui.Input(I18n.L("用户名"));
		TextBox pw = Ui.Pw();
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("填 32 位服务器 ID,或把含该 ID 的链接贴进来")), sid, user, pw);
		if (await Dlg.Ask(I18n.L("统一通行证登录"), body, I18n.L("登录")))
		{
			if (string.IsNullOrWhiteSpace(sid.Text))
			{
				PageBase.Toast(I18n.L("缺少服务器 ID"), I18n.L("请填写统一通行证服务器 ID"), ToastKind.Warning);
				Motion.Shake(sid);
			}
			else
			{
				_nide8Task = await PageBase.Api.StartTaskAsync("start_nide8_login", new
				{
					server_id = sid.Text.Trim(),
					username = (user.Text?.Trim() ?? ""),
					password = (pw.Text ?? "")
				});
				PageBase.Toast(I18n.L("正在登录"), I18n.L("统一通行证验证中,结果看下载任务"));
			}
		}
	}

	/// <summary>【Windows 源】<c>:513-581</c>:登录码 / 登录状态 / 三个任务的 finished 都从这里进来。</summary>
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
					_loginHint.Text = I18n.L("在浏览器打开下面的地址并输入代码:\n") + ev.Uri;
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
			if (ev.TaskId == _loginTask)
			{
				_loginTask = null;
				if (ev.Success)
				{
					_loginLayer?.Close();
					_loginLayer = null;
					PageBase.Toast(I18n.L("登录成功"), I18n.L("微软账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else if (_loginHint != null)
				{
					_loginHint.Text = ev.Message;
				}
			}
			else if (ev.TaskId == _authlibTask)
			{
				_authlibTask = null;
				if (ev.Success)
				{
					PageBase.Toast(I18n.L("登录成功"), I18n.L("皮肤站账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else
				{
					PageBase.Toast(I18n.L("登录失败"), ev.Message, ToastKind.Error);
				}
			}
			else if (ev.TaskId == _nide8Task)
			{
				_nide8Task = null;
				if (ev.Success)
				{
					PageBase.Toast(I18n.L("登录成功"), I18n.L("统一通行证账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else
				{
					PageBase.Toast(I18n.L("登录失败"), ev.Message, ToastKind.Error);
				}
			}

			break;
		}
	}
}

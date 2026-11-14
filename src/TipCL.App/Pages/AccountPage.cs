using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TipCL.App.Common;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages;

// 账号页(移植自 Windows 版 Pages/AccountPage.cs):
// 顶部操作区(添加离线 / 微软 / 皮肤站 / 统一通行证 / 刷新)+ 英雄卡(头像 + 皮肤正面图)
// + 账号行(首字母圆 + 名称 + 标签 + 皮肤/设为当前/删除)。
// 与 Windows 的差异:登录进度用本页自建模态窗(Windows 走 Dlg.Panel 的模型层,Linux Dlg 暂无该形态)。
public sealed class AccountPage : PageBase
{
    private sealed class AccountRow
    {
        public string Name = "";
        public string Type = "";
        public string Uuid = "";
        public string Api = "";
        public string Avatar = "";
        public string Body = "";
        public string SkinFile = "";
        public string SkinModel = "";
        public bool Active;
    }

    private readonly StackPanel _list = UiClass.V(10);

    private readonly TextBlock _count = UiClass.Muted("");

    private readonly AvatarTile _heroAvatar = new("?", 96, 48);

    private readonly Image _heroBody = new() { Stretch = Stretch.Uniform, Height = 190 };

    private readonly TextBlock _heroName = UiClass.Txt("未登录", 15, bold: true).Trim();

    private readonly TextBlock _heroKind = UiClass.Small("");

    private Window? _loginWin;

    private TextBlock? _loginHint;

    private TextBlock? _loginCode;

    private string _loginUri = "";

    private string _loginTask = "";

    private string _authlibTask = "";

    private string _nide8Task = "";

    private bool _eventWired;

    public AccountPage() : base("account", "账号")
    {
        var addOffline = UiClass.Btn("添加离线账号", BtnKind.Primary, (_, _) => RunSafe(AddOfflineAsync, "添加失败"));
        var msLogin = UiClass.Btn("微软登录", BtnKind.Soft, (_, _) => RunSafe(MicrosoftLoginAsync, "微软登录失败"));
        var authlib = UiClass.Btn("皮肤站登录", BtnKind.Chip, (_, _) => RunSafe(AuthlibLoginAsync, "皮肤站登录失败"));
        var nide8 = UiClass.Btn("统一通行证", BtnKind.Chip, (_, _) => RunSafe(Nide8LoginAsync, "统一通行证登录失败"));
        var refresh = UiClass.IconBtn("Refresh", "刷新", (_, _) => RunSafe(LoadAccountsAsync, ""));

        var header = UiClass.Section("账号", "启动游戏时使用标记为「当前」的账号",
            UiClass.H(8, _count.VCenter(), refresh, nide8, authlib, msLogin, addOffline));

        _heroBody.IsVisible = false;
        var heroCard = UiClass.Card(UiClass.V(10, _heroBody,
            UiClass.G(null, "Auto,*")
                .Add(_heroAvatar, 0, 0)
                .Add(UiClass.V(2, _heroName, _heroKind).VCenter().Mk(10, 0, 0, 0), 0, 1)));
        heroCard.Width = 240;

        var body = UiClass.G(null, "Auto,*");
        body.Add(heroCard.Mk(0, 0, 14, 0).VTop(), 0, 0);
        body.Add(_list, 0, 1);

        Body = new BlockHost("account", new List<BlockSpec>
        {
            new() { Id = "header", Title = "账号操作", Content = header },
            new() { Id = "list", Title = "账号卡", Content = body },
        }).Root;
    }

    protected override void OnCreate()
    {
        if (!_eventWired)
        {
            _eventWired = true;
            // 登录进度/用户码靠事件流推进;订阅后统一回 UI 线程处理
            Api.BridgeEvent += ev => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(ev));
        }
        _ = LoadAccountsAsync();
    }

    protected override void OnActivate() => _ = LoadAccountsAsync();

    // ————— 数据 —————

    private async Task LoadAccountsAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_account_rows", new List<object>());
        var accounts = (rows ?? new List<object>()).Select(r =>
        {
            var d = Py.D(r);
            return new AccountRow
            {
                Name = Py.S(d.Get("name")),
                Type = Py.S(d.Get("type")),
                Uuid = Py.S(d.Get("uuid")),
                Api = Py.S(d.Get("api")),
                Avatar = Py.S(d.Get("avatar")),
                Body = Py.S(d.Get("body")),
                SkinFile = Py.S(d.Get("skin_file")),
                SkinModel = Py.S(d.Get("skin_model")),
                Active = Py.Truthy(d.Get("active")),
            };
        }).ToList();

        _list.Children.Clear();
        _count.Text = $"共 {accounts.Count} 个";
        if (accounts.Count == 0)
        {
            _list.Children.Add(UiClass.Empty("User", "还没有账号", "添加离线账号,或用微软 / 皮肤站 / 统一通行证登录"));
            await RenderHeroAsync(null);
            return;
        }
        foreach (var acc in accounts)
            _list.Children.Add(Row(acc));
        Motion.Stagger(_list, 22);
        await RenderHeroAsync(accounts.FirstOrDefault(a => a.Active) ?? accounts[0]);
    }

    private async Task RenderHeroAsync(AccountRow? acc)
    {
        _heroBody.Source = null;
        _heroBody.IsVisible = false;
        if (acc == null)
        {
            _heroName.Text = "未登录";
            _heroKind.Text = "先添加一个账号";
            _heroAvatar.SetUrl(null);
            return;
        }
        _heroName.Text = acc.Name;
        _heroKind.Text = TypeLabel(acc);

        // 自定义皮肤优先用本地 PNG 渲染正面图(纯本地,不需联网)
        if (acc.SkinFile.Length > 0)
        {
            var skin = await Api.TryCallAsync<PyDict>("get_account_skin", null, new PyDict { ["name"] = acc.Name });
            byte[]? png = SkinPng.Decode(skin != null ? Py.S(skin.Get("data_url")) : null);
            var front = png != null ? SkinPng.FrontView(png, acc.SkinModel == "slim") : null;
            if (front != null)
            {
                _heroBody.Source = front;
                _heroBody.IsVisible = true;
                _heroAvatar.SetUrl(null);
                return;
            }
        }

        var urls = await Api.TryCallAsync<PyDict>("skin_urls", null, new PyDict { ["account_name"] = acc.Name });
        string avatar = urls != null ? Py.S(urls.Get("avatar")) : acc.Avatar;
        string bodyUrl = urls != null ? Py.S(urls.Get("body")) : acc.Body;
        _heroAvatar.SetUrl(avatar.Length > 0 ? avatar : acc.Avatar);
        if (bodyUrl.Length > 0)
        {
            Bitmap? bmp = await Thumbs.LoadAsync(bodyUrl, 190);
            if (bmp != null)
            {
                _heroBody.Source = bmp;
                _heroBody.IsVisible = true;
            }
        }
    }

    private static string TypeLabel(AccountRow acc) => acc.Type switch
    {
        "microsoft" => "微软",
        "offline" => "离线",
        "authlib" => "皮肤站",
        "nide8" => "统一通行证",
        _ => acc.Api.Length > 0 ? "皮肤站" : (acc.Type.Length == 0 ? "其他" : acc.Type),
    };

    private Control Row(AccountRow acc)
    {
        var tile = new AvatarTile(acc.Name, 40, 20);
        tile.SetUrl(acc.SkinFile.Length == 0 ? acc.Avatar : null);

        string label = TypeLabel(acc);
        var tags = UiClass.H(7,
            UiClass.Txt(acc.Name, 13.5, bold: true).Trim().VCenter(),
            acc.Active ? UiClass.Tag("当前", "B.OnAccentStrong", "B.AccentStrong") : null,
            UiClass.Tag(label),
            acc.SkinFile.Length == 0 ? null : UiClass.Tag("自定义皮肤", "B.OnOk", "B.Ok"));
        string sub = acc.Uuid.Length == 0 ? label + "账号" : "UUID " + acc.Uuid;
        if (sub.Length > 40)
            sub = sub[..40] + "...";

        var ops = UiClass.H(6);
        if (acc.Type == "offline")
            ops.Children.Add(UiClass.Btn("皮肤", BtnKind.Chip, (_, _) => RunSafe(() => EditSkinAsync(acc), "皮肤没能保存")));
        if (!acc.Active)
            ops.Children.Add(UiClass.Btn("设为当前", BtnKind.Soft, (_, _) => RunSafe(() => SetActiveAsync(acc.Name), "切换失败")));
        ops.Children.Add(UiClass.Btn("删除", BtnKind.Ghost, (_, _) => RunSafe(() => DeleteAsync(acc), "删除失败")));
        ops.VCenter();

        var grid = UiClass.G(null, "Auto,*,Auto");
        grid.Add(tile.Mk(0, 0, 12, 0), 0, 0);
        grid.Add(UiClass.V(3, tags, UiClass.Small(sub)).VCenter(), 0, 1);
        grid.Add(ops, 0, 2);

        var card = UiClass.RowCard(grid);
        Motion.HoverLift(card, 1.004);
        return card;
    }

    // ————— 操作 —————

    private async Task SetActiveAsync(string name)
    {
        await Api.CallAsync<object>("set_active_account", new PyDict { ["name"] = name });
        Toast("已切换", "当前账号:" + name, ToastKind.Success);
        await LoadAccountsAsync();
    }

    private async Task DeleteAsync(AccountRow acc)
    {
        if (!await Dlg.Confirm("删除账号",
                $"将删除「{acc.Name}」。微软账号的刷新令牌也会一并丢失,需要重新走设备码登录。",
                "删除", "取消", danger: true))
            return;
        await Api.CallAsync<object>("remove_account", new PyDict { ["name"] = acc.Name });
        Toast("已删除", acc.Name, ToastKind.Success);
        await LoadAccountsAsync();
    }

    private async Task AddOfflineAsync()
    {
        var name = UiClass.Input("例如 Steve");
        var skin = UiClass.Combo(new[] { "默认", "Steve", "Alex" });
        var body = UiClass.V(8, UiClass.Muted("离线模式,无正版验证"), name, UiClass.Field("默认皮肤", skin, null, 78));
        if (!await Dlg.Ask("添加离线账号", body, "添加"))
            return;
        string username = name.Text?.Trim() ?? "";
        if (username.Length == 0)
        {
            Toast("缺少名字", "请填写离线角色名", ToastKind.Warning);
            return;
        }
        string picked = skin.SelectedItem as string ?? "默认";
        string skinKey = picked == "Steve" ? "steve" : picked == "Alex" ? "alex" : "default";
        await Api.CallAsync<object>("add_offline_account", new PyDict { ["username"] = username, ["skin"] = skinKey });
        Toast("已添加", username, ToastKind.Success);
        await LoadAccountsAsync();
    }

    private async Task EditSkinAsync(AccountRow acc)
    {
        var current = await Api.TryCallAsync<PyDict>("get_account_skin", null, new PyDict { ["name"] = acc.Name });
        string currentFile = current != null ? Py.S(current.Get("skin_file")) : "";
        bool currentSlim = current != null && Py.Truthy(current.Get("slim"));
        string picked = "";
        bool clear = false;

        var preview = new Image { Height = 200, Stretch = Stretch.Uniform, IsVisible = false };
        var previewHint = UiClass.Muted("还没设皮肤").Center();
        var fileLabel = UiClass.Small(currentFile.Length == 0 ? "游戏默认皮肤" : currentFile).Center();
        var model = UiClass.Combo(new[] { "宽臂(Steve)", "细臂(Alex)" }, currentSlim ? "细臂(Alex)" : "宽臂(Steve)");
        var clearBtn = UiClass.Btn("清除,用游戏默认", BtnKind.Chip, null);
        clearBtn.IsEnabled = currentFile.Length > 0;

        var pane = UiClass.Pane(new Panel { Children = { previewHint, preview } }, "B.Paper2", 10, 10);
        pane.Width = 150;

        var pick = UiClass.Btn("选择 PNG", BtnKind.Primary, async (_, _) =>
        {
            string? p = await Dlg.PickFile("选择皮肤 PNG", "*.png");
            if (string.IsNullOrEmpty(p)) return;
            picked = p;
            clear = false;
            clearBtn.IsEnabled = true;
            fileLabel.Text = p;
            Refresh();
        });
        clearBtn.Click += (_, _) =>
        {
            picked = "";
            clear = true;
            clearBtn.IsEnabled = false;
            fileLabel.Text = "保存后清除,恢复游戏默认皮肤";
            Refresh();
        };
        model.SelectionChanged += (_, _) => Refresh();

        void Refresh()
        {
            if (clear)
            {
                preview.Source = null;
                preview.IsVisible = false;
                previewHint.IsVisible = true;
                previewHint.Text = "保存后清除,恢复游戏默认皮肤";
                return;
            }
            byte[]? src = SourcePng();
            var bmp = src != null ? SkinPng.FrontView(src, model.SelectedItem as string == "细臂(Alex)") : null;
            preview.Source = bmp;
            preview.IsVisible = bmp != null;
            previewHint.IsVisible = bmp == null;
            previewHint.Text = src == null ? "还没设皮肤" : "这张图读不出来";
        }

        byte[]? SourcePng()
        {
            if (picked.Length > 0)
            {
                try { return System.IO.File.ReadAllBytes(picked); }
                catch { return null; }
            }
            return SkinPng.Decode(current != null ? Py.S(current.Get("data_url")) : null);
        }

        Refresh();

        var body = UiClass.V(10,
            UiClass.Muted("64x64 或 64x32 的 PNG。进游戏后由启动器自带的本地皮肤服务发给游戏,不需要联网。").Wrap(),
            pane.Center(), fileLabel,
            UiClass.H(8, pick, clearBtn),
            UiClass.Field("手臂模型", model, null, 78));
        if (!await Dlg.Ask($"「{acc.Name}」的皮肤", body, "保存", "取消", width: 460))
            return;

        if (clear)
        {
            await Api.CallAsync<object>("set_account_skin", new PyDict { ["name"] = acc.Name });
            Toast("皮肤已更新", "已清除,回到游戏默认皮肤", ToastKind.Success);
            await LoadAccountsAsync();
            return;
        }
        if (picked.Length == 0 && currentFile.Length == 0)
            return;
        await Api.CallAsync<object>("set_account_skin", new PyDict
        {
            ["name"] = acc.Name,
            ["path"] = picked,
            ["model"] = model.SelectedItem as string == "细臂(Alex)" ? "slim" : "classic",
        });
        Toast("皮肤已更新", "下次启动游戏时生效", ToastKind.Success);
        await LoadAccountsAsync();
    }

    // ————— 登录 —————

    private async Task MicrosoftLoginAsync()
    {
        if (_loginWin != null)
            return;
        _loginHint = UiClass.Muted("正在获取登录代码...").Wrap();
        _loginCode = UiClass.Txt("------", 26, bold: true, "B.AccentText");
        _loginUri = "";
        var copy = UiClass.Btn("复制代码", BtnKind.Chip, async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip != null && _loginCode != null)
                await clip.SetTextAsync(_loginCode.Text ?? "");
            Toast("已复制", "代码已放进剪贴板", ToastKind.Success);
        });
        var openBtn = UiClass.Btn("打开浏览器", BtnKind.Primary, (_, _) => OpenUrl(_loginUri));
        var body = UiClass.V(10, _loginHint, _loginCode, UiClass.H(8, copy, openBtn));
        _loginWin = BuildModal("微软账号登录", body, 460, onClosed: () =>
        {
            _loginWin = null;
            if (_loginTask.Length > 0)
            {
                Api.CancelTask(_loginTask);
                _loginTask = "";
            }
        });
        var host = Dlg.Owner ?? TopLevel.GetTopLevel(this) as Window;
        if (host == null)
        {
            _loginWin = null;
            return;
        }
        _ = _loginWin.ShowDialog(host);
        _loginTask = await Api.CallAsync<string>("start_microsoft_login") ?? "";
    }

    private async Task AuthlibLoginAsync()
    {
        var presets = await Api.TryCallAsync<List<object>>("authlib_presets", new List<object>()) ?? new List<object>();
        var presetNames = presets.Select(p => Py.S(Py.D(p).Get("name"))).Where(n => n.Length > 0).ToList();
        var api = UiClass.Input("https://littleskin.cn/api/yggdrasil",
            presets.Count > 0 ? Py.S(Py.D(presets[0]).Get("api")) : "");
        var user = UiClass.Input("邮箱 / 用户名");
        var pw = UiClass.Pw();
        var pick = UiClass.Combo(presetNames);
        pick.SelectionChanged += (_, _) =>
        {
            string name = pick.SelectedItem as string ?? "";
            var hit = presets.FirstOrDefault(p => Py.S(Py.D(p).Get("name")) == name);
            if (hit != null)
                api.Text = Py.S(Py.D(hit).Get("api"));
        };
        var body = UiClass.V(8, UiClass.Muted("选择皮肤站,或直接填 Yggdrasil API 地址"), pick, api, user, pw);
        if (!await Dlg.Ask("皮肤站登录", body, "登录"))
            return;
        if (api.Text?.Trim().Length is null or 0 || user.Text?.Trim().Length is null or 0)
        {
            Toast("填写不完整", "API 地址和用户名都不能为空", ToastKind.Warning);
            return;
        }
        _authlibTask = await Api.CallAsync<string>("start_authlib_login", new PyDict
        {
            ["api"] = api.Text.Trim(),
            ["username"] = user.Text.Trim(),
            ["password"] = pw.Text ?? "",
        }) ?? "";
        Toast("正在登录", "皮肤站验证中,结果看下载任务");
    }

    private async Task Nide8LoginAsync()
    {
        var sid = UiClass.Input("服务器 ID / 链接");
        var user = UiClass.Input("用户名");
        var pw = UiClass.Pw();
        var body = UiClass.V(8, UiClass.Muted("填 32 位服务器 ID,或把含该 ID 的链接贴进来"), sid, user, pw);
        if (!await Dlg.Ask("统一通行证登录", body, "登录"))
            return;
        if (sid.Text?.Trim().Length is null or 0)
        {
            Toast("缺少服务器 ID", "请填写统一通行证服务器 ID", ToastKind.Warning);
            return;
        }
        _nide8Task = await Api.CallAsync<string>("start_nide8_login", new PyDict
        {
            ["server_id"] = sid.Text.Trim(),
            ["username"] = user.Text?.Trim() ?? "",
            ["password"] = pw.Text ?? "",
        }) ?? "";
        Toast("正在登录", "统一通行证验证中,结果看下载任务");
    }

    protected override void OnEvent(ApiEvent ev)
    {
        switch (ev.Event)
        {
            case "login_code":
                if (_loginWin == null)
                    break;
                _loginUri = ev.Uri;
                if (_loginCode != null) _loginCode.Text = ev.Code;
                if (_loginHint != null) _loginHint.Text = "在浏览器打开下面的地址并输入代码:\n" + ev.Uri;
                break;
            case "login_status":
                if (_loginHint != null) _loginHint.Text = ev.Text;
                break;
            case "finished":
                if (ev.TaskId == _loginTask && _loginTask.Length > 0)
                {
                    _loginTask = "";
                    if (ev.Success)
                    {
                        CloseLoginWindow();
                        Toast("登录成功", "微软账号已加入列表", ToastKind.Success);
                        _ = LoadAccountsAsync();
                    }
                    else if (_loginHint != null)
                    {
                        _loginHint.Text = ev.Message;
                    }
                }
                else if (ev.TaskId == _authlibTask && _authlibTask.Length > 0)
                {
                    _authlibTask = "";
                    Toast(ev.Success ? "登录成功" : "登录失败", ev.Success ? "皮肤站账号已加入列表" : ev.Message,
                        ev.Success ? ToastKind.Success : ToastKind.Error);
                    if (ev.Success) _ = LoadAccountsAsync();
                }
                else if (ev.TaskId == _nide8Task && _nide8Task.Length > 0)
                {
                    _nide8Task = "";
                    Toast(ev.Success ? "登录成功" : "登录失败", ev.Success ? "统一通行证账号已加入列表" : ev.Message,
                        ev.Success ? ToastKind.Success : ToastKind.Error);
                    if (ev.Success) _ = LoadAccountsAsync();
                }
                break;
        }
    }

    private void CloseLoginWindow()
    {
        _loginWin?.Close();
        _loginWin = null;
    }

    private Window BuildModal(string title, Control body, double width, Action onClosed)
    {
        var win = new Window
        {
            Title = title,
            Width = width,
            MinWidth = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var inner = UiClass.V(14,
            UiClass.Txt(title, 17, bold: true, "B.Ink"),
            body);
        var card = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = inner,
        };
        card.Dyn(Border.BackgroundProperty, "B.Paper");
        card.Dyn(Border.BorderBrushProperty, "B.Line");
        win.Content = card;
        win.Closed += (_, _) => onClosed();
        return win;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { url });
            else
                Process.Start("xdg-open", new[] { url });
        }
        catch (Exception)
        {
            // 打不开浏览器不算致命:用户可手动复制地址
        }
    }

    /// <summary>跑一个异步操作,失败弹提示(对齐 Windows PageBase.Run(action, failToast))。</summary>
    private void RunSafe(Func<Task> action, string failTitle)
    {
        _ = RunSafeAsync(action, failTitle);
    }

    private async Task RunSafeAsync(Func<Task> action, string failTitle)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (failTitle.Length > 0)
                Toast(failTitle, ex.Message, ToastKind.Error);
        }
    }
}

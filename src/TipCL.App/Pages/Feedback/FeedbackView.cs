// 来源: Windows 版参考仓库 src/TipCL/Pages/FeedbackPage.cs(M4-H P4 移植)。
// 反馈与帮助:提交反馈(类型 / 标题 / 正文 / 联系方式 / 附带系统信息 / 附件)+ 我提交过的 +
// 常见问题(搜索 + 点标题展开)。
// 说明:Linux 侧这一页挂在设置页内层导航「反馈与关于」里(与 Windows 的 SettingsPage 第 7 项同位置);
// 提交链路沿用 M4-F 打通的两道门(同意门 → feedback_submit → 显示服务器原话),见 D-14。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using U = TipCL.App.Pages.Layout.U;
using TipCL.App.Pages.Settings;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Feedback;

/// <summary>反馈与帮助子页。</summary>
internal sealed class FeedbackView : SettingsSubView
{
    private static readonly string[] Categories = { "问题反馈", "功能建议", "崩溃报告", "其它" };

    private readonly ComboBox _cat = UiClass.Combo(Categories, "问题反馈", 160);
    private readonly TextBox _title = UiClass.Input("一句话说清问题");
    private readonly TextBox _body = UiClass.Multi("详细描述:怎么触发的、期望是什么", "", 130);
    private readonly TextBox _contact = UiClass.Input("联系方式(可空)");
    private readonly CheckBox _sys = UiClass.Check("附带系统信息(显卡 / 内存 / Java)", true);
    private readonly StackPanel _attachList = UiClass.V(4);
    private readonly StackPanel _history = UiClass.V(6);
    private readonly StackPanel _articles = UiClass.V(6);
    private readonly TextBox _search = UiClass.Input("搜索帮助文章");
    private readonly List<string> _attachments = new();

    public FeedbackView() : base("settings:feedback", "反馈与帮助", "遇到问题先看帮助,解决不了再提反馈")
    {
        // ── 提交反馈 ──
        var submit = U.Btn("提交反馈", BtnKind.Primary, (_, _) => Run(SubmitAsync, "提交失败"), "Send");
        var sysInfo = U.Btn("查看系统信息", BtnKind.Chip, (_, _) => Run(SysInfoAsync, "读取失败"), "Info");
        var attach = U.Btn("添加附件", BtnKind.Chip, (_, _) => Run(async () => { await AttachAsync(); }, "附件没能上传"), "Import");
        var form = UiClass.V(10,
            UiClass.Section("提交反馈", "会发到开发者的反馈服务,不含账号密码"),
            UiClass.Field("类型", _cat, null, 74),
            UiClass.Field("标题", _title, null, 74),
            _body,
            UiClass.Field("联系方式", _contact, null, 74),
            _sys,
            _attachList,
            UiClass.H(8, submit, attach, sysInfo));
        _stack.Children.Add(UiClass.Card(form, 14));

        // ── 我提交过的 ──
        _stack.Children.Add(UiClass.Card(UiClass.V(10, UiClass.Section("我提交过的"), _history), 14));

        // ── 常见问题 ──
        _search.TextChanged += (_, _) => Run(() => LoadArticlesAsync(_search.Text), "读取失败");
        var searchRow = UiClass.G(null, "*,Auto");
        searchRow.Add(_search, 0, 0);
        var runWizard = U.Btn("重新运行向导", BtnKind.Chip, (_, _) => Run(FirstRunWizard.ShowAsync), "Rocket");
        runWizard.M(8, 0, 0, 0).VCenter();
        searchRow.Add(runWizard, 0, 1);
        _stack.Children.Add(UiClass.Card(UiClass.V(10,
            UiClass.Section("常见问题", "启动、Java、模组、账号、联机的快速说明(点标题展开)"),
            searchRow, _articles), 14));
        _stack.Children.Add(UiClass.Card(UiClass.V(10,
            UiClass.Section("关于", "TipCL · AI 原生 Minecraft 启动器"),
            UiClass.Small($"启动器版本 {TipCL.Core.Launcher.AppInfo.APP_VERSION}"),
            UiClass.Small($"数据目录 {Utils.ROOT}").Wrap()), 14));
    }

    protected override async Task LoadCoreAsync()
    {
        await LoadHistoryAsync();
        await LoadArticlesAsync("");
    }

    // ───────────────────────────── 历史 ─────────────────────────────

    private async Task LoadHistoryAsync()
    {
        _history.Children.Clear();
        var rows = await Api.TryCallAsync<List<object?>>("feedback_history", null) ?? new();
        if (rows.Count == 0)
        {
            _history.Children.Add(UiClass.Muted("还没有提交记录"));
            return;
        }
        foreach (var item in rows.Take(20))
        {
            if (item is not PyDict d) continue;
            var grid = UiClass.G(null, "*,Auto");
            var col = UiClass.V(2,
                UiClass.Txt(Py.S(d.Get("title")), 12.5).Ellipsis(),
                UiClass.Small(JoinMeta(Py.S(d.Get("category")), TimeText(Py.Int(d.Get("ts"))))));
            col.VCenter();
            grid.Add(col, 0, 0);
            bool ok = Py.Truthy(d.Get("ok", true));
            grid.Add(UiClass.Tag(ok ? "已提交" : "提交失败").VCenter(), 0, 1);
            _history.Children.Add(grid);
        }
    }

    // ───────────────────────────── 常见问题 ─────────────────────────────

    private async Task LoadArticlesAsync(string? query)
    {
        _articles.Children.Clear();
        var rows = await Api.TryCallAsync<List<object?>>("help_articles", null, new { query = query ?? "" }) ?? new();
        if (rows.Count == 0)
        {
            _articles.Children.Add(UiClass.Muted("没有匹配的文章"));
            return;
        }
        foreach (var item in rows.Take(30))
        {
            if (item is not PyDict d) continue;
            string id = Py.S(d.Get("id"));
            string articleTitle = Py.S(d.Get("title"));
            var text = UiClass.Muted("");
            text.TextWrapping = TextWrapping.Wrap;
            text.Margin = new Avalonia.Thickness(8, 0, 8, 4);
            text.IsVisible = false;
            var btn = UiClass.Btn(articleTitle, BtnKind.Ghost);
            btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            btn.Padding = new Avalonia.Thickness(8, 6, 8, 7);
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            btn.Click += (_, _) => Run(() => ToggleArticleAsync(id, text), "读取失败");
            _articles.Children.Add(UiClass.V(2, btn, text));
        }
    }

    private async Task ToggleArticleAsync(string id, TextBlock body)
    {
        if (body.IsVisible)
        {
            body.IsVisible = false;
            return;
        }
        if (string.IsNullOrEmpty(body.Text))
        {
            var art = await Api.TryCallAsync<PyDict>("help_article", null, new { article_id = id });
            body.Text = art == null ? "" : Py.S(art.Get("body"));
        }
        body.Text = string.IsNullOrEmpty(body.Text) ? "暂无内容" : body.Text;
        body.IsVisible = true;
    }

    // ───────────────────────────── 提交 ─────────────────────────────

    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            Toast("标题不能为空", "", ToastKind.Warning);
            return;
        }
        // 同意门(照 D-14):没同意过就先问,否则 Core 会直接抛「需要先同意上传诊断数据」
        bool consent = await Api.TryCallAsync<bool>("feedback_has_consent", false);
        if (!consent)
        {
            bool allow = await Dlg.Confirm("是否上传诊断数据",
                "同意后才会向开发者上传:\n· 你提交的反馈内容\n· 本机配置(CPU / 内存 / 显卡 / Java / 已装版本)\n\n暂不同意则不会上传。",
                "同意", "暂不同意");
            await Api.CallAsync<PyDict>("feedback_consent", new { allow });
            if (!allow)
            {
                Toast("未同意上传", "已取消,可在本页重新提交", ToastKind.Warning);
                return;
            }
        }

        string body = _body.Text ?? "";
        if (_attachments.Count > 0)
            body = body.TrimEnd() + "\n\n附件:\n" + string.Join("\n", _attachments);

        try
        {
            var res = await Api.CallAsync<PyDict>("submit_feedback", new
            {
                category = MapCategory(_cat.SelectedItem as string ?? "其它"),
                title = _title.Text.Trim(),
                body,
                contact = _contact.Text?.Trim() ?? "",
                include_sysinfo = _sys.IsChecked == true,
            });
            string message = res == null ? "" : Py.S(res.Get("message"));
            Toast("已提交", message, ToastKind.Success);
            _title.Text = "";
            _body.Text = "";
            _attachments.Clear();
            RenderAttachments();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            // 把服务器原话显示出来(403/400 要看得见,不能伪装成「后端没做」)
            Toast("提交失败", ex.Message, ToastKind.Error);
        }
    }

    /// <summary>中文标签 → 桌面端分类键(照 Windows Categories + D-15 的归并)。</summary>
    private static string MapCategory(string label) => label switch
    {
        "问题反馈" => "bug",
        "功能建议" => "suggest",
        "崩溃报告" => "crash",
        _ => "other",
    };

    private async Task SysInfoAsync()
    {
        var info = await Api.TryCallAsync<PyDict>("collect_sysinfo", null, new { force = true, scan_system_java = true });
        string text = await Api.TryCallAsync<string>("sysinfo_text", "", new { info }) ?? "";
        await Dlg.Alert("系统信息", text);
    }

    /// <summary>
    /// 「添加附件」入口:多选后逐个上传(对齐 Windows `FeedbackPage.AttachAsync` 的 foreach)。
    /// 取消(空数组)什么都不做;单个文件走 <see cref="AttachFilesAsync"/> 的 N=1 分支,提示逐字不变。
    /// </summary>
    public async Task AttachAsync()
    {
        var files = await Dlg.PickFiles("添加附件", "*.png", "*.jpg", "*.log", "*.txt");
        await AttachFilesAsync(files);
    }

    /// <summary>
    /// 逐个上传附件(测试直接喂真实临时文件)。返回逐文件结果:
    /// 空数组 = 取消(一条都不上传、也不报错);多选失败最后给一条带文件名与原因的汇总。
    /// </summary>
    public async Task<ImportBatch> AttachFilesAsync(IReadOnlyList<string> paths)
    {
        var batch = new ImportBatch(paths.Count);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string name = Path.GetFileName(path);
            try
            {
                byte[] bytes;
                try { bytes = await Task.Run(() => File.ReadAllBytes(path)); }
                catch (Exception ex)
                {
                    // 单个文件:与改动前逐字一致(「读不了这个文件」+ 文件名:原因),且不重画附件列表
                    if (batch.Total == 1)
                    {
                        Toast("读不了这个文件", $"{name}:{ex.Message}", ToastKind.Warning);
                        return batch;
                    }
                    batch.Failure(name, ex.Message);
                    continue;
                }
                if (bytes.Length == 0)
                {
                    if (batch.Total == 1)
                    {
                        Toast("跳过空文件", name, ToastKind.Warning);
                        return batch;
                    }
                    batch.Failure(name, "空文件");
                    continue;
                }
                string stored = await Api.CallAsync<string>("stash_upload", new
                {
                    name,
                    data = Convert.ToBase64String(bytes),
                }) ?? "";
                if (stored.Length > 0) { _attachments.Add(stored); batch.Success(name); }
                else batch.Failure(name, "上传接口没有返回落盘路径");
            }
            catch (Exception ex)
            {
                // 单个文件:往外抛,交外层 Run(AttachAsync, "附件没能上传") 提示,与改动前一致
                if (batch.Total == 1) throw;
                batch.Failure(name, ex.Message);
            }
        }
        if (batch.Total > 1)
        {
            Toast(batch.AnyFailed ? "部分附件没能上传" : "已添加附件", batch.SummaryText(),
                batch.AnyFailed ? ToastKind.Warning : ToastKind.Success);
        }
        RenderAttachments();
        return batch;
    }

    private void RenderAttachments()
    {
        _attachList.Children.Clear();
        if (_attachments.Count == 0) return;
        _attachList.Children.Add(UiClass.Small($"已附 {_attachments.Count} 个文件"));
        foreach (string stored in _attachments.ToList())
        {
            string path = stored;
            var grid = UiClass.G(null, "*,Auto");
            grid.Add(UiClass.Small(Path.GetFileName(path)).Ellipsis().VCenter(), 0, 0);
            var rm = UiClass.Btn("移除", BtnKind.Ghost);
            rm.Click += (_, _) =>
            {
                _attachments.Remove(path);
                RenderAttachments();
            };
            grid.Add(rm.VCenter(), 0, 1);
            _attachList.Children.Add(grid);
        }
    }


    private static string TimeText(long unixSeconds)
    {
        if (unixSeconds <= 0) return "";
        try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
        catch (Exception) { return ""; }
    }

    private static string JoinMeta(params string[] parts)
        => string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
}
/// <summary>本页的链式小扩展(只在这里用:文本截断)。</summary>
internal static class FeedbackViewExt
{
    public static TextBlock Ellipsis(this TextBlock t)
    {
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }
}
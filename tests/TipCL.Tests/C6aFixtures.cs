using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using TipCL.Pages;
using TipCL.Services;
using TipCL.Themes;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C6a(下载 + 目录 + 安装)的全局静态作用域:进作用域时快照、<c>Dispose</c> 时还原(D-69 的纪律)。
///
/// 与 <see cref="C5aScope"/> 同一套做法,多盯两样 C6a 自己的可写静态:
/// <list type="bullet">
/// <item><see cref="CatalogPage.ClipboardReader"/>(读剪贴板的注入缝 —— 测试里**绝不碰真剪贴板**);</item>
/// <item>两个超时静态(<see cref="DownloadPage.FetchTimeoutMs"/> / <see cref="CatalogPage.SearchTimeoutMs"/>),
///   用例会把它们调小以验超时分支。</item>
/// </list>
/// 用例必须在 <c>AvaloniaSession.Run(...)</c> 的用例体里建这个作用域(D-79:会话线程上一次只跑一个用例体)。
/// </summary>
internal sealed class C6aScope : IDisposable
{
    private readonly BridgeClient? _client;

    private readonly Action<string, string, ToastKind>? _toastSink;

    private readonly Window? _window;

    private readonly Grid? _dlgHost;

    private readonly Control? _blur;

    private readonly bool _dlgSmoke;

    private readonly Action<Control, Action>? _autoAnswer;

    private readonly Action<string>? _autoCancel;

    private readonly bool _motionUserEnabled;

    private readonly double _motionScale;

    private readonly bool? _clockOverride;

    private readonly ThemeVariant? _variant;

    private readonly IReadOnlyDictionary<string, Func<PageBase>> _registry;

    private readonly Func<string>? _clipboardReader;

    private readonly int _fetchTimeoutMs;

    private readonly int _searchTimeoutMs;

    private readonly string? _extrasDir;

    private readonly string _themeColor;

    private readonly string _background;

    private bool _disposed;

    private C6aScope()
    {
        _client = AppServices.Client;
        _toastSink = AppServices.ToastSink;
        _window = AppServices.Window;
        _dlgHost = Dlg.Host;
        _blur = Dlg.BlurTarget;
        _dlgSmoke = Dlg.SmokeActive;
        _autoAnswer = Dlg.SmokeAutoAnswer;
        _autoCancel = Dlg.SmokeAutoCancelSystemDialog;
        _motionUserEnabled = Motion.UserEnabled;
        _motionScale = Motion.Scale;
        _clockOverride = Motion.ClockOverride;
        _variant = Application.Current!.RequestedThemeVariant;
        _registry = PageRegistry.Snapshot();
        _clipboardReader = CatalogPage.ClipboardReader;
        _fetchTimeoutMs = DownloadPage.FetchTimeoutMs;
        _searchTimeoutMs = CatalogPage.SearchTimeoutMs;
        _extrasDir = SettingsPage.ExtrasDirOverride;
        _themeColor = SettingsPage.VisualThemeColor;
        _background = SettingsPage.Extras.Background;
    }

    internal static C6aScope Capture()
    {
        return new C6aScope();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppServices.Client = _client;
        AppServices.ToastSink = _toastSink;
        AppServices.Window = _window;
        Dlg.Host = _dlgHost;
        Dlg.BlurTarget = _blur;
        Dlg.SmokeActive = _dlgSmoke;
        Dlg.SmokeAutoAnswer = _autoAnswer;
        Dlg.SmokeAutoCancelSystemDialog = _autoCancel;
        Motion.Enabled = _motionUserEnabled;
        Motion.Scale = _motionScale;
        Motion.ClockOverride = _clockOverride;
        Application.Current!.RequestedThemeVariant = _variant;
        PageRegistry.Restore(_registry);
        CatalogPage.ClipboardReader = _clipboardReader;
        DownloadPage.FetchTimeoutMs = _fetchTimeoutMs;
        CatalogPage.SearchTimeoutMs = _searchTimeoutMs;
        SettingsPage.ExtrasDirOverride = _extrasDir;
        SettingsPage.SyncPersistedThemeColor(_themeColor);
        SettingsPage.SyncPersistedBackground(_background);
        // 色板覆盖层也是全局量(C5a 的 PaletteOverride):测试里清干净,免得漏给别的用例。
        PaletteOverride.Reset();
    }
}

/// <summary>C6a 的公共小工具(转发既有的 C5a/C3b 夹具,不重新发明一套)。</summary>
internal static class C6aFiles
{
    /// <summary>一条 <c>get_version_list</c> 的行(字段名与后端 / <see cref="TipCL.Models.VersionRow"/> 一致)。</summary>
    internal static string VersionRowJson(string version, string type = "release", string date = "2026-01-01")
    {
        return "{\"version\":\"" + version + "\",\"type\":\"" + type + "\",\"date\":\"" + date + "\"}";
    }

    /// <summary>一条目录项(<c>search_*</c> / <c>catalog_favorites</c> 的行)。</summary>
    internal static string CatalogItemJson(string name, string source = "Modrinth", string slug = "demo", string author = "作者甲", long downloads = 1234, string description = "", string tags = "[]")
    {
        return "{\"name\":\"" + name + "\",\"author\":\"" + author + "\",\"downloads\":" + downloads +
               ",\"id\":\"" + slug + "\",\"slug\":\"" + slug + "\",\"source\":\"" + source + "\",\"description\":\"" + description +
               "\",\"tags\":" + tags + ",\"updated\":\"\",\"game_version\":\"1.20.1\",\"icon_url\":\"\",\"project_url\":\"\"}";
    }

    /// <summary>一条加载器版本(<c>list_loader_versions</c> 的行)。</summary>
    internal static string LoaderVerJson(string label, string id, bool stable = true)
    {
        return "{\"label\":\"" + label + "\",\"id\":\"" + id + "\",\"stable\":" + (stable ? "true" : "false") + "}";
    }

    /// <summary>一条版本文件(<c>list_catalog_files</c> 的行)。</summary>
    internal static string CatalogFileJson(string name, string filename, string versionNumber = "1.0.0", string releaseType = "release", string source = "modrinth")
    {
        return "{\"id\":\"7\",\"name\":\"" + name + "\",\"version_number\":\"" + versionNumber + "\",\"filename\":\"" + filename +
               "\",\"game_versions\":[\"1.20.1\"],\"loaders\":[\"fabric\"],\"date\":\"2026-01-01\",\"downloads\":9,\"release_type\":\"" + releaseType + "\",\"source\":\"" + source + "\"}";
    }

    /// <summary>
    /// 三个页面用的假后端:把这一簇会碰到的调用**全部**路由好(按方法名路由,不按请求序号 —— D-103)。
    /// 默认返回"空结果"这一族(未命中路由的方法回 null),用例按需覆写。
    /// </summary>
    internal static C3cFakeApi Api()
    {
        return new C3cFakeApi()
            .Route("get_version_list", "[" + VersionRowJson("1.20.1") + "," + VersionRowJson("1.21.2") + "," + VersionRowJson("1.20.1-pre1", "snapshot") + "," + VersionRowJson("b1.7.3", "old_beta") + "]")
            .Route("fetch_version_list", "[" + VersionRowJson("1.20.1") + "]")
            .Route("get_instances", "[]")
            .Route("get_setting", "\"\"")
            .Route("get_settings", "{}")
            .Route("catalog_favorites", "[]")
            .Route("toggle_favorite", "[]")
            .Route("list_catalog_files", "[]")
            .Route("get_mods_targets", "[{\"label\":\"实例共享目录\",\"value\":\"\"}]")
            .Route("get_installed_mod_entries", "[]")
            .Route("get_installed_mods", "[]")
            .Route("get_installed_modpacks", "[]")
            .Route("list_loader_versions", "[" + LoaderVerJson("0.15.11", "0.15.11") + "," + LoaderVerJson("0.14.9", "0.14.9", stable: false) + "]")
            .Route("default_export_dir", "\"\"")
            .Route("create_instance", "{}")
            // StartTaskAsync 只在 result 是**字符串**时返回它本身,否则返回整段 JSON 文本 ⇒ 这里必须是裸字符串
            .Route("install_game", "\"task-1\"")
            .Route("install_mod", "\"task-2\"")
            .Route("install_modpack", "\"task-3\"")
            .Route("wait_task", "{\"ok\":true,\"message\":\"\"}");
    }
}

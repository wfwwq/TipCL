// 来源: Windows 版参考仓库 src/TipCL/Pages/Plugins/PluginModels.cs（M2-B4 移植，仅改命名空间；
// JSON 读法保持 camelCase/snake_case 双认读）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

/// <summary>读后端 JSON 的小工具：同一个字段同时认 camelCase 和 snake_case（内核用哪种都行）。</summary>
internal static class J
{
    public static bool TryGet(JsonElement e, string key, out JsonElement v)
    {
        v = default;
        if (e.ValueKind != JsonValueKind.Object) return false;
        if (e.TryGetProperty(key, out v)) return true;
        string snake = Snake(key);
        if (snake != key && e.TryGetProperty(snake, out v)) return true;
        return false;
    }

    public static string Snake(string key)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in key)
        {
            if (char.IsUpper(c))
            {
                if (sb.Length > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Str(JsonElement e, string key, string def = "")
    {
        if (!TryGet(e, key, out JsonElement v)) return def;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? def,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => def,
        };
    }

    public static bool Bool(JsonElement e, string key, bool def = false)
    {
        if (!TryGet(e, key, out JsonElement v)) return def;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetDouble(out double d) && d != 0.0,
            JsonValueKind.String => string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => def,
        };
    }

    public static double Num(JsonElement e, string key, double def = 0.0)
    {
        if (!TryGet(e, key, out JsonElement v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d2)) return d2;
        return def;
    }

    public static List<string> StrList(JsonElement e, string key)
    {
        var list = new List<string>();
        if (TryGet(e, key, out JsonElement v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement x in v.EnumerateArray())
            {
                // 字符串，或 {id, ...} 对象（内核的权限行是 {id, label, danger}）
                string s = (x.ValueKind == JsonValueKind.String) ? (x.GetString() ?? "") : ((x.ValueKind == JsonValueKind.Object) ? Str(x, "id") : "");
                if (s.Length > 0) list.Add(s);
            }
        }
        return list;
    }

    public static JsonElement Obj(JsonElement e, string key)
    {
        if (TryGet(e, key, out JsonElement v) && v.ValueKind == JsonValueKind.Object) return v;
        return default;
    }

    public static IEnumerable<JsonElement> Items(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
            return e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList();
        // 有的接口可能包一层 {items:[...]} / {plugins:[...]}
        if (e.ValueKind == JsonValueKind.Object)
        {
            string[] keys = { "items", "plugins", "list", "providers" };
            foreach (string k in keys)
            {
                if (e.TryGetProperty(k, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                    return Items(arr);
            }
        }
        return Array.Empty<JsonElement>();
    }
}

public sealed class PluginInfo
{
    public string Id = "";
    public string Name = "";
    public string Version = "";
    public string Author = "";
    public string Description = "";
    public string Icon = "";
    public string Runtime = "node";
    public bool Enabled;
    public string State = "";
    public string Error = "";
    public string Source = "";
    public List<string> Permissions = new List<string>();
    public List<string> Granted = new List<string>();
    public bool HasSettings;
    public string DevPath = "";
    public bool UpdateAvailable;
    public string Homepage = "";

    public string DisplayName => (Name.Length > 0) ? Name : Id;

    public static PluginInfo From(JsonElement e)
    {
        return new PluginInfo
        {
            Id = J.Str(e, "id"),
            Name = J.Str(e, "name"),
            Version = J.Str(e, "version"),
            Author = J.Str(e, "author"),
            Description = J.Str(e, "description"),
            Icon = J.Str(e, "icon"),
            Runtime = J.Str(e, "runtime", "node"),
            Enabled = J.Bool(e, "enabled"),
            State = J.Str(e, "state"),
            Error = J.Str(e, "error"),
            Source = J.Str(e, "source"),
            Permissions = J.StrList(e, "permissions"),
            Granted = J.StrList(e, "granted"),
            HasSettings = J.Bool(e, "hasSettings"),
            DevPath = J.Str(e, "devPath"),
            UpdateAvailable = J.Bool(e, "updateAvailable"),
            Homepage = J.Str(e, "homepage")
        };
    }
}

public sealed class MarketItem
{
    public string Id = "";
    public string Name = "";
    public string Version = "";
    public string Author = "";
    public string Description = "";
    public string Icon = "";
    public string Homepage = "";
    public List<string> Tags = new List<string>();
    public bool Verified;
    public string Runtime = "node";
    public List<string> Permissions = new List<string>();
    public List<string> Screenshots = new List<string>();
    public string Changelog = "";
    public long Size;
    public long Downloads;
    public bool Installed;
    public string InstalledVersion = "";
    public bool Updatable;
    public string Market = "";

    public string DisplayName => (Name.Length > 0) ? Name : Id;

    public static MarketItem From(JsonElement e)
    {
        MarketItem m = new MarketItem
        {
            Id = J.Str(e, "id"),
            Name = J.Str(e, "name"),
            Version = J.Str(e, "version"),
            Author = J.Str(e, "author"),
            Description = J.Str(e, "description"),
            Icon = J.Str(e, "icon"),
            Homepage = J.Str(e, "homepage"),
            Tags = J.StrList(e, "tags"),
            Verified = J.Bool(e, "verified"),
            Runtime = J.Str(e, "runtime", "node"),
            Permissions = J.StrList(e, "permissions"),
            Screenshots = J.StrList(e, "screenshots"),
            Changelog = J.Str(e, "changelog"),
            Downloads = (long)J.Num(e, "downloads"),
            Installed = J.Bool(e, "installed"),
            InstalledVersion = J.Str(e, "installedVersion"),
            Updatable = J.Bool(e, "updatable"),
            Market = J.Str(e, "market")
        };
        JsonElement dl = J.Obj(e, "download");
        if (dl.ValueKind == JsonValueKind.Object)
        {
            m.Size = (long)J.Num(dl, "size");
        }
        return m;
    }
}

/// <summary>plugins_ui() 的一项：插件的一个界面贡献。</summary>
public sealed class UiContribution
{
    public string Plugin = "";
    public string Kind = "";
    public string Id = "";
    public string Label = "";
    public string Icon = "";
    public double Order;

    /// <summary>已带 pageToken 的视图地址；.NET 插件的原生界面为空。</summary>
    public string Url = "";

    public JsonElement Spec;

    /// <summary>插件+类型+id，界面缓存用的键。</summary>
    public string Key => Plugin + "|" + Kind + "|" + Id;

    /// <summary>侧栏 / 页面 id：plugin:&lt;plugin&gt;:&lt;id&gt;。</summary>
    public string PageId => "plugin:" + Plugin + ":" + Id;

    public string Group => J.Str(Spec, "group", "more");

    public bool NativeFlag;

    public bool Native => NativeFlag || J.Bool(Spec, "native");

    public List<string> Presets => J.StrList(Spec, "presets");

    public string Where => J.Str(Spec, "where");

    public static UiContribution From(JsonElement e)
    {
        UiContribution c = new UiContribution
        {
            Plugin = J.Str(e, "plugin"),
            Kind = J.Str(e, "kind"),
            Id = J.Str(e, "id"),
            Label = J.Str(e, "label"),
            Icon = J.Str(e, "icon"),
            Order = J.Num(e, "order"),
            Url = J.Str(e, "url"),
            NativeFlag = J.Bool(e, "native"),
            Spec = J.Obj(e, "spec").ValueKind == JsonValueKind.Object ? J.Obj(e, "spec").Clone() : default
        };
        if (c.Id.Length == 0) c.Id = J.Str(c.Spec, "id");
        if (c.Label.Length == 0) c.Label = J.Str(c.Spec, "label", c.Id);
        if (c.Icon.Length == 0) c.Icon = J.Str(c.Spec, "icon");
        if (c.Order == 0.0) c.Order = J.Num(c.Spec, "order");
        return c;
    }
}

/// <summary>
/// 插件界面贡献 kind 分发表（R3 §3.5）。Avalonia 版无 WebView2：page / aiView / overlay
/// 这三类网页承载面统一降级为「占位空态 + 在浏览器打开」；action / settings 一样给占位，
/// 插件运行时（宿主、RPC 事件流）M3 落地后逐类替换成真实实现。
/// </summary>
public static class UiDispatch
{
    public static Control Placeholder(UiContribution c)
    {
        string desc = c.Kind switch
        {
            "page" => "该插件提供了一个网页界面。Linux 版还没有内置网页视图，M3 接通插件运行时后在应用内打开。",
            "aiView" => "该插件提供了一个 AI 对话视图。插件运行时（M3）接通后可用。",
            "dashCard" => "该插件提供了首页卡片。插件运行时（M3）接通后显示。",
            "action" => "该插件注册了动作按钮。插件运行时（M3）接通后显示。",
            "overlay" => "主窗口浮层不再移植（Windows 专用的点击穿透层，R3 已标砍）。",
            _ => "该插件注册了一块自定义界面，类型未知。",
        };
        Control? open = null;
        if (c.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || c.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            open = U.Btn("在浏览器打开", BtnKind.Soft, delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c.Url) { UseShellExecute = true });
                }
                catch { }
            });
        }
        var host = U.V(10,
            U.Empty("⬒", c.Label.Length > 0 ? c.Label : "插件界面", desc),
            open);
        return host;
    }
}

/// <summary>已装插件卡片 logo 底色：按 id 从固定调色板取色（与主题无关，配白字明暗都成立）。</summary>
public static class PluginLogo
{
    internal static readonly string[] Palette = { "#7C5CFF", "#F59E0B", "#64748B", "#0EA5E9", "#10B981", "#E0559D" };

    public static IBrush BrushFor(string id)
    {
        int hash = 0;
        foreach (char c in id) hash = hash * 31 + c;
        return new SolidColorBrush(Color.Parse(Palette[Math.Abs(hash) % Palette.Length]));
    }
}

/// <summary>字节量格式化（Windows 版 Services/Fmt.Size 的等价）。</summary>
public static class Fmt
{
    public static string Size(long n)
    {
        if (n < 1024) return n + " B";
        if (n < 1024 * 1024) return (n / 1024.0).ToString("0.#") + " KB";
        if (n < 1024L * 1024 * 1024) return (n / 1024.0 / 1024).ToString("0.#") + " MB";
        return (n / 1024.0 / 1024 / 1024).ToString("0.#") + " GB";
    }
}

/// <summary>权限行的只读展示（Windows 版 PluginPerms.List 的轻量等价）。</summary>
public static class PluginPermsView
{
    public static Control List(IEnumerable<string> perms)
    {
        var host = U.V(6);
        bool any = false;
        foreach (string p in perms)
        {
            if (!PluginPermissions.IsKnown(p)) continue;
            any = true;
            host.Children.Add(U.RowCard(U.H(8, U.Txt(p, 12.5, bold: true), U.Small(PluginPermissions.Describe(p))).VCenter(), null, 8));
        }
        if (!any) host.Children.Add(U.Muted("（无已知权限）"));
        return host;
    }
}

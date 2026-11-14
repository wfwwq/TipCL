// ————— 插件设置子视图(按 manifest 的 config 自动生成表单) —————
// M4-H P6:从 PluginsPage.cs 拆出,与 Windows 版 Pages/Plugins/PluginsPage.Settings.cs 同名同分段。
//
// 与参考版的差别(见 docs/PARITY.md §7.1):参考版优先内嵌插件自己的「设置页」(WebView),
// 再退到自动表单,表单值经 plugins_get_config / plugins_set_config 走宿主;Linux 没有宿主与 WebView,
// 所以只做自动表单,值直接落在 plugins.json 的 config 里(插件将来能跑时读的就是这份)。
// secret 类型的配置项与 contributes.credentials:凭据要走 SecretStore 且只有插件进程会读 → 不呈现。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.App.Common;
using TipCL.App.Common.Plugins;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

public sealed partial class PluginsPage
{
    private sealed class FieldRow
    {
        public string Key = "";
        public string Type = "string";
        public string Label = "";
        public object? Initial;
        public Func<object?>? Read;
        public double? Min;
        public double? Max;
    }

    private void ShowSettings(PluginInfo p)
    {
        var host = U.V(10);
        host.Margin = new Thickness(26, 6, 26, 22);
        host.MaxWidth = 820;
        host.HorizontalAlignment = HorizontalAlignment.Left;
        ShowSubView($"{p.DisplayName} · 设置", new ScrollViewer { Content = host, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        FillAutoForm(p, host);
    }

    /// <summary>按 manifest 的 config 声明生成表单,值读 plugins.json、写回 plugins.json。</summary>
    private void FillAutoForm(PluginInfo p, StackPanel host)
    {
        host.Children.Clear();
        PluginManifest? m = null;
        string dir = Path.Combine(_dirs.PluginsRoot, p.Id);
        if (Directory.Exists(dir))
        {
            try { m = PluginManifest.LoadDir(dir); } catch { m = null; }
        }
        if (m == null)
        {
            host.Children.Add(U.Card(U.V(4,
                U.Section("读不到插件清单"),
                U.Muted("插件目录缺失或 tipcl-plugin.json 读不出来,没有可配置项。").Wrap())));
            return;
        }

        var values = (PluginRuntime.Host.StateStore.Get(p.Id)?.Config ?? new PyDict()).Copy();
        var defaults = m.ConfigDefaults();
        var rows = new List<FieldRow>();
        var card = U.V(2, U.Section("配置", "值保存在 plugins.json;插件运行时(Core 侧)读同一份"));
        int shown = 0;
        foreach (var kv in m.Config)
        {
            string key = kv.Key;
            if (kv.Value is not PyDict spec) continue;
            if (Py.Truthy(spec.Get("hidden", Py.Box(false)))) continue;
            if (Py.Truthy(spec.Get("secret", Py.Box(false)))) continue; // 凭据走 SecretStore,无宿主不呈现
            object? cur = values.ContainsKey(key) ? values[key] : defaults.Get(key);
            FieldRow row = BuildField(key, spec, cur, out Control control);
            rows.Add(row);
            string hint = Py.S(spec.Get("description"));
            card.Children.Add(U.Field(row.Label, control, hint.Length > 0 ? hint : null, 200));
            shown++;
        }
        if (shown == 0)
        {
            card.Children.Add(U.Muted("这个插件没有可以配置的项。"));
        }
        host.Children.Add(U.Card(card));

        var secretKeys = m.Config.Keys.Where(m.IsSecretConfig).ToList();
        if (secretKeys.Count > 0 || m.Credentials.Count > 0)
        {
            host.Children.Add(U.Card(U.V(2,
                U.Section("凭据", "API Key 之类"),
                U.Muted("凭据要交给插件宿主保管并只在插件进程里读取。Linux 版没有插件运行时,"
                        + "这里不呈现输入框,也不会把密钥写进任何文件。").Wrap())));
        }

        if (shown > 0)
        {
            var save = U.Btn("保存", BtnKind.Primary, delegate { SaveAutoForm(p, m, rows, defaults); });
            var reset = U.Btn("重新读取", BtnKind.Normal, delegate { ShowSettings(p); });
            host.Children.Add(U.H(8, save, reset));
        }
    }

    private static double ToDbl(object? o)
    {
        try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
        catch { return 0.0; }
    }

    private static FieldRow BuildField(string key, PyDict spec, object? val, out Control control)
    {
        string type = Py.S(spec.Get("type"));
        if (type.Length == 0) type = "string";
        var row = new FieldRow { Key = key, Type = type, Label = Py.S(Py.Or(spec.Get("label"), key)) };
        if (Py.IntOr(spec.Get("min"), long.MinValue) != long.MinValue) row.Min = ToDbl(spec.Get("min"));
        if (Py.IntOr(spec.Get("max"), long.MinValue) != long.MinValue) row.Max = ToDbl(spec.Get("max"));
        List<string> en = Py.ListOr(spec.Get("enum")).Select(Py.S).Where(x => x.Length > 0).ToList();

        switch (type)
        {
            case "boolean":
            {
                bool b = Py.Truthy(val ?? Py.Box(false));
                var sw = U.Switch(b);
                row.Initial = b;
                row.Read = () => sw.IsChecked == true;
                control = sw;
                return row;
            }
            case "integer":
            case "number":
                if (en.Count == 0)
                {
                    string text = val == null ? "" : Py.S(val);
                    var box = U.Input("", text, 160);
                    row.Initial = text;
                    row.Read = () => box.Text?.Trim() ?? "";
                    control = box;
                    return row;
                }
                break;
        }
        if (en.Count > 0)
        {
            string cur = Py.S(val ?? "");
            var combo = TipCL.App.Ui.Ui.Combo(en, en.Contains(cur) ? cur : en[0], 240);
            row.Initial = cur;
            row.Read = () => combo.SelectedItem as string ?? cur;
            control = combo;
            return row;
        }
        string s = val == null ? "" : Py.S(val);
        var input = U.Input(Py.S(spec.Get("placeholder")), s, 360);
        row.Initial = s;
        row.Read = () => input.Text ?? "";
        control = input;
        return row;
    }

    private void SaveAutoForm(PluginInfo p, PluginManifest m, List<FieldRow> rows, PyDict defaults)
    {
        var changes = new List<(string Key, object? Value)>();
        foreach (FieldRow r in rows)
        {
            object? cur = r.Read?.Invoke();
            if (Equals(cur, r.Initial)) continue;
            if (r.Type == "integer" || r.Type == "number")
            {
                string text = cur as string ?? "";
                if (text.Length == 0)
                {
                    changes.Add((r.Key, null));
                    continue;
                }
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    || (r.Type == "integer" && Math.Abs(d - Math.Round(d)) > 1e-9))
                {
                    ToastHost.Show("填写有误", $"「{r.Label}」要填{(r.Type == "integer" ? "整数" : "数字")}", ToastKind.Warning);
                    return;
                }
                if ((r.Min.HasValue && d < r.Min.Value) || (r.Max.HasValue && d > r.Max.Value))
                {
                    ToastHost.Show("填写有误", $"「{r.Label}」超出范围", ToastKind.Warning);
                    return;
                }
                changes.Add((r.Key, r.Type == "integer" ? (object)(long)Math.Round(d) : d));
            }
            else
            {
                changes.Add((r.Key, cur));
            }
        }
        if (changes.Count == 0)
        {
            ToastHost.Show("没有改动", p.DisplayName);
            return;
        }
        try
        {
            PluginRuntime.Host.StateStore.Update(p.Id, r =>
            {
                foreach (var (key, value) in changes)
                {
                    // 走 Core 的类型校验(enum / min-max / 类型转换),校验不过就整批不做
                    object coerced = value == null ? value! : m.CoerceConfig(key, value);
                    if (value == null) r.Config.Remove(key);
                    else r.Config[key] = coerced;
                }
            });
        }
        catch (Exception ex)
        {
            ToastHost.Show("保存失败", ex.Message, ToastKind.Error);
            return;
        }
        ToastHost.Show("已保存", $"{changes.Count} 项设置", ToastKind.Success);
        ShowSettings(p);
    }

    /// <summary>页面内子视图:顶部带返回。</summary>
    private void ShowSubView(string title, Control content)
    {
        var back = U.IconBtn(BackGlyph, "返回", delegate { SelectTab(_tab); });
        var head = U.G(null, "Auto,*,Auto");
        head.Add(back.VCenter(), 0);
        head.Add(U.H2(title).VCenter().Mk(6, 0, 0, 0), 0, 1);
        head.Margin = new Thickness(18, 12, 22, 8);
        var g = U.G("Auto,*");
        g.Add(head, 0);
        g.Add(content, 1);
        _body.Content = g;
    }
}

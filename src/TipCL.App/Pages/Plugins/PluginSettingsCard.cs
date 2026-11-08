// 来源: Windows 版参考仓库 src/TipCL/Pages/Plugins/PluginSettingsCard.cs（M2-B4 移植）。
// 差异：Windows 版市场索引 / 加速镜像 / 未审核开关经 save_settings 存后端；Linux 版 M2 无
// RPC 桥，直接落 Core 的 LauncherConfig（config.json）——Core 的 PluginMarket.EnabledMirrors/
// IndexUrls 本来就从同一份配置读取，语义闭环；M3 接桥后把 SaveAsync 换回 save_settings。
using System;
using System.Collections.Generic;
using System.Linq;
using TipCL.App.Common;
using TipCL.App.Ui;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Core;
using TipCL.Core.Plugins;

namespace TipCL.App.Pages.Plugins;

/// <summary>
/// 设置页里的「插件」卡片：市场索引地址（plugin_market_indexes）、加速镜像（plugin_mirrors）、
/// 允许未审核插件（plugin_allow_unverified）、管理插件 / 模型供应商入口。随改随存。
/// </summary>
public static class PluginSettingsCard
{
    public static List<string> ParseIndexes(object? v)
    {
        var list = new List<string>();
        foreach (string s in Py.ListOr(v).Select(Py.S))
        {
            // 容忍一整行多个分隔符（对齐 Windows 版对字符串值的宽松解析）
            if (s.Contains('\n') || s.Contains('\r') || s.Contains(',') || s.Contains(';'))
            {
                list.AddRange(s.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else if (s.Trim().Length > 0)
            {
                list.Add(s);
            }
        }
        return Clean(list);
    }

    public static List<string> Clean(IEnumerable<string> lines)
    {
        return (from s in lines
                select s.Trim() into s
                where s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                select s).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>写回 config.json（M3 换 save_settings RPC）。</summary>
    public static void Save(string key, object value)
    {
        Config.CONFIG.Set(key, value);
        Config.CONFIG.Save();
    }

    public static List<PluginMarket.GhMirror> LoadMirrors()
    {
        // 三态语义与 Core.PluginMarket.EnabledMirrors 一致：配置缺失 = 内置默认全开
        object sentinel = new object();
        object v = Config.CONFIG.Get(PluginMarket.SettingMirrors, sentinel);
        if (ReferenceEquals(v, sentinel)) return PluginMarket.DefaultMirrors();
        var parsed = ParseMirrors(v);
        return parsed.Count > 0 ? parsed : PluginMarket.DefaultMirrors();
    }

    public static List<PluginMarket.GhMirror> ParseMirrors(object? v)
    {
        var list = new List<PluginMarket.GhMirror>();
        foreach (object item in Py.ListOr(v).OfType<object>())
        {
            if (item is not PyDict x) continue;
            string id = Py.S(x.Get("id")).Trim();
            string raw = Py.S(x.Get("raw_prefix"));
            string dl = Py.S(x.Get("download_prefix"));
            if (id.Length == 0 || !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !dl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new PluginMarket.GhMirror
            {
                Id = id,
                Label = Py.S(Py.Or(x.Get("name"), id)),
                RawPrefix = raw,
                DownloadPrefix = dl,
                Enabled = Py.Truthy(x.Get("enabled", true)),
                Priority = (int)Py.Int(Py.Or(x.Get("priority"), 999L)),
            });
        }
        return list;
    }

    public static PyDict MirrorDict(PluginMarket.GhMirror m)
    {
        return PyDict.Of(
            "id", m.Id,
            "name", m.Label,
            "raw_prefix", m.RawPrefix,
            "download_prefix", m.DownloadPrefix,
            "enabled", m.Enabled,
            "priority", (long)m.Priority);
    }

    public static Border Build()
    {
        var summary = U.Small("");
        List<string> indexes = ParseIndexes(Config.CONFIG.Get(PluginMarket.SettingIndexes));
        var unverified = U.Switch(Py.Truthy(Config.CONFIG.Get(PluginMarket.SettingAllowUnverified)));
        // —— 加速镜像：每行 = 一个镜像（enabled + priority），↑↓ 交换 priority，整份落 config.json ——
        var mirrorHost = U.V(8);
        List<PluginMarket.GhMirror> mirrors = LoadMirrors();
        bool mirrorSyncing = false;

        void SaveMirrors()
        {
            Save(PluginMarket.SettingMirrors, mirrors.OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal).Select(m => (object)MirrorDict(m)).ToList());
            ToastHost.Show("已保存", "镜像设置已更新", ToastKind.Success);
        }

        void RenderMirrors()
        {
            mirrorHost.Children.Clear();
            mirrors = mirrors.OrderBy(m => m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            foreach (PluginMarket.GhMirror m in mirrors.ToList())
            {
                bool on = m.Enabled;
                var sw = U.Switch(on);
                var label = U.Txt(m.Label, 12.5, bold: true);
                var sub = U.Muted("Raw: " + m.RawPrefix);
                var up = U.IconBtn("↑", "上移");
                var down = U.IconBtn("↓", "下移");
                int index = mirrors.IndexOf(m);
                up.IsEnabled = index > 0;
                down.IsEnabled = index < mirrors.Count - 1;
                sw.IsCheckedChanged += delegate
                {
                    if (mirrorSyncing) return;
                    m.Enabled = sw.IsChecked == true;
                    SaveMirrors();
                    RenderMirrors();
                };
                void MoveMirror(bool isUp)
                {
                    if (mirrorSyncing) return;
                    int cur = mirrors.IndexOf(m);
                    int other = isUp ? cur - 1 : cur + 1;
                    if (cur < 0 || other < 0 || other >= mirrors.Count) return;
                    (mirrors[cur].Priority, mirrors[other].Priority) = (mirrors[other].Priority, mirrors[cur].Priority);
                    SaveMirrors();
                    RenderMirrors();
                }
                up.Click += delegate { MoveMirror(true); };
                down.Click += delegate { MoveMirror(false); };
                var actions = U.H(4, up, down);
                if (m.Id.StartsWith(PluginMarket.CustomMirrorPrefix, StringComparison.Ordinal))
                {
                    var del = U.IconBtn("✕", "删除");
                    del.Click += delegate
                    {
                        mirrors.Remove(m);
                        SaveMirrors();
                        RenderMirrors();
                    };
                    actions.Children.Add(del);
                }
                var row = U.Card(U.G(null, "Auto,*,Auto")
                    .Add(sw.VCenter(), 0)
                    .Add(U.V(1, label, sub).Mk(10, 0, 6, 0), 0, 1)
                    .Add(actions, 0, 2), 12);
                row.Opacity = on ? 1.0 : 0.62;
                mirrorHost.Children.Add(row);
            }
        }

        var mirrorReset = U.Btn("恢复默认", BtnKind.Ghost, delegate
        {
            mirrors = PluginMarket.DefaultMirrors();
            SaveMirrors();
            RenderMirrors();
        });
        var mirrorAdd = U.Btn("添加自定义镜像", BtnKind.Soft, async delegate
        {
            var nameBox = U.Input("我的镜像");
            var rawBox = U.Input("https://example.com/https://raw.githubusercontent.com");
            var dlBox = U.Input("https://example.com/https://github.com");
            var body = U.V(8,
                U.Field("名称", nameBox),
                U.Field("Raw 前缀", rawBox, "完整前缀，含 https://raw.githubusercontent.com"),
                U.Field("下载前缀", dlBox, "完整前缀，含 https://github.com"));
            if (!await Dlg.Ask("添加自定义镜像", body)) return;
            string name = nameBox.Text.Trim();
            string rawPrefix = rawBox.Text.Trim().TrimEnd('/');
            string dlPrefix = dlBox.Text.Trim().TrimEnd('/');
            if (name.Length == 0 || !rawPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !dlPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ToastHost.Show("填得不对", "名称必填；两个前缀都要以 https:// 开头", ToastKind.Warning);
                return;
            }
            int maxPriority = mirrors.Count > 0 ? mirrors.Max(m => m.Priority) : 0;
            mirrors.Add(new PluginMarket.GhMirror
            {
                Id = PluginMarket.CustomMirrorPrefix + Guid.NewGuid().ToString("N").Substring(0, 8),
                Label = name,
                RawPrefix = rawPrefix,
                DownloadPrefix = dlPrefix,
                Enabled = true,
                Priority = maxPriority + 1
            });
            SaveMirrors();
            RenderMirrors();
        }, "⬒");

        bool syncing = false;
        unverified.IsCheckedChanged += async delegate
        {
            if (syncing) return;
            bool on = unverified.IsChecked == true;
            if (on && !await Dlg.Confirm("允许安装未审核插件？", "未审核的插件没人检查过来源和代码。插件拥有完整的电脑权限，只安装你信任的作者发布的插件。"))
            {
                syncing = true;
                unverified.IsChecked = false;
                syncing = false;
                return;
            }
            Save(PluginMarket.SettingAllowUnverified, on);
        };

        var edit = U.Btn("编辑", BtnKind.Soft, async delegate
        {
            var next = await EditIndexesDialogAsync(indexes);
            if (next != null)
            {
                indexes = next;
                Sync();
            }
        }, "✎");

        var manage = U.Btn("管理插件", BtnKind.Normal, delegate
        {
            LayoutFx.Navigate("plugins");
        }, "⬒");

        var card = U.Card(U.V(2,
            U.Section("插件", "插件市场、加速镜像、未审核插件"),
            U.Field("插件市场地址", U.H(10, edit, summary.VCenter()), "填写插件市场的索引地址，可以填多个"),
            U.Field("", U.V(6,
                U.Txt("加速镜像", 13, bold: true),
                U.Small("开关与顺序即优先级：排在前面的先尝试，全部失败才报错。").Mk(0, 0, 0, 0),
                mirrorHost,
                U.H(6, mirrorAdd, mirrorReset))),
            U.Field("允许安装未审核插件", unverified, "关着时，市场里未审核的插件要先打开这里才能安装"),
            U.Field("", U.H(8, manage))));

        Sync();
        RenderMirrors();
        return card;

        void Sync()
        {
            summary.Text = (indexes.Count == 0) ? "还没有填写" : ((indexes.Count == 1) ? indexes[0] : $"{indexes.Count} 个市场");
            if (indexes.Count > 0) ToolTip.SetTip(summary, string.Join("\n", indexes));
        }
    }

    /// <summary>编辑市场地址；确认返回清洗后的列表，取消返回 null（Windows 版 Dlg.Form 的等价）。</summary>
    public static async Task<List<string>?> EditIndexesDialogAsync(List<string> current)
    {
        var box = U.Multi("https://example.com/tipcl-plugins/index.json", string.Join("\n", current), 140);
        box.MinWidth = 480;
        var body = U.V(8, U.Muted("一行一个插件市场索引地址（index.json）。多个市场按插件 id 合并，排在前面的优先。").Wrap(), box);
        if (!await Dlg.Ask("插件市场地址", body)) return null;
        var raw = box.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var clean = Clean(raw);
        if (clean.Count != raw.Count)
        {
            ToastHost.Show("填得不对", "地址要以 http:// 或 https:// 开头", ToastKind.Warning);
            return null;
        }
        Save(PluginMarket.SettingIndexes, clean.Cast<object>().ToList());
        ToastHost.Show("已保存", $"{clean.Count} 个市场地址", ToastKind.Success);
        return clean;
    }
}

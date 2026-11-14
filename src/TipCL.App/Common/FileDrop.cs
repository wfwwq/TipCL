// M4-H P7:统一拖放入口。
// 来源:Windows 参考仓库 src/TipCL/Services/FileDrop.cs(291 行)+ PageBase.EnableDrop。
//
// 口径(见 docs/PARITY.md §5 的 FileDrop/FileKinds 行):
// - 与参考版同形:识别 → 分桶(认得准的一个桶,认不准的问一次)→ 逐桶落到真实安装链;
//   插件包走本地安装,整合包确认后装成新版本,模组/资源包/光影/数据包/存档进下载任务,皮肤设给离线账号。
// - 与参考版的差别:没有壁纸(wallpaper)分支 —— Linux 启动页没有自定义壁纸的消费方,列出来是假入口;
//   整合包不做嵌套包探测(参考版会递归解包找 manifest),只按包内标志识别。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TipCL.App.Common.Plugins;
using TipCL.App.Pages.Layout;
using TipCL.App.Shell;
using TipCL.App.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Plugins;

namespace TipCL.App.Common;

/// <summary>把拖进来的文件认出来并落到对应安装链。</summary>
public static class FileDrop
{
    /// <summary>拖放上下文:页面知道自己当前的实例 / 版本,识别不出类型时也可以给个偏好。</summary>
    public sealed class DropContext
    {
        public static readonly DropContext None = new DropContext();

        public string Instance { get; init; } = "";

        public string Version { get; init; } = "";

        public string PreferKind { get; init; } = "";
    }

    /// <summary>
    /// 给一个控件挂上拖放(参考版 PageBase.EnableDrop 的等价物,可重复调用)。
    /// 认不出类型的文件会问一次;处理结果走 Toast,安装类跳到「下载任务」。
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Attached = new();

    public static void Attach(Control target, Func<DropContext>? context = null)
    {
        // 幂等:页面可能在 OnActivate 里挂(每次激活都调),重复挂会重复处理同一次拖放
        if (Attached.TryGetValue(target, out _)) return;
        Attached.Add(target, new object());
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        });
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.Data.GetFiles()?.ToList();
            if (files == null || files.Count == 0) return;
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();
            if (paths.Count == 0) return;
            e.Handled = true;
            _ = HandleAsync(target, paths, context?.Invoke());
        });
    }

    /// <summary>处理一批拖进来的路径。</summary>
    public static async Task HandleAsync(Control owner, IReadOnlyList<string> paths, DropContext? context = null)
    {
        DropContext ctx = context ?? DropContext.None;
        if (paths.Count == 0) return;
        try
        {
            var verdicts = await Task.Run(() => paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(FileKinds.Identify)
                .ToList());
            if (verdicts.Count == 0) return;

            var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var unsure = new List<FileKinds.Verdict>();
            foreach (var v in verdicts)
            {
                string kind = (v.Sure && v.Kinds.Count == 1)
                    ? v.Kinds[0]
                    : (ctx.PreferKind.Length > 0 && v.Kinds.Contains(ctx.PreferKind) ? ctx.PreferKind : "");
                if (kind.Length > 0)
                {
                    if (!buckets.TryGetValue(kind, out var list)) buckets[kind] = list = new List<string>();
                    list.Add(v.Path);
                }
                else
                {
                    unsure.Add(v);
                }
            }

            if (unsure.Count > 0)
            {
                var head = unsure[0];
                var same = unsure.Where(x => x.KindSig == head.KindSig).ToList();
                string picked = await AskKindAsync(head, same.Count - 1);
                if (picked.Length > 0)
                {
                    if (!buckets.TryGetValue(picked, out var list)) buckets[picked] = list = new List<string>();
                    list.AddRange(same.Select(x => x.Path));
                }
                var rest = unsure.Except(same).ToList();
                if (rest.Count > 0)
                {
                    ToastHost.Show("有文件没处理",
                        "这些认不出来,可以单独拖进来再选:" + string.Join("、", rest.Take(3).Select(x => x.Name)),
                        ToastKind.Warning);
                }
            }

            foreach (var kv in buckets)
            {
                try
                {
                    string message = await ApplyAsync(owner, kv.Key, kv.Value, ctx);
                    if (message.Length > 0) ToastHost.Show(FileKinds.Label(kv.Key), message, ToastKind.Success);
                }
                catch (Exception ex)
                {
                    ToastHost.Show("没能处理", Path.GetFileName(kv.Value[0]) + " — " + ex.Message, ToastKind.Error);
                }
            }
        }
        catch (Exception ex)
        {
            ToastHost.Show("拖放处理失败", ex.Message, ToastKind.Error);
        }
    }

    /// <summary>问用户「这个文件放哪儿」;取消返回空串。</summary>
    private static async Task<string> AskKindAsync(FileKinds.Verdict info, int others)
    {
        var kinds = info.Kinds.Count > 0 ? info.Kinds : FileKinds.AllKinds.ToList();
        var picks = new List<(RadioButton Btn, string Kind)>();
        var list = U.V(6);
        foreach (string k in kinds)
        {
            var rb = new RadioButton
            {
                Content = U.Txt(FileKinds.Label(k) + " — " + FileKinds.Action(k)),
                IsChecked = picks.Count == 0,
                Margin = new Avalonia.Thickness(0, 2, 0, 2),
            };
            picks.Add((rb, k));
            list.Children.Add(rb);
        }
        var body = U.V(8,
            U.Txt(info.Name, 13.5, bold: true).Wrap(),
            U.Muted(info.Detail).Wrap(),
            list);
        if (others > 0)
        {
            body.Children.Add(U.Small($"这一批里还有 {others} 个同样认不准的文件,会照同一个选择处理。").Wrap());
        }
        if (!await Dlg.Ask("这个文件放哪儿?", body, "就这么放", "算了")) return "";
        return picks.FirstOrDefault(p => p.Btn.IsChecked == true).Kind ?? "";
    }

    private static async Task<string> ApplyAsync(Control owner, string kind, List<string> paths, DropContext ctx)
    {
        var window = TopLevel.GetTopLevel(owner) as MainWindow;
        switch (kind)
        {
            case FileKinds.Plugin:
            {
                foreach (string path in paths)
                {
                    await Task.Run(() => PluginRuntime.InstallLocal(path, dev: false,
                        null, line => AppLog.Info("[插件安装] " + line)));
                }
                window?.RefreshPluginUi();
                string names = string.Join("、", paths.Select(Path.GetFileName));
                return $"已安装 {names}(插件不会被执行,原因见插件页说明)";
            }
            case FileKinds.Modpack:
                await ImportModpackAsync(owner, paths[0]);
                return "";
            case FileKinds.Skin:
                return await SetSkinAsync(paths[0]);
            default:
            {
                string method = kind switch
                {
                    FileKinds.Mod => "install_mod",
                    FileKinds.ResourcePack => "install_resourcepack",
                    FileKinds.ShaderPack => "install_shader",
                    FileKinds.DataPack => "install_datapack",
                    FileKinds.World => "install_world",
                    _ => "",
                };
                if (method.Length == 0) throw new InvalidOperationException("还不支持这种文件");
                string instance = ctx.Instance.Length > 0 ? ctx.Instance : CurrentInstance();
                foreach (string path in paths)
                {
                    var extra = new Dictionary<string, object?>
                    {
                        ["path"] = path,
                        ["instance"] = instance,
                        ["version"] = ctx.Version,
                        ["source"] = "本地",
                    };
                    await InProcessApi.Instance.CallAsync<string>(method, new
                    {
                        name = Path.GetFileName(path),
                        instance,
                        extra,
                    });
                }
                window?.Navigate("tasks");
                string where = ctx.Instance.Length > 0 ? $"实例「{instance}」" : "默认实例";
                return $"已加入下载任务,装进{where}的{FileKinds.Label(kind)}";
            }
        }
    }

    /// <summary>整合包:确认名字后装成一个新版本(照参考版 FileDrop.ImportModpackAsync)。</summary>
    public static async Task ImportModpackAsync(Control owner, string path)
    {
        string suggest = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
        var name = TiCLInput(suggest);
        var body = U.V(8,
            U.Txt(suggest, 14, bold: true).Wrap(),
            U.Small(path).Wrap(),
            U.Field("装成版本", name, "会建成一个新版本,原有版本不受影响", 78));
        if (!await Dlg.Ask("安装整合包", body, "开始安装")) return;
        string target = (name.Text ?? "").Trim();
        if (target.Length == 0) target = suggest;
        var extra = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["name"] = target,
            ["source"] = "本地",
            ["kind"] = "modpack",
        };
        await InProcessApi.Instance.CallAsync<string>("install_modpack", new { name = target, source = "本地", extra });
        (TopLevel.GetTopLevel(owner) as MainWindow)?.Navigate("tasks");
        ToastHost.Show("已加入队列", $"正在安装「{target}」", ToastKind.Success);
    }

    private static TextBox TiCLInput(string text) => U.Input("新版本名称", text, 320);

    private static async Task<string> SetSkinAsync(string path)
    {
        var rows = await InProcessApi.Instance.TryCallAsync<List<object?>>("get_account_rows", null) ?? new List<object?>();
        var offline = rows.Select(r => Py.D(r!)).Where(d => Py.Eq(d.Get("type"), "offline")).ToList();
        if (offline.Count == 0)
        {
            throw new InvalidOperationException("还没有离线账号;先到「账号」页建一个,再把皮肤拖进来");
        }
        var target = offline.FirstOrDefault(d => Py.Truthy(d.Get("active"))) ?? offline[0];
        string name = Py.S(target.Get("name"));
        await InProcessApi.Instance.CallAsync<object>("set_account_skin", new { name, path });
        return $"已把皮肤设为「{name}」";
    }

    /// <summary>没给上下文的兜底:默认实例(照参考版 CurrentInstanceAsync:配置 → 第一个实例)。</summary>
    private static string CurrentInstance()
    {
        try
        {
            string configured = Py.S(Config.CONFIG.Get("default_instance"));
            if (configured.Length > 0) return configured;
        }
        catch (Exception)
        {
        }
        try
        {
            var first = Instances.ListInstances().FirstOrDefault();
            if (first != null) return Py.S(first);
        }
        catch (Exception)
        {
        }
        return "default";
    }
}

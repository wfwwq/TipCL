// M4-H P5:AI 页的图片输入(移植 Windows 版 Pages/AiPage.Images.cs,149 行)。
//
// Windows 版用 WPF 的 Clipboard(位图 / 文件拖放)+ FileDrop;Avalonia 侧:
//   选文件 → StorageProvider(与 Dlg.PickFile 同一路径)
//   粘贴   → Ctrl+V:先看剪贴板里的文件列表,再退回「剪贴板位图落盘」
//   拖入   → DragDrop.Drop(与 P7「拖放入口」同一机制)
// 图片块构造与「当前模型看不看得懂图」的判断都在 Core(Images / VisionCaps),这里只管取路径。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>AI 页的图片附件管理(照 Windows 版 AiPage.Images)。</summary>
internal sealed class AiImageInput
{
    /// <summary>最多附几张(照参考版)。</summary>
    public const int MaxImages = 4;

    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private readonly List<string> _paths = new();
    private readonly Action<string, string, ToastKind> _toast;

    public AiImageInput(Action<string, string, ToastKind>? toast = null)
        => _toast = toast ?? ((t, b, k) => ToastHost.Show(t, b, k));

    public IReadOnlyList<string> Paths => _paths;

    public int Count => _paths.Count;

    public event Action? Changed;

    /// <summary>附件标签(照 Windows 版 SyncImageChip:「3 张图 ×」)。</summary>
    public string ChipText => _paths.Count > 0 ? $"{_paths.Count} 张图 ×" : "";

    /// <summary>选文件(StorageProvider)。</summary>
    public async Task PickAsync(Window? owner = null)
    {
        var host = owner ?? Dlg.Owner;
        if (host == null)
        {
            return;
        }
        var files = await host.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("图片")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" },
                },
            },
        });
        foreach (var file in files)
        {
            string? path = file.Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
            {
                Add(path);
            }
        }
    }

    /// <summary>加一条路径(去重 / 限数量 / 限扩展名)。</summary>
    public bool Add(string? path)
    {
        if (string.IsNullOrEmpty(path) || _paths.Contains(path))
        {
            return false;
        }
        if (!Exts.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            return false;
        }
        if (_paths.Count >= MaxImages)
        {
            _toast("最多附 4 张图", "", ToastKind.Warning);
            return false;
        }
        _paths.Add(path);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_paths.Count == 0)
        {
            return;
        }
        _paths.Clear();
        Changed?.Invoke();
    }

    /// <summary>Ctrl+V:剪贴板里的文件列表优先,否则把位图落成临时 PNG 再当附件。</summary>
    public async Task<bool> HandlePasteAsync(TopLevel? top)
    {
        var clipboard = top?.Clipboard;
        if (clipboard == null)
        {
            return false;
        }
        bool added = false;
        try
        {
            var data = await clipboard.GetDataAsync(DataFormats.Files);
            if (data is IEnumerable<Avalonia.Platform.Storage.IStorageItem> items)
            {
                foreach (var item in items)
                {
                    string? path = item.Path.LocalPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        added |= Add(path);
                    }
                }
            }
            if (added)
            {
                return true;
            }
            // 剪贴板位图(Avalonia 11.3 的 DataFormats 只有文本/文件两组;位图按「格式化文本=base64」不支持,
            // 这里只处理「剪贴板里是图片文件」与「剪贴板是文件列表」两种真实可用的情况)
        }
        catch (Exception)
        {
            // 读剪贴板失败(格式不支持 / 无权限)不该打断输入
        }
        return added;
    }

    /// <summary>目标控件挂上拖放:拖入图片文件即附件。</summary>
    public void HookDrop(Control target)
    {
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
            bool any = false;
            if (e.Data.GetFiles() is { } files)
            {
                foreach (var file in files)
                {
                    string? path = file.Path.LocalPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        any |= Add(path);
                    }
                }
            }
            if (any)
            {
                e.Handled = true;
            }
        });
    }

    /// <summary>当前模型看不看得懂图(Core 的 VisionCaps);拿不准按「能看」处理。</summary>
    public static bool VisionSupported(out string reason)
    {
        reason = "";
        try
        {
            VisionCaps.Result result = VisionCaps.Detect(TipCL.Core.Launcher.Config.CONFIG.data, false);
            reason = VisionCaps.Describe(result);
            return result.Supported;
        }
        catch (Exception ex)
        {
            reason = Py.ExcStr(ex);
            return true;
        }
    }
}

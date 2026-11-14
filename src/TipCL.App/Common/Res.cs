using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace TipCL.App.Common;

/// <summary>Common 内部资源取值小工具（不改 A2 的 UiHelpers；Brush 复用 Ui 门面）。</summary>
internal static class Res
{
    /// <summary>资源键 → DynamicResource 观察绑定（明暗/主题色切换即时跟随）。</summary>
    public static IBinding Bind(string key)
        => Application.Current!.GetResourceObservable(key).ToBinding();
}

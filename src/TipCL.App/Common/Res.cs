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
        => BindFrom(Application.Current, key);

    /// <summary>
    /// AI2-stability:资源宿主为空(Application 已不在 —— 进程退出途中 / 无头测试会话重置时冲刷旧队列)时
    /// 不再抛 ArgumentNullException,而是给一个「未设置」绑定(属性保持默认值)。与 Ui.Dyn 的空安全口径一致。
    /// </summary>
    internal static IBinding BindFrom(IResourceHost? host, string key)
        => host != null ? host.GetResourceObservable(key).ToBinding() : UnsetObservable.Instance.ToBinding();

    private sealed class UnsetObservable : IObservable<object?>
    {
        public static readonly UnsetObservable Instance = new();

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            observer.OnNext(AvaloniaProperty.UnsetValue);
            return Nothing.Instance;
        }

        private sealed class Nothing : IDisposable
        {
            public static readonly Nothing Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

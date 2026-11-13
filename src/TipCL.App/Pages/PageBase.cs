using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 所有页面的基类。**成员名、语义与 Windows 版 <c>Pages\PageBase.cs</c> 一一对应**
/// (<c>Id</c> / <c>Title</c> / <c>Api</c> / <c>Win</c> / <c>EnsureLoadedAsync</c> / <c>LoadAsync</c> /
/// <c>RefreshAsync</c> / <c>OnEvent</c> / <c>OnShown</c> / <c>OnHidden</c> / <c>Toast</c> / <c>Run</c> /
/// <c>Body</c> / <c>ScrollBody</c>),C3+ 的页面移植因此是机械的。
///
/// 四处 mac 侧差异(逐条记在 PROGRESS.md 的 C2b 差异表):
/// <list type="bullet">
/// <item><c>UserControl</c> → <c>UserControl</c>(Avalonia 有同名类型),<c>Focusable</c> 在构造里置 false;</item>
/// <item><c>Win</c> 的静态类型:<c>MainWindow</c> 在 mac 侧是 internal(C2a 的自由),所以这个成员是
/// <c>internal static</c>(不是 protected):程序集内派生页面照样不带前缀直接用,而 public 类型上
/// 不能有暴露 internal 类型的 protected 成员(CS0053);</item>
/// <item><c>Toast</c> 走 <see cref="AppServices.Toast"/> —— 外壳类还没落地前 C2a 不能凭空依赖它的成员
/// (Toast 的视觉件属 C2c);</item>
/// <item><see cref="Smoke"/> 侧入口叫 <c>SmokeState</c>(命名空间与类型不能同名,见 SmokeState 的注释);
/// <c>EnableDrop</c> **本包不移植**(它依赖 <c>FileDrop.DropContext</c>,属 C2c 的拖放浮层)。</item>
/// </list>
/// </summary>
public abstract class PageBase : UserControl
{
	private bool _loaded;

	private Task? _inflight;

	public string Id { get; set; } = "";

	public virtual string Title => "";

	/// <summary>后端客户端(与 Windows 同名;后端没起来时是 null,页面自己按 Windows 的写法处理)。</summary>
	protected static BridgeClient Api => AppServices.Client;

	/// <summary>外壳窗口(见类注释的差异说明:类型仍是 <c>MainWindow</c>,只是成员可访问性降为 internal)。</summary>
	internal static MainWindow? Win => AppServices.Window as MainWindow;

	protected PageBase()
	{
		Focusable = false;
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
	}

	/// <summary>首次显示时加载一次(<c>LoadAsync</c> 只跑一次),失败记一笔并 Toast,不把异常抛给外壳。</summary>
	public Task EnsureLoadedAsync()
	{
		if (_loaded)
		{
			return Task.CompletedTask;
		}
		return _inflight ?? (_inflight = LoadOnceAsync());
	}

	private async Task LoadOnceAsync()
	{
		try
		{
			await LoadAsync();
			_loaded = true;
		}
		catch (Exception ex)
		{
			Smoke.SmokeState.Note(string.IsNullOrEmpty(Id) ? GetType().Name : Id, ex.ToString());
			Toast(I18n.L("加载失败"), ex.Message, ToastKind.Error);
		}
		finally
		{
			_inflight = null;
		}
	}

	protected virtual Task LoadAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>刷新(<c>RefreshAsync</c>):没加载过就什么都不做,加载过才重跑 <c>LoadAsync</c>(Windows 同)。</summary>
	public virtual Task RefreshAsync()
	{
		if (!_loaded)
		{
			return Task.CompletedTask;
		}
		return LoadAsync();
	}

	public virtual void OnEvent(BridgeEvent ev)
	{
	}

	public virtual void OnShown()
	{
	}

	public virtual void OnHidden()
	{
	}

	protected static void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		AppServices.Toast(title, body, kind);
	}

	/// <summary>
	/// 跑一段异步活儿:取消异常吞掉,其它异常记一笔 + Toast,并在离屏记账里包一次操作
	/// (Windows 同名静态方法;失败标题默认「操作失败」)。
	/// </summary>
	public static async void Run(Func<Task> work, string? failTitle = null)
	{
		long op = Smoke.SmokeState.OperationStarted();
		try
		{
			await work();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Smoke.SmokeState.Note("handler", ex.ToString());
			AppServices.Toast(failTitle ?? I18n.L("操作失败"), ex.Message, ToastKind.Error);
		}
		finally
		{
			Smoke.SmokeState.OperationFinished(op);
		}
	}

	/// <summary>页面正文(带 18/14 边距的竖排容器;Windows 同)。</summary>
	protected static Panel Body(params Control?[] kids)
	{
		SPanel panel = Ui.V(10.0, kids);
		panel.Margin = new Avalonia.Thickness(18.0, 14.0, 18.0, 14.0);
		return panel;
	}

	/// <summary>可平滑滚动的页面正文(Windows 同:外边距 18/14/18/16)。</summary>
	protected static SmoothScroll ScrollBody(params Control?[] kids)
	{
		SPanel panel = Ui.V(10.0, kids);
		panel.Margin = new Avalonia.Thickness(18.0, 14.0, 18.0, 16.0);
		return Ui.Scroll(panel);
	}
}

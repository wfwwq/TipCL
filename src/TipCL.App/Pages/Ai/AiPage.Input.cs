using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// K5:输入区分区(【Windows 源】<c>Pages\AiPage.cs:607-748</c> 斜杠候选与清空、<c>:756-771</c> 占位提示、
/// <c>:789-797</c> 的 @ 提及;<c>:750-754</c> 的 <c>OnHidden</c>)加上**附图那一整块**
/// (Windows <c>Pages\AiPage.Images.cs</c>,149 行:选择 / 粘贴 / 拖入 / 视觉能力自检 / 图数胶囊)。
///
/// Windows → mac 的差异(逐条):
/// <list type="number">
/// <item><b>斜杠浮层</b>:浮层仍是 Avalonia 的 <c>Popup</c>,但定位宽度取 <c>_composer.Bounds.Width</c>
///   (Avalonia 没有 WPF 的 <c>ActualWidth</c>,见 <c>Ui.WindowPctOf</c> 的同一条口径);</item>
/// <item><b>按键预览</b>:<c>ChatInput.KeyIntercept</c> 已按 mac 口径落地(<c>Avalonia.Input.Key</c>),
///   上下 / 回车 / Tab / Esc 五个键名与 WPF 一一对应;</item>
/// <item><b>剪贴板读图</b>:WPF 有静态的 <c>Clipboard.ContainsImage()</c> / <c>GetImage()</c> /
///   <c>GetFileDropList()</c>,Avalonia 11 **没有**静态入口(只有 <c>TopLevel.Clipboard</c>)⇒ 见下面的
///   <see cref="PasteReader"/> 注入缝(与 <c>CatalogPage.ClipboardReader</c>、<c>CrashUi.ClipboardWriter</c>
///   同一模式:离屏 / 测试换掉,**绝不碰真剪贴板**);</item>
/// <item><b>粘贴手势</b>:Windows 只认 <c>Ctrl+V</c>;mac 侧 <b>Ctrl+V 与 ⌘V 都收</b>
///   (macOS 的粘贴键就是 ⌘V,这是平台差异不是放宽);<c>PreviewKeyDown</c>(WPF 的隧道事件)→
///   <c>AddHandler(KeyDownEvent, …, RoutingStrategies.Tunnel)</c>(本仓 InstancePage 的先例);</item>
/// <item><b>异步</b>:Avalonia 的剪贴板只有异步入口 ⇒ 粘贴那段处理器是 <c>async</c> 的
///   (WPF 那边同步拿图;行为一致,只是完成时机在同一个事件回调内排队);</item>
/// <item><b>拖入</b>:<c>AllowDrop</c> → <c>DragDrop.SetAllowDrop</c>;<c>PreviewDragOver/PreviewDrop</c>
///   → <c>AddHandler(DragDrop.DragOverEvent/DropEvent, …, RoutingStrategies.Tunnel)</c>;
///   <c>e.Data.GetData(DataFormats.FileDrop)</c> → <c>e.Data.GetFiles()</c> + <c>TryGetLocalPath()</c>
///   (与 <c>MainWindow.DroppedPaths</c> 同一口径,含同一处 <c>CS0618</c> 的局部抑制)。</item>
/// </list>
/// </summary>
internal sealed partial class AiPage
{
	/// <summary>附图白名单(【Windows 源】<c>AiPage.Images.cs:24</c>)。</summary>
	private static readonly string[] _imgExts = new string[6] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

	// =========================================================================
	// K5a · 斜杠候选与清空(【Windows 源】Pages\AiPage.cs:607-748)
	// =========================================================================

	private void SyncSlash()
	{
		List<SlashCommand> items = AiChatOps.MatchSlash(_input.Text);
		if (items.Count == 0 || !_input.IsKeyboardFocusWithin)
		{
			CloseSlash();
			return;
		}

		_slashItems = items;
		_slashSel = 0;
		RenderSlash();
		_slashPop.Width = Math.Max(260.0, _composer.Bounds.Width);
		_slashPop.HorizontalOffset = 0.0;
		_slashPop.IsOpen = true;
	}

	private void RenderSlash()
	{
		_slashRows.Children.Clear();
		for (int i = 0; i < _slashItems.Count; i++)
		{
			SlashCommand cmd = _slashItems[i];
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(Ui.Txt("/" + I18n.L(cmd.Name), 13.0, bold: true, "B.Ink").VCenter(), 0);
			grid.Add(Ui.Txt(I18n.L(cmd.Desc), 12.0, bold: false, "B.InkMuted").M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Button row = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = new Cursor(StandardCursorType.Hand),
				Focusable = false
			};
			if (i == _slashSel)
			{
				row.Dyn(TemplatedControl.BackgroundProperty, "B.AccentSoft");
			}
			else
			{
				row.Background = Brushes.Transparent;
			}

			row.Click += delegate
			{
				PickSlash(cmd);
			};
			_slashRows.Children.Add(row);
		}
	}

	private void CloseSlash()
	{
		_slashPop.IsOpen = false;
		_slashItems = new List<SlashCommand>();
		_slashSel = -1;
	}

	private bool SlashKey(Key key)
	{
		if (!_slashPop.IsOpen || _slashItems.Count == 0)
		{
			return false;
		}

		switch (key)
		{
		case Key.Up:
		case Key.Down:
			_slashSel = AiChatOps.MoveSelection(_slashSel, (key == Key.Down) ? 1 : (-1), _slashItems.Count);
			RenderSlash();
			return true;
		case Key.Enter:
		case Key.Tab:
			if (_slashSel >= 0 && _slashSel < _slashItems.Count)
			{
				PickSlash(_slashItems[_slashSel]);
				return true;
			}

			return false;
		case Key.Escape:
			CloseSlash();
			return true;
		default:
			return false;
		}
	}

	private void PickSlash(SlashCommand cmd)
	{
		CloseSlash();
		_input.Clear();
		RunSlash(cmd);
		_input.Focus();
	}

	private void RunSlash(SlashCommand cmd)
	{
		switch (cmd.Local)
		{
		case "new_chat":
			PageBase.Run(NewChatAsync);
			return;
		case "clear":
			PageBase.Run(ClearChatAsync);
			return;
		case "rewind":
			if (_busy)
			{
				PageBase.Toast(I18n.L("回合还在跑"), I18n.L("先停止再撤回"), ToastKind.Warning);
			}
			else
			{
				PageBase.Run(RewindAsync);
			}

			return;
		}

		if (cmd.Prompt.Length > 0)
		{
			PageBase.Run(() => SendTextAsync(I18n.L(cmd.Prompt)));
		}
	}

	/// <summary>`/清空`:确认后删掉这个对话再开一个空的(【Windows 源】:728-748)。</summary>
	private async Task ClearChatAsync()
	{
		AiChatDto? chat = ActiveChat;
		if (chat == null || chat.Messages.Count == 0)
		{
			PageBase.Toast(I18n.L("当前对话是空的"), "");
			return;
		}

		if (await Dlg.Confirm(I18n.L("清空当前对话？"), I18n.L("这个对话的全部消息会被删除，无法恢复（不会还原磁盘改动）。"), I18n.L("清空"), null, danger: true))
		{
			string id = chat.Id;
			await SwitchChatAsync(async delegate
			{
				await PageBase.Api.TryCallAsync("ai_delete_chat", new
				{
					chat_id = id
				}, _store);
				return await PageBase.Api.TryCallAsync("ai_new_chat", null, _store);
			});
		}
	}

	public override void OnHidden()
	{
		CloseSlash();
		base.OnHidden();
	}

	// =========================================================================
	// K5b · 占位提示 / @ 提及(【Windows 源】Pages\AiPage.cs:756-771、:789-797)
	// =========================================================================

	private void SyncPlaceholder()
	{
		if (_pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_placeholder.Text = I18n.L("先处理上面的确认，或直接输入新要求来拒绝");
		}
		else if (_busy)
		{
			_placeholder.Text = I18n.L("回复中也可以继续输入，会立刻交给助手");
		}
		else
		{
			_placeholder.Text = I18n.L("问点什么，@ 引用实例或崩溃日志…  Enter 发送，Shift+Enter 换行");
		}

		_placeholder.IsVisible = string.IsNullOrEmpty(_input.Text);
	}

	private void InsertMention()
	{
		string text = PageBase.Win?.Prefs.CatalogInstance ?? "";
		string insert = ((text.Length > 0) ? I18n.L("@实例「{0}」 ", text) : I18n.L("@最近的崩溃日志 "));
		int caret = _input.CaretIndex;
		_input.Text = _input.Text.Insert(Math.Min(caret, _input.Text.Length), insert);
		_input.CaretIndex = Math.Min(caret + insert.Length, _input.Text.Length);
		_input.Focus();
	}

	// =========================================================================
	// K5c · 附图(【Windows 源】Pages\AiPage.Images.cs:26-147 全部 7 个方法)
	// =========================================================================

	/// <summary>
	/// 【注入缝 · mac 差异】读剪贴板(WPF 是静态的 <c>Clipboard.ContainsImage/GetImage/GetFileDropList</c>;
	/// Avalonia 11 只有 <c>TopLevel.Clipboard</c>,而且**一律异步**)。返回"能直接当附图喂进去的本地路径"。
	/// 离屏与测试可以换掉它(与 <c>CatalogPage.ClipboardReader</c> 同一模式),**绝不碰真剪贴板**。
	/// </summary>
	internal static Func<TopLevel, Task<List<string>>>? PasteReader { get; set; }

	private void PickImage()
	{
		string? path = Dlg.PickFile(I18n.L("图片") + "|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp", I18n.L("选择图片"));
		if (path != null)
		{
			AddImage(path);
		}
	}

	private void AddImage(string path)
	{
		if (string.IsNullOrEmpty(path) || _pendingImages.Contains(path) || !_imgExts.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
		{
			return;
		}

		if (_pendingImages.Count >= 4)
		{
			PageBase.Toast(I18n.L("最多附 4 张图"), "", ToastKind.Warning);
			return;
		}

		_pendingImages.Add(path);
		SyncImageChip();
		_ = CheckVisionAsync();
	}

	private async Task CheckVisionAsync()
	{
		try
		{
			System.Text.Json.JsonElement r = await PageBase.Api.TryCallAsync<System.Text.Json.JsonElement>("ai_vision_status");
			if (r.ValueKind == System.Text.Json.JsonValueKind.Object && r.TryGetProperty("supported", out System.Text.Json.JsonElement s) && s.ValueKind == System.Text.Json.JsonValueKind.False)
			{
				PageBase.Toast(I18n.L("当前模型可能看不了图"), I18n.L("图片会被省略。可在「设置 → AI 助手 → 识图」里改成「支持」或换模型"), ToastKind.Warning);
			}
		}
		catch (Exception)
		{
		}
	}

	private void SyncImageChip()
	{
		_imgChip.Text = ((_pendingImages.Count > 0) ? I18n.L("{0} 张图 ×", _pendingImages.Count) : "");
		ToolTip.SetTip(_imgChip, (_pendingImages.Count > 0) ? (string.Join("\n", _pendingImages) + "\n" + I18n.L("点击清空")) : null);
	}

	private void SetupImageInput()
	{
		_imgChip.FontSize = 12.0;
		_imgChip.Cursor = new Cursor(StandardCursorType.Hand);
		_imgChip.Dyn(TextBlock.ForegroundProperty, "B.InkMuted");
		_imgChip.PointerReleased += delegate(object? _, PointerReleasedEventArgs e)
		{
			if (e.InitialPressMouseButton != MouseButton.Left)
			{
				return;
			}

			_pendingImages.Clear();
			SyncImageChip();
		};
		// 【mac 差异】WPF 的 PreviewKeyDown → Avalonia 隧道路由(本仓 InstancePage 的先例)
		_input.AddHandler(InputElement.KeyDownEvent, OnInputPastePreview, RoutingStrategies.Tunnel);
	}

	/// <summary>Ctrl+V(mac 上还有 ⌘V):把剪贴板里的图 / 文件列表当附图收进来(【Windows 源】<c>Images.cs:82-117</c>)。</summary>
	private async void OnInputPastePreview(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.V || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
		{
			return;
		}

		try
		{
			TopLevel? top = TopLevel.GetTopLevel(this);
			if (top == null)
			{
				return;
			}

			List<string> paths = await (PasteReader ?? DefaultPasteReader)(top);
			foreach (string path in paths)
			{
				AddImage(path);
			}

			e.Handled = _pendingImages.Count > 0;
		}
		catch (Exception)
		{
			// Windows 那边整段也是空 catch:粘贴失败就当"没粘到东西",不打断输入
		}
	}

	/// <summary>默认的剪贴板读法:先文件列表(拷贝的文件),再 PNG 位图落一个临时文件(= Windows 的 <c>Clipboard.GetImage</c> 那段)。</summary>
	private static async Task<List<string>> DefaultPasteReader(TopLevel top)
	{
		List<string> list = new List<string>();
		// 离屏(--smoke)不碰真剪贴板(CatalogPage 的同一条纪律:D-69/D-128)
		if (Dlg.SmokeActive)
		{
			return list;
		}

		IClipboard? clipboard = top.Clipboard;
		if (clipboard == null)
		{
			return list;
		}

		try
		{
			// Avalonia 11.3.23 的 IClipboard **没有** GetFilesAsync(那是 11.3 起
			// IAsyncDataTransfer 上的扩展方法,而这一版的程序集里换不过去)⇒ 走既有的
			// GetDataAsync(DataFormats.Files):拿到 IStorageItem 列表或路径列表两种形态都认。
			// 与 MainWindow.DroppedPaths 一样局部抑制过时告警(不动全局 NoWarn)。
#pragma warning disable CS0618
			object? files = await clipboard.GetDataAsync(DataFormats.Files);
#pragma warning restore CS0618
			foreach (string path in PathsOf(files))
			{
				if (path.Length > 0 && _imgExts.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
				{
					list.Add(path);
				}
			}

			if (list.Count > 0)
			{
				return list;
			}

			string? pasted = await SaveClipboardImageAsync(clipboard);
			if (pasted != null)
			{
				list.Add(pasted);
			}
		}
		catch (Exception)
		{
			// 拿不到就当"剪贴板里没有图"(Windows 的 ContainsImage 为 false 时也是这个结果)
		}

		return list;
	}

	/// <summary>剪贴板里的"文件"数据两种形态都认(Avalonia 给 <c>IStorageItem</c> 列表,别的后端可能给路径串)。</summary>
	private static IEnumerable<string> PathsOf(object? data)
	{
		if (data is IEnumerable<IStorageItem> items)
		{
			foreach (IStorageItem item in items)
			{
				string path = item.TryGetLocalPath() ?? "";
				if (path.Length > 0)
				{
					yield return path;
				}
			}

			yield break;
		}

		if (data is IEnumerable<string> names)
		{
			foreach (string name in names)
			{
				if (name.Length > 0)
				{
					yield return name;
				}
			}
		}
	}

	/// <summary>剪贴板里的位图落成临时 PNG(WPF 那段 <c>PngBitmapEncoder</c> 的等价物;Avalonia 的
	/// <c>Bitmap.Save</c> 就是写 PNG)。</summary>
	private static async Task<string?> SaveClipboardImageAsync(IClipboard clipboard)
	{
#pragma warning disable CS0618
		object? data = await clipboard.GetDataAsync("PNG");
#pragma warning restore CS0618
		if (data is not Stream stream)
		{
			return null;
		}

		using MemoryStream buffer = new MemoryStream();
		await stream.CopyToAsync(buffer);
		buffer.Position = 0L;
		string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TipCL-paste");
		System.IO.Directory.CreateDirectory(dir);
		string file = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
		using Bitmap bitmap = new Bitmap(buffer);
		bitmap.Save(file);
		return file;
	}

	/// <summary>让某件控件收图(【Windows 源】<c>Images.cs:120-147</c>)。</summary>
	private void HookImageDrop(Control target)
	{
		DragDrop.SetAllowDrop(target, true);
		target.AddHandler(DragDrop.DragOverEvent, OnImageDragOver, RoutingStrategies.Tunnel);
		target.AddHandler(DragDrop.DropEvent, OnImageDrop, RoutingStrategies.Tunnel);
	}

	private void OnImageDragOver(object? sender, DragEventArgs e)
	{
		if (DroppedPaths(e).Any((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
		{
			e.DragEffects = DragDropEffects.Copy;
			e.Handled = true;
		}
	}

	private void OnImageDrop(object? sender, DragEventArgs e)
	{
		string[] files = DroppedPaths(e).Where((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
		if (files.Length > 0)
		{
			foreach (string f in files)
			{
				AddImage(f);
			}

			e.Handled = true;
		}
	}

	/// <summary>这次拖拽带的本地文件路径(Avalonia 的 <c>e.Data.GetFiles()</c> 对应 Windows 的
	/// <c>DataFormats.FileDrop</c>;取不到就是空表)。</summary>
	private static List<string> DroppedPaths(DragEventArgs e)
	{
		List<string> paths = new List<string>();
		try
		{
#pragma warning disable CS0618
			IEnumerable<IStorageItem>? files = e.Data.GetFiles();
#pragma warning restore CS0618
			foreach (IStorageItem item in files ?? Enumerable.Empty<IStorageItem>())
			{
				string path = item.TryGetLocalPath() ?? "";
				if (path.Length > 0)
				{
					paths.Add(path);
				}
			}
		}
		catch
		{
			// 拿不到文件列表就当"没带文件"(Windows 那边 GetDataPresent 为 false 时也是这个结果)
		}

		return paths;
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FileMemory = TipCL.Core.Launcher.Ai.FileMemory;
using McpServer = TipCL.Core.Launcher.Ai.McpServer;
using TipCL.Models;
using TipCL.Pages.Ai;
using TipCL.Services;

namespace TipCL.Pages;

// AI 页图片输入：选择/粘贴/拖入/视觉检测（WP10 拆分）
// 从 AiPage.cs 机械提取（partial class，行为不变；见 DECISIONS D-24）。
public sealed partial class AiPage
{
	private static readonly string[] _imgExts = new string[6] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

	private void PickImage()
	{
		string path = Dlg.PickFile(I18n.L("图片") + "|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp", I18n.L("选择图片"));
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
			JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_vision_status");
			if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("supported", out var s) && s.ValueKind == JsonValueKind.False)
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
		_imgChip.Text = (_pendingImages.Count > 0) ? I18n.L("{0} 张图 ×", _pendingImages.Count) : "";
		_imgChip.ToolTip = (_pendingImages.Count > 0) ? (string.Join("\n", _pendingImages) + "\n" + I18n.L("点击清空")) : null;
	}

	private void SetupImageInput()
	{
		_imgChip.FontSize = 12.0;
		_imgChip.Cursor = Cursors.Hand;
		_imgChip.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		_imgChip.MouseLeftButtonUp += delegate
		{
			_pendingImages.Clear();
			SyncImageChip();
		};
		_input.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
			{
				return;
			}
			try
			{
				if (Clipboard.ContainsImage())
				{
					System.Windows.Media.Imaging.BitmapSource img = Clipboard.GetImage();
					string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TipCL-paste");
					System.IO.Directory.CreateDirectory(dir);
					string file = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
					using (System.IO.FileStream fs = System.IO.File.Create(file))
					{
						System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
						enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
						enc.Save(fs);
					}
					AddImage(file);
					e.Handled = true;
				}
				else if (Clipboard.ContainsFileDropList())
				{
					foreach (string f in Clipboard.GetFileDropList())
					{
						AddImage(f);
					}
					e.Handled = _pendingImages.Count > 0;
				}
			}
			catch (Exception)
			{
			}
		};
	}

	private void HookImageDrop(UIElement target)
	{
		target.AllowDrop = true;
		target.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop) && ((string[])e.Data.GetData(DataFormats.FileDrop)).Any((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		target.PreviewDrop += delegate(object _, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				return;
			}
			string[] files = ((string[])e.Data.GetData(DataFormats.FileDrop)).Where((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
			if (files.Length > 0)
			{
				foreach (string f in files)
				{
					AddImage(f);
				}
				e.Handled = true;
			}
		};
	}
}


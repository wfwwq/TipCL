using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// AI 页对话上方的标签条：「对话」+ 插件注册的 aiView（kind "aiView"）。
/// 只有当前对话的预设匹配 spec.presets（为空 = 所有对话）时才出现。选中插件标签时，
/// 插件视图盖住消息列表和输入框（插件视图自己有终端 / 输入 / 停止，比如 dsh-game 的游戏标签）。
/// 每个（视图, 对话）一个 WebView，最近用过的保留几个，切回来状态还在；视图 props 带 chatId。
/// </summary>
public sealed class AiViewStrip
{
	private const int KeepViews = 4;

	private readonly Border _bar;

	private readonly StackPanel _tabs = new StackPanel
	{
		Orientation = Orientation.Horizontal
	};

	private readonly Grid _host = new Grid
	{
		Visibility = Visibility.Collapsed
	};

	private string _chatId = "";

	private string _preset = "";

	private string _defaultView = "";

	/// <summary>当前选中的标签：空 = 对话；否则 UiContribution.Key。</summary>
	private string _selected = "";

	private readonly Dictionary<string, string> _perChat = new Dictionary<string, string>();

	private readonly List<(string Key, FrameworkElement View)> _views = new List<(string, FrameworkElement)>();

	private List<UiContribution> _applicable = new List<UiContribution>();

	private string _shownKey = "";

	public FrameworkElement Bar => _bar;

	public FrameworkElement Host => _host;

	/// <summary>true = 插件视图在前台（AI 页隐藏消息列表和输入框）。</summary>
	public event Action<bool>? ModeChanged;

	/// <summary>预设列表读到了（AI 页重画对话列表上的预设标记）。</summary>
	public event Action? PresetsChanged;

	public bool PluginViewActive => _selected.Length > 0;

	public AiViewStrip()
	{
		_bar = new Border
		{
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Child = _tabs,
			Visibility = Visibility.Collapsed
		};
		_bar.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		PluginUi.Changed += delegate
		{
			PresetCatalog.Invalidate();
			Rebuild();
			PageBase.Run(LoadPresetsAsync);
		};
	}

	private async Task LoadPresetsAsync()
	{
		if (await PresetCatalog.EnsureAsync())
		{
			PresetsChanged?.Invoke();
		}
	}

	/// <summary>AI 页切换 / 刷新当前对话时调用。</summary>
	public void SetChat(string chatId, string preset, string view)
	{
		if (!PresetCatalog.Loaded)
		{
			PageBase.Run(LoadPresetsAsync);
		}
		bool changed = chatId != _chatId;
		if (changed && _chatId.Length > 0)
		{
			_perChat[_chatId] = _selected;
		}
		_chatId = chatId ?? "";
		_preset = preset ?? "";
		if (_defaultView.Length == 0 || changed)
		{
			_defaultView = (view ?? "").Length > 0 ? view! : PresetCatalog.DefaultView(_preset);
		}
		if (changed)
		{
			_selected = _perChat.TryGetValue(_chatId, out string remembered) ? remembered : (Resolve(_defaultView)?.Key ?? "");
		}
		Rebuild();
	}

	/// <summary>切到某个标签：aiView id、插件/id、或完整 key；空字符串 = 对话。</summary>
	public void Select(string view)
	{
		UiContribution? c = Resolve(view);
		_selected = c?.Key ?? "";
		Rebuild();
	}

	private UiContribution? Resolve(string view)
	{
		if (string.IsNullOrEmpty(view))
		{
			return null;
		}
		return Applicable().FirstOrDefault((UiContribution c) => c.Id == view || c.Key == view || c.Plugin + "/" + c.Id == view || "plugin:" + c.Plugin + "/" + c.Id == view);
	}

	private List<UiContribution> Applicable()
	{
		if (_chatId.Length == 0)
		{
			return new List<UiContribution>();
		}
		return PluginUi.OfKind("aiView").Where(Applies).ToList();
	}

	private bool Applies(UiContribution c)
	{
		List<string> presets = c.Presets;
		if (presets.Count == 0)
		{
			return true;
		}
		if (_preset.Length == 0)
		{
			return false;
		}
		string full = "plugin:" + c.Plugin + "/";
		foreach (string p in presets)
		{
			if (p == _preset || full + p == _preset || "plugin:" + p == _preset || (_preset.StartsWith("plugin:", StringComparison.Ordinal) && _preset.EndsWith("/" + p, StringComparison.Ordinal) && !p.Contains('/')))
			{
				return true;
			}
		}
		return false;
	}

	private void Rebuild()
	{
		_applicable = Applicable();
		if (_selected.Length > 0 && !_applicable.Any((UiContribution c) => c.Key == _selected))
		{
			_selected = "";
		}
		_tabs.Children.Clear();
		if (_applicable.Count == 0)
		{
			_bar.Visibility = Visibility.Collapsed;
			Show(null);
			return;
		}
		_bar.Visibility = Visibility.Visible;
		_tabs.Children.Add(Tab(I18n.L("对话"), ""));
		foreach (UiContribution c in _applicable)
		{
			_tabs.Children.Add(Tab(c.Label, c.Key));
		}
		Show(_applicable.FirstOrDefault((UiContribution c) => c.Key == _selected));
	}

	private ToggleButton Tab(string text, string key)
	{
		ToggleButton b = new ToggleButton
		{
			Content = text,
			Style = Ui.S("TabItemBtn"),
			IsChecked = (_selected == key)
		};
		b.Click += delegate
		{
			_selected = key;
			if (_chatId.Length > 0)
			{
				_perChat[_chatId] = key;
			}
			Rebuild();
		};
		return b;
	}

	private void Show(UiContribution? c)
	{
		string newKey = (c == null) ? "" : (c.Key + "|" + _chatId);
		if (newKey == _shownKey && c != null && !c.Native && c.Url.Length > 0 && !(_views.FirstOrDefault(((string Key, FrameworkElement View) v) => v.Key == newKey).View is PluginWebView))
		{
			// 之前显示的是「还没准备好」占位，现在有地址了：重建
			_shownKey = "";
		}
		if (newKey == _shownKey)
		{
			if (c != null)
			{
				// 地址可能因插件重载变了
				FrameworkElement? cur = _views.FirstOrDefault(((string Key, FrameworkElement View) v) => v.Key == newKey).View;
				if (cur is PluginWebView w0 && w0.Url != c.Url)
				{
					w0.SetUrl(c.Url);
				}
			}
			return;
		}
		UiContribution? old = OldContribution();
		if (old != null)
		{
			Notify(old, "hidden");
		}
		_shownKey = newKey;
		_host.Children.Clear();
		if (c == null)
		{
			_host.Visibility = Visibility.Collapsed;
			ModeChanged?.Invoke(obj: false);
			return;
		}
		int idx = _views.FindIndex(((string Key, FrameworkElement View) v) => v.Key == newKey);
		FrameworkElement view;
		if (idx >= 0 && (c.Native || _views[idx].View is PluginWebView || c.Url.Length == 0))
		{
			view = _views[idx].View;
			_views.RemoveAt(idx);
			if (view is PluginWebView w && w.Url != c.Url)
			{
				w.SetUrl(c.Url);
			}
		}
		else
		{
			if (idx >= 0)
			{
				(_views[idx].View as IDisposable)?.Dispose();
				_views.RemoveAt(idx);
			}
			view = NativePluginUi.Content(c, new Dictionary<string, object?> { ["chatId"] = _chatId }, _chatId);
		}
		_views.Add((newKey, view));
		while (_views.Count > KeepViews)
		{
			(_views[0].View as IDisposable)?.Dispose();
			_views.RemoveAt(0);
		}
		_host.Children.Add(view);
		_host.Visibility = Visibility.Visible;
		ModeChanged?.Invoke(obj: true);
		Notify(c, "shown");
	}

	private UiContribution? OldContribution()
	{
		if (_shownKey.Length == 0)
		{
			return null;
		}
		int bar = _shownKey.LastIndexOf('|');
		string key = (bar > 0) ? _shownKey.Substring(0, bar) : _shownKey;
		return PluginUi.All.FirstOrDefault((UiContribution x) => x.Key == key);
	}

	private void Notify(UiContribution c, string ev)
	{
		if (!AppServices.Ready)
		{
			return;
		}
		string chatId = _chatId;
		PageBase.Run(async delegate
		{
			await AppServices.Client.TryCallAsync<object>("plugins_ui_event", new
			{
				plugin = c.Plugin,
				kind = c.Kind,
				id = c.Id,
				@event = ev,
				data = new Dictionary<string, object> { ["chatId"] = chatId }
			});
		});
	}

	/// <summary>对话被删掉时清理它的视图。</summary>
	public void Forget(string chatId)
	{
		_perChat.Remove(chatId);
		for (int i = _views.Count - 1; i >= 0; i--)
		{
			if (_views[i].Key.EndsWith("|" + chatId, StringComparison.Ordinal) && _views[i].Key != _shownKey)
			{
				(_views[i].View as IDisposable)?.Dispose();
				_views.RemoveAt(i);
			}
		}
	}
}

/// <summary>
/// AI 预设清单（ai_presets）：用于「用预设新建对话」菜单和对话列表上的预设标记。
/// 每项 {id, name, description?, plugin?, view?}；id 形如 plugin:&lt;插件&gt;/&lt;预设&gt;。
/// </summary>
public static class PresetCatalog
{
	public sealed class Preset
	{
		public string Id = "";

		public string Name = "";

		public string Description = "";

		public string Plugin = "";

		public string View = "";
	}

	private static List<Preset> _items = new List<Preset>();

	public static bool Loaded { get; private set; }

	private static Task<bool>? _inflight;

	public static IReadOnlyList<Preset> Items => _items;

	public static void Invalidate()
	{
		Loaded = false;
	}

	/// <summary>读一次；返回是否有变化。</summary>
	public static Task<bool> EnsureAsync()
	{
		if (Loaded)
		{
			return Task.FromResult(result: false);
		}
		return _inflight ?? (_inflight = LoadAsync());
	}

	private static async Task<bool> LoadAsync()
	{
		try
		{
			if (!AppServices.Ready)
			{
				return false;
			}
			JsonElement r = await AppServices.Client.TryCallAsync<JsonElement>("ai_presets");
			List<Preset> next = J.Items(r).Select((JsonElement e) => new Preset
			{
				Id = J.Str(e, "id"),
				Name = J.Str(e, "name"),
				Description = J.Str(e, "description"),
				Plugin = J.Str(e, "plugin"),
				View = J.Str(e, "view")
			}).Where((Preset p) => p.Id.Length > 0).ToList();
			bool changed = next.Count != _items.Count || next.Where((Preset p, int i) => p.Id != _items[i].Id || p.Name != _items[i].Name).Any();
			_items = next;
			Loaded = true;
			return changed;
		}
		finally
		{
			_inflight = null;
		}
	}

	public static string Name(string presetId)
	{
		if (string.IsNullOrEmpty(presetId))
		{
			return "";
		}
		Preset? p = _items.FirstOrDefault((Preset x) => x.Id == presetId);
		if (p != null && p.Name.Length > 0)
		{
			return p.Name;
		}
		int slash = presetId.LastIndexOf('/');
		if (slash < 0)
		{
			return presetId;
		}
		return presetId.Substring(slash + 1);
	}

	public static string DefaultView(string presetId)
	{
		return _items.FirstOrDefault((Preset x) => x.Id == presetId)?.View ?? "";
	}

	/// <summary>「新对话」旁边的下拉：按预设新建对话。没有预设时菜单里只有「普通对话」。</summary>
	public static void OpenMenu(FrameworkElement anchor, Func<string, string, Task> create)
	{
		PageBase.Run(async delegate
		{
			await EnsureAsync();
			ContextMenu menu = new ContextMenu();
			PluginsPage.AddItem(menu, I18n.L("普通对话"), "\ue710", delegate
			{
				PageBase.Run(() => create("", ""));
			});
			if (_items.Count > 0)
			{
				menu.Items.Add(new Separator());
			}
			foreach (Preset p in _items)
			{
				Preset pp = p;
				MenuItem mi = new MenuItem
				{
					Header = (p.Name.Length > 0) ? p.Name : p.Id,
					ToolTip = (p.Description.Length > 0) ? p.Description : p.Id,
					Icon = Ui.Glyph("\ue99a", 13.0)
				};
				mi.Click += delegate
				{
					PageBase.Run(() => create(pp.Id, pp.View));
				};
				menu.Items.Add(mi);
			}
			if (_items.Count == 0)
			{
				menu.Items.Add(new MenuItem
				{
					Header = I18n.L("还没有插件提供预设"),
					IsEnabled = false
				});
			}
			menu.PlacementTarget = anchor;
			menu.Placement = PlacementMode.Bottom;
			menu.IsOpen = true;
		});
	}
}

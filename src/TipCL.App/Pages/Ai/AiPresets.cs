using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using TipCL.Services;

namespace TipCL.Pages.Ai;

/// <summary>
/// K11 附属件:AI 预设清单(= Windows <c>Pages\Plugins\AiViewStrip.cs</c> 里的 <c>PresetCatalog</c>,
/// <c>:318-455</c>)—— 用于「用预设新建对话」菜单和对话列表上的预设标记。
/// 每项 <c>{id, name, description?, plugin?, view?}</c>;id 形如 <c>plugin:&lt;插件&gt;/&lt;预设&gt;</c>。
///
/// <b>为什么在这里另起一个类,而不是搬 <c>PresetCatalog</c></b>:<c>PresetCatalog</c> 的宿主文件
/// <c>Pages\Plugins\AiViewStrip.cs</c> 里,**同一个文件**还有 AI 视图标签条 <c>AiViewStrip</c>(316 行,
/// 依赖插件视图宿主与插件 UI RPC)。标签条本次不搬(理由见交付报告),但**预设清单这一半与它无关**
/// (只读一个 <c>ai_presets</c> RPC),直接丢掉会连累「用预设新建对话」与列表标记两个已存在的入口,
/// 所以按**同一份逻辑、同一个 RPC**在 mac 侧重新落地成 <see cref="AiPresets"/>。
/// 将来真的移植 <c>AiViewStrip</c> 时,应当把这一份并回去(同一 RPC 只该有一个缓存)。
///
/// Windows → mac 的差异:JSON 解析从 Windows 的 <c>J.Items/J.Str</c> 助手换成直接的 <c>JsonElement</c> 读取
/// (mac 侧没有那个助手类);菜单从 <c>PlacementTarget + IsOpen</c> 换成 <c>ContextMenu.Open</c>(D-124);
/// 其余(名称回退到 <c>'/'</c> 之后那一段、<c>Loaded</c> 缓存、并发只读一次)逐字照搬。
/// </summary>
internal static class AiPresets
{
	internal sealed class Preset
	{
		public string Id = "";

		public string Name = "";

		public string Description = "";

		public string Plugin = "";

		public string View = "";
	}

	private static List<Preset> _items = new List<Preset>();

	private static Task<bool>? _inflight;

	internal static bool Loaded { get; private set; }

	internal static IReadOnlyList<Preset> Items => _items;

	internal static void Invalidate()
	{
		Loaded = false;
	}

	/// <summary>读一次;返回是否有变化。</summary>
	internal static Task<bool> EnsureAsync()
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
			List<Preset> next = ItemsOf(r).Select((JsonElement e) => new Preset
			{
				Id = Str(e, "id"),
				Name = Str(e, "name"),
				Description = Str(e, "description"),
				Plugin = Str(e, "plugin"),
				View = Str(e, "view")
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

	/// <summary>预设显示名;认不出 id 就退回 <c>'/'</c> 之后那一段(Windows 同名同义)。</summary>
	internal static string Name(string presetId)
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

	internal static string DefaultView(string presetId)
	{
		return _items.FirstOrDefault((Preset x) => x.Id == presetId)?.View ?? "";
	}

	/// <summary>「新对话」旁边的下拉:按预设新建对话。没有预设时菜单里只有「普通对话」。</summary>
	internal static void OpenMenu(Control anchor, Func<string, string, Task> create)
	{
		PageBase.Run(async delegate
		{
			await EnsureAsync();
			ContextMenu menu = new ContextMenu();
			AddItem(menu, I18n.L("普通对话"), "\ue710", delegate
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
					Header = ((p.Name.Length > 0) ? p.Name : p.Id),
					Icon = Ui.Glyph("\ue99a", 13.0)
				};
				ToolTip.SetTip(mi, (p.Description.Length > 0) ? p.Description : p.Id);
				mi.Click += delegate
				{
					PageBase.Run(() => create(pp.Id, pp.View));
				};
				menu.Items.Add(mi);
			}

			menu.Open(anchor);
		});
	}

	/// <summary>菜单项(Windows 那份走 <c>PluginsPage.AddItem</c>;mac 侧复刻同一形状:文字 + 图标 + 点击)。</summary>
	private static void AddItem(ContextMenu menu, string header, string glyph, Action act)
	{
		MenuItem mi = new MenuItem
		{
			Header = header,
			Icon = Ui.Glyph(glyph, 13.0)
		};
		mi.Click += delegate
		{
			act();
		};
		menu.Items.Add(mi);
	}

	/// <summary>RPC 回来的清单:数组本体、或包在 <c>items</c> 里两种都认(Windows 的 <c>J.Items</c> 同一口径)。</summary>
	private static IEnumerable<JsonElement> ItemsOf(JsonElement r)
	{
		if (r.ValueKind == JsonValueKind.Array)
		{
			return r.EnumerateArray().ToArray();
		}

		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
		{
			return items.EnumerateArray().ToArray();
		}

		return new JsonElement[0];
	}

	private static string Str(JsonElement e, string prop)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(prop, out JsonElement v) || v.ValueKind != JsonValueKind.String)
		{
			return "";
		}

		return v.GetString() ?? "";
	}
}

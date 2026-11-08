using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Core.Plugins;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

/// <summary>
/// 设置页里的「插件」卡片：市场索引地址（plugin_market_indexes）、允许未审核插件（plugin_allow_unverified）、
/// 管理插件 / 模型供应商入口。这两项随改随存（不等设置页底部的「保存设置」）。
/// </summary>
public static class PluginSettingsCard
{
	public static List<string> ParseIndexes(JsonElement v)
	{
		List<string> list = new List<string>();
		if (v.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement x in v.EnumerateArray())
			{
				if (x.ValueKind == JsonValueKind.String)
				{
					list.Add(x.GetString() ?? "");
				}
			}
		}
		else if (v.ValueKind == JsonValueKind.String)
		{
			list.AddRange((v.GetString() ?? "").Split(new char[4] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
		}
		return Clean(list);
	}

	public static List<string> Clean(IEnumerable<string> lines)
	{
		return (from s in lines
			select s.Trim() into s
			where s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			select s).Distinct<string>(StringComparer.Ordinal).ToList();
	}

	public static async Task SaveAsync(Dictionary<string, object> patch)
	{
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = patch
		});
	}

	/// <summary>编辑市场地址的对话框；取消返回 null。</summary>
	public static async Task<List<string>?> EditIndexesDialogAsync(List<string> current)
	{
		TextBox box = Ui.Multi("https://example.com/tipcl-plugins/index.json", string.Join("\n", current), 140.0);
		box.MinWidth = 480.0;
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("一行一个插件市场索引地址（index.json）。多个市场按插件 id 合并，排在前面的优先。")).Wrap(), box);
		List<string>? result = await Dlg.Form(I18n.L("插件市场地址"), body, delegate
		{
			List<string> raw = box.Text.Split(new char[2] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select((string s) => s.Trim()).Where((string s) => s.Length > 0)
				.ToList();
			List<string> clean = Clean(raw);
			if (clean.Count != raw.Count)
			{
				throw new ArgumentException(I18n.L("地址要以 http:// 或 https:// 开头"));
			}
			return clean;
		});
		if (result == null)
		{
			return null;
		}
		await SaveAsync(new Dictionary<string, object> { ["plugin_market_indexes"] = result });
		AppServices.Toast(I18n.L("已保存"), I18n.L("{0} 个市场地址", result.Count), ToastKind.Success);
		return result;
	}

	public static Border Build()
	{
		TextBlock summary = Ui.Small("");
		List<string> indexes = new List<string>();
		CheckBox unverified = Ui.Switch();
		// —— 加速镜像：开关 + ↑↓ 即优先级（plugin_mirrors = 有序启用 id 列表） ——
		SPanel mirrorHost = Ui.V(2.0);
		List<string> mirrorIds = new List<string>();
		bool mirrorSyncing = false;
		async Task SaveMirrorsAsync()
		{
			await SaveAsync(new Dictionary<string, object> { ["plugin_mirrors"] = mirrorIds.ToList() });
			AppServices.Toast(I18n.L("已保存"), I18n.L("镜像优先级已更新"), ToastKind.Success);
			PluginsPage? page = AppServices.Window?.GetPage("plugins") as PluginsPage;
			page?.RefreshMarketSoon();
		}
		void RenderMirrors()
		{
			mirrorHost.Children.Clear();
			List<PluginMarket.GhMirror> enabled = mirrorIds.Select((string id) => PluginMarket.BuiltinMirrors.FirstOrDefault((PluginMarket.GhMirror m) => m.Id == id)).OfType<PluginMarket.GhMirror>().ToList();
			List<PluginMarket.GhMirror> disabled = PluginMarket.BuiltinMirrors.Where((PluginMarket.GhMirror m) => !mirrorIds.Contains(m.Id)).ToList();
			List<PluginMarket.GhMirror> all = enabled.Concat(disabled).ToList();
			foreach (PluginMarket.GhMirror m in all)
			{
				bool on = mirrorIds.Contains(m.Id);
				CheckBox sw = Ui.Switch(on);
				TextBlock label = Ui.Txt(m.Label, 12.5, bold: true);
				TextBlock sub = Ui.Muted(m.Prefix.Length == 0 ? "https://github.com（直连）" : m.Prefix + "…");
				Button up = Ui.IconBtn("\ue70e", I18n.L("上移"));
				Button down = Ui.IconBtn("\ue70d", I18n.L("下移"));
				up.IsEnabled = on && mirrorIds.IndexOf(m.Id) > 0;
				down.IsEnabled = on && mirrorIds.IndexOf(m.Id) < mirrorIds.Count - 1;
				sw.Click += delegate
				{
					if (mirrorSyncing)
					{
						return;
					}
					if (sw.IsChecked == true)
					{
						mirrorIds.Add(m.Id);
					}
					else
					{
						mirrorIds.Remove(m.Id);
					}
					PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
					RenderMirrors();
				};
				RoutedEventHandler move = delegate(object s, RoutedEventArgs _)
				{
					if (mirrorSyncing || !(s is Button b))
					{
						return;
					}
					int cur = mirrorIds.IndexOf(m.Id);
					int dir = (b == up) ? -1 : 1;
					int next = cur + dir;
					if (cur < 0 || next < 0 || next >= mirrorIds.Count)
					{
						return;
					}
					(mirrorIds[cur], mirrorIds[next]) = (mirrorIds[next], mirrorIds[cur]);
					PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
					RenderMirrors();
				};
				up.Click += move;
				down.Click += move;
				Border row = Ui.Card(Ui.G(null, "Auto,*,Auto,Auto")
					.Add(sw.VCenter(), 0)
					.Add(Ui.V(1.0, label, sub).M(10.0, 0.0, 6.0, 0.0), 0, 1)
					.Add(up.VCenter(), 0, 2)
					.Add(down.VCenter(), 0, 3), 12.0);
				row.Opacity = (on ? 1.0 : 0.62);
				mirrorHost.Children.Add(row);
			}
		}
		Button mirrorReset = Ui.Btn(I18n.L("恢复默认顺序"), BtnKind.Ghost, delegate
		{
			mirrorIds = PluginMarket.BuiltinMirrors.Select((PluginMarket.GhMirror m) => m.Id).ToList();
			PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
			RenderMirrors();
		});
		bool syncing = false;
		unverified.Click += delegate
		{
			if (syncing)
			{
				return;
			}
			bool on = unverified.IsChecked == true;
			PageBase.Run(async delegate
			{
				if (on && !(await Dlg.Confirm(I18n.L("允许安装未审核插件？"), I18n.L("未审核的插件没人检查过来源和代码。Node 插件拥有完整的电脑权限，只安装你信任的作者发布的插件。"), I18n.L("仍然允许"), null, danger: true)))
				{
					unverified.IsChecked = false;
					return;
				}
				await SaveAsync(new Dictionary<string, object> { ["plugin_allow_unverified"] = on });
			}, I18n.L("设置保存失败"));
		};
		Button edit = Ui.Btn(I18n.L("编辑"), BtnKind.Soft, null, "\ue70f");
		edit.Click += delegate
		{
			PageBase.Run(async delegate
			{
				List<string>? next = await EditIndexesDialogAsync(indexes);
				if (next != null)
				{
					indexes = next;
					Sync();
				}
			}, I18n.L("设置保存失败"));
		};
		Button manage = Ui.Btn(I18n.L("管理插件"), BtnKind.Normal, delegate
		{
			AppServices.Window?.Navigate("plugins");
		}, "\uea86");
		Button providers = Ui.Btn(I18n.L("模型供应商"), BtnKind.Normal, delegate
		{
			MainWindow? win = AppServices.Window;
			if (win != null)
			{
				win.Navigate("plugins");
				(win.GetPage("plugins") as PluginsPage)?.ShowTab("dev");
			}
		}, "\ue99a");
		Border card = Ui.Card(Ui.V(2.0, Ui.Section(I18n.L("插件"), I18n.L("插件市场、加速镜像、未审核插件、插件可用的模型供应商")), Ui.Field(I18n.L("插件市场地址"), Ui.H(10.0, edit, summary.VCenter()), I18n.L("填写插件市场的索引地址，可以填多个")), Ui.Field(I18n.L("加速镜像"), Ui.V(6.0, Ui.Small(I18n.L("开关和 ↑↓ 排序即优先级：排在前面的先尝试，全部失败才报错")).Wrap(), mirrorHost, Ui.H(6.0, mirrorReset))), Ui.Field(I18n.L("允许安装未审核插件"), unverified, I18n.L("关着时，市场里未审核的插件要先打开这里才能安装")), Ui.Field("", Ui.H(8.0, manage, providers))));
		card.Loaded += delegate
		{
			if (!AppServices.Ready)
			{
				return;
			}
			PageBase.Run(async delegate
			{
				Dictionary<string, JsonElement> s = (await AppServices.Client.TryCallAsync<Dictionary<string, JsonElement>>("get_settings")) ?? new Dictionary<string, JsonElement>();
				indexes = ParseIndexes(s.TryGetValue("plugin_market_indexes", out JsonElement ix) ? ix : default(JsonElement));
				mirrorSyncing = true;
				// get_settings 已回显默认（未配置 = 内置顺序全开）；再兜一层：键缺失也按全开渲染，避免 UI 显示全关而内核全开
				mirrorIds = (s.TryGetValue("plugin_mirrors", out JsonElement mr) && mr.ValueKind == JsonValueKind.Array)
					? mr.EnumerateArray().Where((JsonElement x) => x.ValueKind == JsonValueKind.String).Select((JsonElement x) => x.GetString() ?? "").Where((string id) => PluginMarket.BuiltinMirrors.Any((PluginMarket.GhMirror m) => m.Id == id)).ToList()
					: PluginMarket.BuiltinMirrors.Select((PluginMarket.GhMirror m) => m.Id).ToList();
				mirrorSyncing = false;
				unverified.IsChecked = s.TryGetValue("plugin_allow_unverified", out JsonElement a) && a.ValueKind == JsonValueKind.True;
				syncing = false;
				Sync();
				RenderMirrors();
			});
		};
		return card;
		void Sync()
		{
			summary.Text = (indexes.Count == 0) ? I18n.L("还没有填写") : ((indexes.Count == 1) ? indexes[0] : I18n.L("{0} 个市场", indexes.Count));
			summary.ToolTip = (indexes.Count > 0) ? string.Join("\n", indexes) : null;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Core;
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
		// —— 加速镜像（照 MaiBot git_mirror_service 语义）：每行 = 一个镜像对象（enabled + priority 数字），
		//    ↑↓ 交换相邻镜像的 priority，整份列表持久化到 plugin_mirrors ——
		SPanel mirrorHost = Ui.V(2.0);
		List<PluginMarket.GhMirror> mirrors = PluginMarket.DefaultMirrors();
		bool mirrorSyncing = false;
		async Task SaveMirrorsAsync()
		{
			await SaveAsync(new Dictionary<string, object>
			{
				["plugin_mirrors"] = mirrors.Select((PluginMarket.GhMirror m) => (object)PyDict.Of(
					"id", m.Id,
					"name", m.Label,
					"raw_prefix", m.RawPrefix,
					"download_prefix", m.DownloadPrefix,
					"enabled", m.Enabled,
					"priority", (long)m.Priority)).ToList()
			});
			AppServices.Toast(I18n.L("已保存"), I18n.L("镜像设置已更新"), ToastKind.Success);
			PluginsPage? page = AppServices.Window?.GetPage("plugins") as PluginsPage;
			page?.RefreshMarketSoon();
		}
		async Task AddMirrorDialogAsync()
		{
			TextBox nameBox = Ui.Input("我的镜像");
			TextBox rawBox = Ui.Input("https://example.com/https://raw.githubusercontent.com");
			TextBox dlBox = Ui.Input("https://example.com/https://github.com");
			SPanel body = Ui.V(8.0,
				Ui.Field(I18n.L("名称"), nameBox),
				Ui.Field(I18n.L("Raw 前缀"), rawBox, I18n.L("完整前缀，含 https://raw.githubusercontent.com")),
				Ui.Field(I18n.L("下载前缀"), dlBox, I18n.L("完整前缀，含 https://github.com")));
			Dictionary<string, object>? r = await Dlg.Form<Dictionary<string, object>?>(I18n.L("添加自定义镜像"), body, delegate
			{
				string name = nameBox.Text.Trim();
				string rawPrefix = rawBox.Text.Trim().TrimEnd('/');
				string dlPrefix = dlBox.Text.Trim().TrimEnd('/');
				if (name.Length == 0 || !rawPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !dlPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					throw new ArgumentException(I18n.L("名称必填；两个前缀都要以 https:// 开头"));
				}
				return new Dictionary<string, object>
				{
					["name"] = name,
					["raw_prefix"] = rawPrefix,
					["download_prefix"] = dlPrefix
				};
			});
			if (r == null)
			{
				return;
			}
			int maxPriority = mirrors.Count > 0 ? mirrors.Max((PluginMarket.GhMirror m) => m.Priority) : 0;
			mirrors.Add(new PluginMarket.GhMirror
			{
				Id = PluginMarket.CustomMirrorPrefix + Guid.NewGuid().ToString("N").Substring(0, 8),
				Label = (string)r["name"],
				RawPrefix = (string)r["raw_prefix"],
				DownloadPrefix = (string)r["download_prefix"],
				Enabled = true,
				Priority = maxPriority + 1
			});
			PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
			RenderMirrors();
		}
		void RenderMirrors()
		{
			mirrorHost.Children.Clear();
			mirrors = mirrors.OrderBy((PluginMarket.GhMirror m) => m.Priority).ThenBy((PluginMarket.GhMirror m) => m.Id, StringComparer.Ordinal).ToList();
			foreach (PluginMarket.GhMirror m in mirrors.ToList())
			{
				bool on = m.Enabled;
				CheckBox sw = Ui.Switch(on);
				TextBlock label = Ui.Txt(m.Label, 12.5, bold: true);
				TextBlock sub = Ui.Muted("Raw: " + m.RawPrefix);
				Button up = Ui.IconBtn("\ue70e", I18n.L("上移"));
				Button down = Ui.IconBtn("\ue70d", I18n.L("下移"));
				int index = mirrors.IndexOf(m);
				up.IsEnabled = index > 0;
				down.IsEnabled = index < mirrors.Count - 1;
				sw.Click += delegate
				{
					if (mirrorSyncing)
					{
						return;
					}
					m.Enabled = (sw.IsChecked == true);
					PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
					RenderMirrors();
				};
				RoutedEventHandler move = delegate(object s, RoutedEventArgs _)
				{
					if (mirrorSyncing || !(s is Button b))
					{
						return;
					}
					int cur = mirrors.IndexOf(m);
					int other = ((b == up) ? (cur - 1) : (cur + 1));
					if (cur < 0 || other < 0 || other >= mirrors.Count)
					{
						return;
					}
					int p = mirrors[cur].Priority;
					mirrors[cur].Priority = mirrors[other].Priority;
					mirrors[other].Priority = p;
					PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
					RenderMirrors();
				};
				up.Click += move;
				down.Click += move;
				SPanel actions = Ui.H(4.0, up, down);
				if (m.Id.StartsWith(PluginMarket.CustomMirrorPrefix, StringComparison.Ordinal))
				{
					Button del = Ui.IconBtn("\ue74d", I18n.L("删除"));
					del.Click += delegate
					{
						mirrors.Remove(m);
						PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
						RenderMirrors();
					};
					actions.Children.Add(del);
				}
				Border row = Ui.Card(Ui.G(null, "Auto,*,Auto,Auto")
					.Add(sw.VCenter(), 0)
					.Add(Ui.V(1.0, label, sub).M(10.0, 0.0, 6.0, 0.0), 0, 1)
					.Add(actions, 0, 2), 12.0);
				row.Opacity = (on ? 1.0 : 0.62);
				mirrorHost.Children.Add(row);
			}
		}
		Button mirrorReset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Ghost, delegate
		{
			mirrors = PluginMarket.DefaultMirrors();
			PageBase.Run(SaveMirrorsAsync, I18n.L("设置保存失败"));
			RenderMirrors();
		});
		Button mirrorAdd = Ui.Btn(I18n.L("添加自定义镜像"), BtnKind.Soft, delegate
		{
			PageBase.Run(AddMirrorDialogAsync, I18n.L("设置保存失败"));
		}, "\ue710");
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
		Border card = Ui.Card(Ui.V(2.0, Ui.Section(I18n.L("插件"), I18n.L("插件市场、加速镜像、未审核插件、插件可用的模型供应商")), Ui.Field(I18n.L("插件市场地址"), Ui.H(10.0, edit, summary.VCenter()), I18n.L("填写插件市场的索引地址，可以填多个")), Ui.Field(I18n.L("加速镜像"), Ui.V(6.0, Ui.Small(I18n.L("开关与顺序即优先级：排在前面的先尝试，全部失败才报错。Raw 前缀用于市场索引，下载前缀用于插件包")).Wrap(), mirrorHost, Ui.H(6.0, mirrorAdd, mirrorReset))), Ui.Field(I18n.L("允许安装未审核插件"), unverified, I18n.L("关着时，市场里未审核的插件要先打开这里才能安装")), Ui.Field("", Ui.H(8.0, manage, providers))));
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
				// get_settings 已回显默认（未配置 = 内置默认全开）；解析完整镜像对象
				mirrors = (s.TryGetValue("plugin_mirrors", out JsonElement mr) && mr.ValueKind == JsonValueKind.Array)
					? mr.EnumerateArray().Where((JsonElement x) => x.ValueKind == JsonValueKind.Object).Select((JsonElement x) => new PluginMarket.GhMirror
					{
						Id = (x.TryGetProperty("id", out JsonElement mid) && mid.ValueKind == JsonValueKind.String) ? (mid.GetString() ?? "") : "",
						Label = (x.TryGetProperty("name", out JsonElement mname) && mname.ValueKind == JsonValueKind.String) ? (mname.GetString() ?? "") : "",
						RawPrefix = (x.TryGetProperty("raw_prefix", out JsonElement mraw) && mraw.ValueKind == JsonValueKind.String) ? (mraw.GetString() ?? "") : "",
						DownloadPrefix = (x.TryGetProperty("download_prefix", out JsonElement mdl) && mdl.ValueKind == JsonValueKind.String) ? (mdl.GetString() ?? "") : "",
						Enabled = (x.TryGetProperty("enabled", out JsonElement men) && men.ValueKind == JsonValueKind.True),
						Priority = (x.TryGetProperty("priority", out JsonElement mp) && mp.ValueKind == JsonValueKind.Number && mp.TryGetInt32(out int mpv)) ? mpv : 999
					}).Where((PluginMarket.GhMirror m) => m.Id.Length > 0 && m.RawPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && m.DownloadPrefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)).ToList()
					: PluginMarket.DefaultMirrors();
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

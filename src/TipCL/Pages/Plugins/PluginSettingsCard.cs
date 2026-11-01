using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
		Border card = Ui.Card(Ui.V(2.0, Ui.Section(I18n.L("插件"), I18n.L("插件市场、未审核插件、插件可用的模型供应商")), Ui.Field(I18n.L("插件市场地址"), Ui.H(10.0, edit, summary.VCenter()), I18n.L("填写插件市场的索引地址，可以填多个")), Ui.Field(I18n.L("允许安装未审核插件"), unverified, I18n.L("关着时，市场里未审核的插件要先打开这里才能安装")), Ui.Field("", Ui.H(8.0, manage, providers))));
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
				syncing = true;
				unverified.IsChecked = s.TryGetValue("plugin_allow_unverified", out JsonElement a) && a.ValueKind == JsonValueKind.True;
				syncing = false;
				Sync();
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TipCL.Services;

namespace TipCL.Pages.Plugins;

// 插件设置：插件注册了 kind "settings" 视图就显示它（右上角可以切到自动表单）；
// 否则按 manifest config（string/boolean/integer/number/enum/secret）+ contributes.credentials 自动生成表单。
public sealed partial class PluginsPage
{
	private sealed class FieldRow
	{
		public string Key = "";

		public string Type = "string";

		public bool Secret;

		public bool Credential;

		public Func<object?>? Read;

		public object? Initial;

		public bool Clear;

		public double? Min;

		public double? Max;

		public string Label = "";
	}

	private void ShowSettings(PluginInfo p, bool forceAuto)
	{
		UiContribution? custom = PluginUi.All.FirstOrDefault((UiContribution c) => c.Plugin == p.Id && c.Kind == "settings");
		if (custom != null && !forceAuto)
		{
			PluginWebView web = new PluginWebView(custom.Url, p.Id);
			_subWeb = web;
			Border frame = new Border
			{
				BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
				Child = web
			};
			frame.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
			Button auto = Ui.Btn(I18n.L("高级（自动表单）"), BtnKind.Chip, delegate
			{
				ShowSettings(p, forceAuto: true);
			}, "\ue713");
			ShowSubView(I18n.L("{0} · 设置", p.DisplayName), frame, auto);
			_subWeb = web;
			return;
		}
		SPanel host = Ui.V(10.0, Ui.Skeleton(18.0, 240.0), Ui.Skeleton(16.0), Ui.Skeleton(16.0));
		host.Margin = new Thickness(26.0, 6.0, 26.0, 22.0);
		host.MaxWidth = 820.0;
		host.HorizontalAlignment = HorizontalAlignment.Left;
		UIElement? right = (custom != null) ? Ui.Btn(I18n.L("插件设置页"), BtnKind.Chip, delegate
		{
			ShowSettings(p, forceAuto: false);
		}, "\ue8a7") : null;
		ShowSubView(I18n.L("{0} · 设置", p.DisplayName), Ui.Scroll(host), right);
		PageBase.Run(() => FillAutoFormAsync(p, host), I18n.L("读取插件设置失败"));
	}

	private async Task FillAutoFormAsync(PluginInfo p, SPanel host)
	{
		JsonElement cfg = await PageBase.Api.TryCallAsync<JsonElement>("plugins_get_config", new { id = p.Id });
		JsonElement creds = await PageBase.Api.TryCallAsync<JsonElement>("plugins_credentials", new { id = p.Id });
		List<FieldRow> rows = new List<FieldRow>();
		host.Children.Clear();
		JsonElement schema = J.Obj(cfg, "schema");
		JsonElement values = J.Obj(cfg, "values");
		if (schema.ValueKind != JsonValueKind.Object && values.ValueKind != JsonValueKind.Object && cfg.ValueKind == JsonValueKind.Object)
		{
			// 内核只给了值：按值的类型推断
			values = cfg;
		}
		SPanel cfgCard = Ui.V(2.0, Ui.Section(I18n.L("配置"), I18n.L("改完点「保存」，插件里立即生效，不用重启")));
		int count = 0;
		IEnumerable<string> keys = (schema.ValueKind == JsonValueKind.Object) ? (from x in schema.EnumerateObject()
			select x.Name) : ((values.ValueKind == JsonValueKind.Object) ? (from x in values.EnumerateObject()
			select x.Name) : Array.Empty<string>());
		foreach (string key in keys)
		{
			JsonElement spec = (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(key, out JsonElement s)) ? s : default(JsonElement);
			JsonElement val = (values.ValueKind == JsonValueKind.Object && values.TryGetProperty(key, out JsonElement v)) ? v : J.Obj(spec, "__none__");
			if (val.ValueKind == JsonValueKind.Undefined && spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("default", out JsonElement d))
			{
				val = d;
			}
			if (J.Bool(spec, "hidden"))
			{
				continue;
			}
			FieldRow row = BuildField(key, spec, val, out UIElement control);
			rows.Add(row);
			string hint = J.Str(spec, "description");
			cfgCard.Children.Add(Ui.Field(row.Label, control, (hint.Length > 0) ? hint : null, 200.0));
			count++;
		}
		if (count == 0)
		{
			cfgCard.Children.Add(Ui.Muted(I18n.L("这个插件没有可以配置的项。")));
		}
		host.Children.Add(Ui.Card(cfgCard));
		List<JsonElement> credList = CredItems(creds);
		if (credList.Count > 0)
		{
			SPanel credCard = Ui.V(2.0, Ui.Section(I18n.L("凭据"), I18n.L("API Key 等，用 Windows 加密保存在本机，只有这个插件能读")));
			foreach (JsonElement c in credList)
			{
				string name = J.Str(c, "name");
				bool configured = J.Bool(c, "configured");
				FieldRow row2 = new FieldRow
				{
					Key = name,
					Credential = true,
					Secret = true,
					Label = J.Str(c, "label", name)
				};
				PasswordBox pw = Ui.Pw(280.0);
				pw.Tag = configured ? I18n.L("已配置，留空不修改") : I18n.L("未配置");
				row2.Read = () => pw.Password;
				Border state = configured ? Ui.Tag(I18n.L("已配置"), "B.Ok", "B.AccentSoft") : Ui.Tag(I18n.L("未配置"), "B.Warn", "B.WarnSoft");
				Button clear = Ui.Btn(I18n.L("清除"), BtnKind.Ghost);
				clear.IsEnabled = configured;
				clear.Click += delegate
				{
					row2.Clear = true;
					pw.Password = "";
					pw.Tag = I18n.L("保存后清除");
					clear.IsEnabled = false;
				};
				string desc = J.Str(c, "description");
				credCard.Children.Add(Ui.Field(row2.Label, Ui.H(8.0, pw, state, clear), (desc.Length > 0) ? desc : name, 200.0));
				rows.Add(row2);
			}
			host.Children.Add(Ui.Card(credCard));
		}
		Button save = Ui.Btn(I18n.L("保存"), BtnKind.Primary, null, "\ue74e");
		save.Click += delegate
		{
			PageBase.Run(() => SaveSettingsAsync(p, rows, save), I18n.L("保存失败"));
		};
		Button reset = Ui.Btn(I18n.L("重新读取"), BtnKind.Normal, delegate
		{
			ShowSettings(p, forceAuto: true);
		}, "\ue72c");
		host.Children.Add(Ui.H(8.0, save, reset));
	}

	private static List<JsonElement> CredItems(JsonElement creds)
	{
		List<JsonElement> list = J.Items(creds).ToList();
		if (list.Count > 0 || creds.ValueKind != JsonValueKind.Object)
		{
			return list;
		}
		// 也认 {NAME: {configured}} 的形式（credentials.status 的格式）
		List<JsonElement> mapped = new List<JsonElement>();
		foreach (JsonProperty prop in creds.EnumerateObject())
		{
			Dictionary<string, object> d = new Dictionary<string, object>
			{
				["name"] = prop.Name,
				["configured"] = J.Bool(prop.Value, "configured", prop.Value.ValueKind == JsonValueKind.True),
				["label"] = J.Str(prop.Value, "label", prop.Name),
				["description"] = J.Str(prop.Value, "description")
			};
			using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(d));
			mapped.Add(doc.RootElement.Clone());
		}
		return mapped;
	}

	private static FieldRow BuildField(string key, JsonElement spec, JsonElement val, out UIElement control)
	{
		string type = J.Str(spec, "type");
		if (type.Length == 0)
		{
			type = val.ValueKind switch
			{
				JsonValueKind.True => "boolean",
				JsonValueKind.False => "boolean",
				JsonValueKind.Number => "number",
				_ => "string",
			};
		}
		FieldRow row = new FieldRow
		{
			Key = key,
			Type = type,
			Label = J.Str(spec, "label", key),
			Secret = J.Bool(spec, "secret")
		};
		if (J.TryGet(spec, "min", out JsonElement mn) && mn.ValueKind == JsonValueKind.Number)
		{
			row.Min = mn.GetDouble();
		}
		if (J.TryGet(spec, "max", out JsonElement mx) && mx.ValueKind == JsonValueKind.Number)
		{
			row.Max = mx.GetDouble();
		}
		List<string> en = new List<string>();
		if (J.TryGet(spec, "enum", out JsonElement enumEl) && enumEl.ValueKind == JsonValueKind.Array)
		{
			en = enumEl.EnumerateArray().Select((JsonElement x) => (x.ValueKind == JsonValueKind.String) ? (x.GetString() ?? "") : x.GetRawText()).ToList();
		}
		List<string> labels = J.StrList(spec, "enumLabels");
		switch (type)
		{
		case "boolean":
		{
			bool b = val.ValueKind == JsonValueKind.True;
			CheckBox sw = Ui.Switch(b);
			row.Initial = b;
			row.Read = () => sw.IsChecked == true;
			control = sw;
			return row;
		}
		case "integer":
		case "number":
			if (en.Count == 0)
			{
				string text = (val.ValueKind == JsonValueKind.Number) ? val.GetRawText() : "";
				TextBox box = Ui.Input("", text, 160.0);
				row.Initial = text;
				row.Read = () => box.Text.Trim();
				control = box;
				return row;
			}
			break;
		}
		if (en.Count > 0)
		{
			List<string> shown = en.Select((string v, int i) => (i < labels.Count && labels[i].Length > 0) ? labels[i] : v).ToList();
			string cur = (val.ValueKind == JsonValueKind.String) ? (val.GetString() ?? "") : ((val.ValueKind == JsonValueKind.Undefined) ? "" : val.GetRawText());
			int idx = en.IndexOf(cur);
			ComboBox combo = Ui.Combo(shown, (idx >= 0) ? shown[idx] : null, 240.0);
			row.Initial = cur;
			row.Read = () => (combo.SelectedIndex >= 0 && combo.SelectedIndex < en.Count) ? en[combo.SelectedIndex] : cur;
			control = combo;
			return row;
		}
		string str = (val.ValueKind == JsonValueKind.String) ? (val.GetString() ?? "") : ((val.ValueKind == JsonValueKind.Undefined || val.ValueKind == JsonValueKind.Null) ? "" : val.GetRawText());
		if (row.Secret)
		{
			// 内核不回传密钥明文：有值时显示「已设置」，留空不改
			PasswordBox pw = Ui.Pw(280.0);
			pw.Tag = (str.Length > 0) ? I18n.L("已设置，留空不修改") : I18n.L("未设置");
			row.Initial = "";
			row.Read = () => pw.Password;
			control = pw;
			return row;
		}
		TextBox input = Ui.Input(J.Str(spec, "placeholder"), str, 360.0);
		row.Initial = str;
		row.Read = () => input.Text;
		control = input;
		return row;
	}

	private async Task SaveSettingsAsync(PluginInfo p, List<FieldRow> rows, Button save)
	{
		// 先校验，全部通过再写
		List<(FieldRow Row, object? Value)> changes = new List<(FieldRow, object)>();
		foreach (FieldRow r in rows)
		{
			if (r.Credential)
			{
				string v = (r.Read?.Invoke() as string) ?? "";
				if (r.Clear && v.Length == 0)
				{
					changes.Add((r, null));
				}
				else if (v.Length > 0)
				{
					changes.Add((r, v));
				}
				continue;
			}
			object? cur = r.Read?.Invoke();
			if (r.Secret)
			{
				if (cur is string s && s.Length > 0)
				{
					changes.Add((r, s));
				}
				continue;
			}
			if (Equals(cur, r.Initial))
			{
				continue;
			}
			if (r.Type == "integer" || r.Type == "number")
			{
				string text = (cur as string) ?? "";
				if (text.Length == 0)
				{
					changes.Add((r, null));
					continue;
				}
				if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || (r.Type == "integer" && Math.Abs(d - Math.Round(d)) > 1E-09))
				{
					PageBase.Toast(I18n.L("填写有误"), I18n.L("「{0}」要填{1}", r.Label, (r.Type == "integer") ? I18n.L("整数") : I18n.L("数字")), ToastKind.Warning);
					return;
				}
				if ((r.Min.HasValue && d < r.Min.Value) || (r.Max.HasValue && d > r.Max.Value))
				{
					PageBase.Toast(I18n.L("填写有误"), I18n.L("「{0}」超出范围 {1} ~ {2}", r.Label, r.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞", r.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"), ToastKind.Warning);
					return;
				}
				changes.Add((r, (r.Type == "integer") ? ((object)(long)Math.Round(d)) : ((object)d)));
			}
			else
			{
				changes.Add((r, cur));
			}
		}
		if (changes.Count == 0)
		{
			PageBase.Toast(I18n.L("没有改动"));
			return;
		}
		save.IsEnabled = false;
		try
		{
			foreach (var (row, value) in changes)
			{
				if (row.Credential)
				{
					// 用字典传参：null（删除）也要原样发出去，匿名对象会被 WhenWritingNull 省略
					await PageBase.Api.CallAsync("plugins_set_credential", new Dictionary<string, object?>
					{
						["id"] = p.Id,
						["name"] = row.Key,
						["value"] = value
					});
				}
				else
				{
					await PageBase.Api.CallAsync("plugins_set_config", new Dictionary<string, object?>
					{
						["id"] = p.Id,
						["key"] = row.Key,
						["value"] = value
					});
				}
			}
		}
		finally
		{
			save.IsEnabled = true;
		}
		PageBase.Toast(I18n.L("已保存"), I18n.L("{0} 项设置", changes.Count), ToastKind.Success);
		ShowSettings(p, forceAuto: true);
	}
}

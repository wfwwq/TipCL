using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// 一个问题块(= Windows <c>Pages\AskBlock.cs</c>,113 行)—— 标题(可多选时带「(可多选)」)+ 若干选项
/// (多选走 <c>CheckBox</c>、单选走带 <c>GroupName</c> 的 <c>RadioButton</c>)+ 一个"其它"输入框。
/// <see cref="Collect"/> 收回答案:`{ picked:[{id,label}...], other:"..." }`;两边都空 ⇒ <c>null</c>(上层的"还没选"提示据此)。
///
/// 与 Windows 的差异(逐条):
/// <list type="number">
/// <item><c>Visibility</c> → <c>IsVisible</c>;<c>SetResourceReference</c> 本文件没用;</item>
/// <item>开关的 <c>Checked/Unchecked</c> → **<c>IsCheckedChanged</c>**(Avalonia 11 把前两者标过时;语义同一时机);</item>
/// <item><c>Ui.Input</c>/<c>Ui.V</c>/<c>Ui.Txt</c> 与 Windows 同形;字号、间距、边距逐值相同;</item>
/// <item>选项 id 为 <c>"other"</c> 的那个控件仍是"显隐另一个输入框"的开关,判据一字不改。</item>
/// </list>
/// </summary>
internal sealed class AskBlock
{
	private readonly List<(ToggleButton Btn, string Id, string Label)> _opts = new List<(ToggleButton, string, string)>();

	private readonly TextBox _other = Ui.Input(I18n.L("选「其它」时在这里填"));

	private readonly bool _multi;

	private readonly ToggleButton? _otherBtn;

	public string Id { get; }

	public string Prompt { get; }

	public SPanel Root { get; }

	public AskBlock(JsonElement q)
	{
		string id = Str(q, "id");
		Id = ((id.Length > 0) ? id : "q1");
		string prompt = Str(q, "prompt");
		Prompt = ((prompt.Length > 0) ? prompt : I18n.L("请选择"));
		_multi = q.TryGetProperty("allow_multiple", out JsonElement multiple) && multiple.ValueKind == JsonValueKind.True;
		Root = Ui.V(5.0, Ui.Txt(Prompt + (_multi ? I18n.L("(可多选)") : ""), 12.5).Wrap());
		string groupName = Guid.NewGuid().ToString("N");
		if (q.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in options.EnumerateArray())
			{
				string oid = Str(item, "id");
				string label = Str(item, "label");
				string text = ((label.Length > 0) ? label : oid);
				ToggleButton toggle = _multi
					? new CheckBox { Content = text }
					: new RadioButton { Content = text, GroupName = groupName };
				toggle.Margin = new Thickness(0.0, 1.0, 0.0, 1.0);
				toggle.IsCheckedChanged += delegate
				{
					SyncOther();
				};
				if (oid == "other")
				{
					_otherBtn = toggle;
				}
				_opts.Add((toggle, oid, text));
				Root.Children.Add(toggle);
			}
		}
		_other.IsVisible = false;
		Root.Children.Add(_other);
	}

	/// <summary>只在"其它"被选中时显示输入框(Windows <c>SyncOther</c>)。</summary>
	private void SyncOther()
	{
		ToggleButton? otherBtn = _otherBtn;
		_other.IsVisible = otherBtn != null && otherBtn.IsChecked == true;
	}

	/// <summary>收回答案;没选任何选项且"其它"也空 ⇒ <c>null</c>(Windows <c>Collect</c>)。</summary>
	public object? Collect()
	{
		List<Dictionary<string, object>> picked = (from o in _opts
			where o.Btn.IsChecked == true
			select new Dictionary<string, object>
			{
				["id"] = o.Id,
				["label"] = o.Label,
			}).ToList();
		string other = _other.Text?.Trim() ?? "";
		if (picked.Count == 0 && other.Length == 0)
		{
			return null;
		}
		return new Dictionary<string, object>
		{
			["picked"] = picked,
			["other"] = other,
		};
	}

	private static string Str(JsonElement el, string key)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out JsonElement value))
		{
			return "";
		}
		if (value.ValueKind != JsonValueKind.String)
		{
			return value.ToString();
		}
		return value.GetString() ?? "";
	}
}

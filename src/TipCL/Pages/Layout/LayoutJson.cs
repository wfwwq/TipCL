using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TipCL.Pages.Layout;

// 布局文档 v2 的 JSON 值搬运工具(纯模型层,禁 WPF、禁界面):
//   1) 宽容读取:字段类型不符合契约时一律退回默认值,绝不抛(对齐 PageLayoutStore.ParseLoose 的思路);
//   2) 未知字段保留:把任意 JSON 子树转成"朴素值"(字典 / 列表 / 字符串 / 整数 / 浮点 / 布尔 / null),
//      这样两条写出通道都不用认识 JsonElement —— v2 文件走 Utils.WriteJson(PyJson),
//      导出/快照走 System.Text.Json,两边都能直接吃朴素值。
internal static class LayoutJson
{
	/// <summary>契约用的有序字典:插入顺序就是写出顺序(§3.2 的字段顺序照此排)。</summary>
	public static Dictionary<string, object?> Map()
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal);
	}

	/// <summary>取对象属性;属性缺失或类型不是对象时返回 false。</summary>
	public static bool TryObject(JsonElement e, string name, out JsonElement value)
	{
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
		{
			return true;
		}
		value = default(JsonElement);
		return false;
	}

	/// <summary>取数组属性;属性缺失或类型不是数组时返回 false。</summary>
	public static bool TryArray(JsonElement e, string name, out JsonElement value)
	{
		if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
		{
			return true;
		}
		value = default(JsonElement);
		return false;
	}

	/// <summary>把 JSON 对象转成朴素字典(未知字段保留用);不是对象时返回空字典。</summary>
	public static Dictionary<string, object?> Object(JsonElement e)
	{
		Dictionary<string, object?> map = Map();
		if (e.ValueKind != JsonValueKind.Object)
		{
			return map;
		}
		foreach (JsonProperty prop in e.EnumerateObject())
		{
			map[prop.Name] = Plain(prop.Value);
		}
		return map;
	}

	/// <summary>把任意 JSON 值转成朴素值(bool / long / double / string / List / Dictionary / null)。</summary>
	public static object? Plain(JsonElement e)
	{
		switch (e.ValueKind)
		{
		case JsonValueKind.Object:
		{
			Dictionary<string, object?> map = Map();
			foreach (JsonProperty prop in e.EnumerateObject())
			{
				map[prop.Name] = Plain(prop.Value);
			}
			return map;
		}
		case JsonValueKind.Array:
		{
			List<object?> list = new List<object?>();
			foreach (JsonElement item in e.EnumerateArray())
			{
				list.Add(Plain(item));
			}
			return list;
		}
		case JsonValueKind.String:
			return e.GetString() ?? "";
		case JsonValueKind.Number:
			return NumberValue(e);
		case JsonValueKind.True:
			return true;
		case JsonValueKind.False:
			return false;
		default:
			return null;
		}
	}

	/// <summary>只认 JSON 数字;其余(含字符串 "10")返回默认值。</summary>
	public static double Number(JsonElement e, double dflt)
	{
		if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
		{
			return d;
		}
		return dflt;
	}

	/// <summary>只认 JSON 整数;浮点按截断,其余返回默认值。</summary>
	public static int Integer(JsonElement e, int dflt)
	{
		if (e.ValueKind != JsonValueKind.Number)
		{
			return dflt;
		}
		if (e.TryGetInt32(out int i))
		{
			return i;
		}
		if (e.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d) && d >= -2147483648.0 && d <= 2147483647.0)
		{
			return (int)Math.Round(d, MidpointRounding.AwayFromZero);
		}
		return dflt;
	}

	/// <summary>只认 JSON 布尔(对齐 PageBlock.FromJson 的 hidden 写法:true 之外的写法不生效)。</summary>
	public static bool Bool(JsonElement e, bool dflt)
	{
		if (e.ValueKind == JsonValueKind.True)
		{
			return true;
		}
		if (e.ValueKind == JsonValueKind.False)
		{
			return false;
		}
		return dflt;
	}

	/// <summary>只认 JSON 字符串;其余返回默认值。</summary>
	public static string Text(JsonElement e, string dflt)
	{
		if (e.ValueKind == JsonValueKind.String)
		{
			return e.GetString() ?? dflt;
		}
		return dflt;
	}

	/// <summary>字符串数组:只收字符串项,丢掉空串与重复项(保持原顺序)。</summary>
	public static List<string> TextList(JsonElement e)
	{
		List<string> list = new List<string>();
		if (e.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonElement item in e.EnumerateArray())
		{
			string text = Text(item, "");
			if (text.Length != 0 && seen.Add(text))
			{
				list.Add(text);
			}
		}
		return list;
	}

	/// <summary>四元边距数组:不足补默认、超出截断;非数字项保留默认值。</summary>
	public static double[] Pad(JsonElement e, double[] dflt)
	{
		double[] result = new double[4];
		for (int i = 0; i < 4; i++)
		{
			result[i] = ((dflt != null && i < dflt.Length) ? dflt[i] : 0.0);
		}
		if (e.ValueKind != JsonValueKind.Array)
		{
			return result;
		}
		int index = 0;
		foreach (JsonElement item in e.EnumerateArray())
		{
			if (index >= 4)
			{
				break;
			}
			result[index] = Number(item, result[index]);
			index++;
		}
		return result;
	}

	/// <summary>写出用的数字:整数值输出 int(避免 "10" 变成 "10.0"),其余原样。</summary>
	public static object Num(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v))
		{
			return 0;
		}
		if (v >= -9.2233720368547758E+18 && v <= 9.2233720368547758E+18 && v == Math.Floor(v))
		{
			return (long)v;
		}
		return v;
	}

	/// <summary>写出用的四元边距数组(可空)。</summary>
	public static object? PadList(double[]? pad)
	{
		if (pad == null)
		{
			return null;
		}
		List<object?> list = new List<object?>(4);
		for (int i = 0; i < 4; i++)
		{
			list.Add(Num(i < pad.Length ? pad[i] : 0.0));
		}
		return list;
	}

	/// <summary>字符串数组 → 朴素列表。</summary>
	public static List<object?> ListOf(IEnumerable<string>? items)
	{
		List<object?> list = new List<object?>();
		if (items != null)
		{
			foreach (string item in items)
			{
				list.Add(item);
			}
		}
		return list;
	}

	/// <summary>把"未知字段"合并进写出字典:同名时已知字段优先(未知字段永远不覆盖生效字段)。</summary>
	public static void MergeExtra(Dictionary<string, object?> target, Dictionary<string, object?>? extra)
	{
		if (extra == null)
		{
			return;
		}
		foreach (KeyValuePair<string, object?> kv in extra)
		{
			if (!string.IsNullOrEmpty(kv.Key) && !target.ContainsKey(kv.Key))
			{
				target[kv.Key] = ClonePlain(kv.Value);
			}
		}
	}

	/// <summary>朴素值的深拷贝(字典/列表递归,其余值类型或字符串按值复制)。</summary>
	public static object? ClonePlain(object? value)
	{
		if (value is Dictionary<string, object?> map)
		{
			Dictionary<string, object?> copy = Map();
			foreach (KeyValuePair<string, object?> kv in map)
			{
				copy[kv.Key] = ClonePlain(kv.Value);
			}
			return copy;
		}
		if (value is List<object?> list)
		{
			List<object?> copy2 = new List<object?>(list.Count);
			foreach (object? item in list)
			{
				copy2.Add(ClonePlain(item));
			}
			return copy2;
		}
		return value;
	}

	/// <summary>把朴素字典里的值逐项深拷贝到新字典。</summary>
	public static Dictionary<string, object?> CloneMap(Dictionary<string, object?>? src)
	{
		Dictionary<string, object?> copy = Map();
		if (src != null)
		{
			foreach (KeyValuePair<string, object?> kv in src)
			{
				copy[kv.Key] = ClonePlain(kv.Value);
			}
		}
		return copy;
	}

	private static object NumberValue(JsonElement e)
	{
		if (e.TryGetInt64(out long l))
		{
			return l;
		}
		if (e.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
		{
			return d;
		}
		return 0L;
	}
}

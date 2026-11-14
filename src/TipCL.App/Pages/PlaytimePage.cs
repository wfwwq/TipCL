using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;

namespace TipCL.Pages;

/// <summary>
/// C6b:Windows 版 <c>Pages\PlaytimePage.cs</c>(10802 字节 / 312 行 / WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「游戏时长」页(不是导航页;它是设置页分段 5 内嵌的子页,见 <see cref="C6bSegmentRegistration"/> 的注入缝)。
///
/// 形状(逐条照抄 Windows):概览行(刷新 / 清空统计)/ 累计卡(34 号大字 + 副说明 + 大图标)/
/// 范围卡(「全部实例」或某个实例)/ 明细列表(按实例或按版本,每条一支细进度条 + 时长 + 查看 / 清除)。
///
/// 数据源与调用名一字未改:<c>get_instances</c> / <c>get_total_playtime</c> / <c>get_all_playtime</c> /
/// <c>get_playtime</c> / <c>format_playtime</c> / <c>clear_playtime</c>;
/// 时长文案先走后端 <c>format_playtime</c>,拿不到就用 <see cref="Fmt.Duration"/>(与 Windows 同序)。
///
/// Windows → Avalonia 的差异(逐条,理由写在行内):
/// <list type="number">
/// <item><c>TextBlock.SetResourceReference(ForegroundProperty, key)</c> → <c>Dyn(...)</c>(全仓统一口径);</item>
/// <item><c>Visibility</c> / <c>UIElement</c> → <c>Control</c>(本文件只经 <c>Ui</c> 拿控件);</item>
/// <item>时长缓存是 WPF 版的 <c>ConcurrentDictionary</c> + 400 条上限,**逐字保留**(纯数据,没有平台成分);</item>
/// <item><c>Motion.CountUp</c> / <c>Motion.Progress</c> / <c>Motion.Stagger</c> 在 mac 侧已是同名同参门面
///   (C3e 落地的),调用形状一字未改。</item>
/// </list>
/// </summary>
public sealed class PlaytimePage : PageBase
{
	private readonly SPanel _list = Ui.V(10.0);

	private readonly TextBlock _total = new TextBlock
	{
		FontSize = 34.0,
		FontWeight = FontWeight.Bold,
	};

	private readonly TextBlock _sub = Ui.Muted("");

	private readonly ComboBox _inst = Ui.Combo(new string[1] { AllInstances }, null, 190.0);

	private bool _sync;

	/// <summary>【Windows 源】<c>:30-32</c>:秒数 → 文案的进程内缓存(上限 400,超了整表清空)。</summary>
	private static readonly ConcurrentDictionary<long, string> _durCache = new ConcurrentDictionary<long, string>();

	private const int DurCacheMax = 400;

	private static string AllInstances => I18n.L("全部实例");

	/// <summary>【Windows 源】<c>:36</c>:标题「游戏时长」。</summary>
	public override string Title => I18n.L("游戏时长");

	/// <summary>【Windows 源】<c>:38-67</c>:两个动作 + 累计卡 + 范围卡 + 四个区块的 <c>BlockHost("playtime")</c>。</summary>
	public PlaytimePage()
	{
		Button clearAll = Ui.Btn(I18n.L("清空统计"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ClearAllAsync);
		}, "\uea99");
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadAsync);
		}, "\ue72c");
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(ReloadAsync);
			}
		};
		_total.Dyn(TextBlock.ForegroundProperty, "B.AccentDeep");
		Border totalCard = Ui.Card(Ui.G(null, "*,Auto").Add(Ui.V(4.0, Ui.Muted(I18n.L("累计游玩")), _total, _sub), 0).Add(Ui.Glyph("\ue823", 44.0, "B.AccentSoft2").VCenter(), 0, 1), 20.0);
		Border rangeCard = Ui.Card(Ui.H(10.0, Ui.Txt(I18n.L("范围"), 12.0, bold: false, "B.InkMuted").VCenter(), _inst.VCenter()), 12.0);
		Grid header = Ui.Section(I18n.L("游戏时长"), I18n.L("按实例与版本统计，每次退出游戏后自动累加"), Ui.H(8.0, refresh, clearAll));
		// 页面布局自定义(WP-U3):区块 = 概览 / 累计卡 / 范围卡 / 明细列表;默认全可见、顺序同旧 ScrollBody 实参
		Content = new BlockHost("playtime", new List<BlockSpec>
		{
			new BlockSpec { Id = "header", Title = I18n.L("概览"), Content = header },
			new BlockSpec { Id = "total", Title = I18n.L("累计卡"), Content = totalCard },
			new BlockSpec { Id = "range", Title = I18n.L("范围卡"), Content = rangeCard },
			new BlockSpec { Id = "list", Title = I18n.L("明细列表"), Content = _list },
		}).Root;
	}

	/// <summary>【Windows 源】<c>:69-76</c>:实例下拉的第一项永远是「全部实例」。</summary>
	protected override async Task LoadAsync()
	{
		List<InstanceInfo> instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(new string[1] { AllInstances }.Concat(instances.Select((InstanceInfo i) => i.Name)));
		_sync = false;
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:78-81</c>:刷新 = 重跑当前的"全部 / 单个"那一支。</summary>
	public override Task RefreshAsync()
	{
		return ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:83-91</c>:选到「全部实例」(或空)就按全部统计,否则只统计那一个实例。</summary>
	private Task ReloadAsync()
	{
		string text = _inst.Str();
		if (string.IsNullOrEmpty(text) || !(text != AllInstances))
		{
			return ReloadAllAsync();
		}

		return ReloadOneAsync(text);
	}

	/// <summary>【Windows 源】<c>:93-145</c>:全部实例:总时长 + 每个实例一条(版本明细最多 6 条)。</summary>
	private async Task ReloadAllAsync()
	{
		await ShowTotalAsync(await PageBase.Api.TryCallAsync("get_total_playtime", null, 0L));
		Dictionary<string, JsonElement> all = (await PageBase.Api.TryCallAsync("get_all_playtime", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		_sub.Text = I18n.L("{0} 个实例有记录", all.Count);
		_list.Children.Clear();
		if (all.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue823", I18n.L("还没有游玩记录"), I18n.L("启动一次游戏后这里就有数据了")));
			return;
		}

		List<KeyValuePair<string, JsonElement>> ordered = all.OrderByDescending((KeyValuePair<string, JsonElement> k) => Secs(k.Value)).ToList();
		long max = Math.Max(1L, ordered.Max((KeyValuePair<string, JsonElement> k) => Secs(k.Value)));
		await WarmAsync(ordered.SelectMany((KeyValuePair<string, JsonElement> kv) => Versions(kv.Value).Select((KeyValuePair<string, long> v) => v.Value).Concat(new long[1] { Secs(kv.Value) })));
		foreach (KeyValuePair<string, JsonElement> item in ordered)
		{
			string name = item.Key;
			long seconds = Secs(item.Value);
			ProgressBar bar = Ui.Prog();
			bar.Height = 6.0;
			List<string> versions = Versions(item.Value).OrderByDescending((KeyValuePair<string, long> v) => v.Value).Take(6).Select((KeyValuePair<string, long> v) => v.Key + " " + Cached(v.Value)).ToList();
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(6.0, Ui.Txt(name, 14.0, bold: true), bar, Ui.Small((versions.Count == 0) ? "—" : string.Join(" · ", versions)).Wrap()), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(Cached(seconds), 13.0, bold: true, "B.AccentDeep").Right(), Ui.Btn(I18n.L("查看"), BtnKind.Chip, delegate
			{
				_sync = true;
				_inst.Fill(_inst.Items.Cast<string>(), name);
				_sync = false;
				PageBase.Run(() => ReloadOneAsync(name));
			}).Right(), Ui.Btn(I18n.L("清除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ClearOneAsync(name));
			}).Right()).M(14.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			_list.Children.Add(Ui.Card(grid, 14.0));
			Motion.Progress(bar, (double)seconds * 100.0 / (double)max);
		}

		Motion.Stagger(_list, 30.0);
	}

	/// <summary>【Windows 源】<c>:147-193</c>:单个实例:按版本分段 + 「最近会话」(最多 10 条)。</summary>
	private async Task ReloadOneAsync(string instance)
	{
		JsonElement data = await PageBase.Api.TryCallAsync<JsonElement>("get_playtime", new { instance });
		long total = Secs(data);
		await ShowTotalAsync(total);
		List<KeyValuePair<string, long>> versions = Versions(data).OrderByDescending((KeyValuePair<string, long> v) => v.Value).ToList();
		List<string> sessions = Sessions(data);
		_sub.Text = I18n.L("实例「{0}」 · {1} 个版本 · {2} 次会话", instance, versions.Count, sessions.Count);
		_list.Children.Clear();
		if (total <= 0 && versions.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue823", I18n.L("这个实例还没有记录"), I18n.L("启动一次游戏后这里就有数据了")));
			return;
		}

		await WarmAsync(versions.Select((KeyValuePair<string, long> v) => v.Value));
		long max = ((versions.Count == 0) ? 1 : Math.Max(1L, versions.Max((KeyValuePair<string, long> v) => v.Value)));
		foreach (KeyValuePair<string, long> item in versions)
		{
			string version = item.Key;
			long seconds = item.Value;
			ProgressBar bar = Ui.Prog();
			bar.Height = 6.0;
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(6.0, Ui.Txt(version, 13.5, bold: true), bar), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(Cached(seconds), 13.0, bold: true, "B.AccentDeep").Right(), Ui.Btn(I18n.L("清除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ClearOneAsync(instance, version));
			}).Right()).M(14.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			_list.Children.Add(Ui.Card(grid, 13.0));
			Motion.Progress(bar, (double)seconds * 100.0 / (double)max);
		}

		if (sessions.Count > 0)
		{
			SPanel recent = Ui.V(4.0, Ui.Txt(I18n.L("最近会话"), 13.0, bold: true));
			foreach (string session in sessions.Take(10))
			{
				recent.Children.Add(Ui.Small(session).Wrap());
			}

			_list.Children.Add(Ui.Card(recent, 13.0));
		}

		Motion.Stagger(_list);
	}

	/// <summary>【Windows 源】<c>:195-206</c>:清空全部要确认(确认文案与按钮都是 Windows 的)。</summary>
	private async Task ClearAllAsync()
	{
		if (await Dlg.Confirm(I18n.L("清空游戏时长"), I18n.L("所有实例的时长记录都会归零。"), I18n.L("清空"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync("clear_playtime", new
			{
				instance = "",
				version = ""
			});
			await LoadAsync();
		}
	}

	/// <summary>【Windows 源】<c>:208-212</c>:清一个实例(或它下面一个版本)—— **不**要确认。</summary>
	private async Task ClearOneAsync(string instance, string version = "")
	{
		await PageBase.Api.CallAsync("clear_playtime", new { instance, version });
		await ReloadAsync();
	}

	/// <summary>【Windows 源】<c>:214-217</c>:缓存优先的后端文案。</summary>
	private static string Cached(long secs)
	{
		return _durCache.TryGetValue(secs, out string? hit) ? hit : Fmt.Duration(secs);
	}

	/// <summary>【Windows 源】<c>:219-226</c>:累计数字走 <c>Motion.CountUp</c>,停在真实文案上。</summary>
	private async Task ShowTotalAsync(long seconds)
	{
		string text = await FormatAsync(seconds);
		Motion.CountUp(_total, 0.0, (double)seconds / 3600.0, "0.0", 620.0, delegate
		{
			_total.Text = text;
		});
	}

	/// <summary>【Windows 源】<c>:228-245</c>:先问后端 <c>format_playtime</c>,拿不到才用本地 <see cref="Fmt.Duration"/>。</summary>
	private static async Task<string> FormatAsync(long seconds)
	{
		if (_durCache.TryGetValue(seconds, out string? hit))
		{
			return hit;
		}

		string text = (await PageBase.Api.TryCallAsync("format_playtime", new { seconds }, "")) ?? "";
		if (string.IsNullOrWhiteSpace(text))
		{
			text = Fmt.Duration(seconds);
		}

		if (_durCache.Count >= DurCacheMax)
		{
			_durCache.Clear();
		}

		_durCache[seconds] = text;
		return text;
	}

	/// <summary>【Windows 源】<c>:247-256</c>:把列表里要用到的秒数先并发问一遍(最多 60 个不重复值)。</summary>
	private static Task WarmAsync(IEnumerable<long> values)
	{
		List<long> keys = values.Where((long v) => v > 0 && !_durCache.ContainsKey(v)).Distinct().Take(60).ToList();
		if (keys.Count != 0)
		{
			return Task.WhenAll(keys.Select((long v) => FormatAsync(v)));
		}

		return Task.CompletedTask;
	}

	/// <summary>【Windows 源】<c>:258-265</c>:一个记录对象的 <c>total</c>(缺字段 / 非数字都是 0)。</summary>
	private static long Secs(JsonElement el)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("total", out JsonElement value) || !value.TryGetInt64(out long seconds))
		{
			return 0L;
		}

		return seconds;
	}

	/// <summary>【Windows 源】<c>:267-282</c>:<c>versions</c> 里的 (版本 id, 秒数),只要正数。</summary>
	private static List<KeyValuePair<string, long>> Versions(JsonElement el)
	{
		List<KeyValuePair<string, long>> rows = new List<KeyValuePair<string, long>>();
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("versions", out JsonElement versions) || versions.ValueKind != JsonValueKind.Object)
		{
			return rows;
		}

		foreach (JsonProperty item in versions.EnumerateObject())
		{
			if (item.Value.TryGetInt64(out long seconds) && seconds > 0)
			{
				rows.Add(new KeyValuePair<string, long>(item.Name, seconds));
			}
		}

		return rows;
	}

	/// <summary>【Windows 源】<c>:284-311</c>:<c>sessions</c> 组装成「版本 · 开始时间 · 时长」,倒序。</summary>
	private static List<string> Sessions(JsonElement el)
	{
		List<string> rows = new List<string>();
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("sessions", out JsonElement sessions) || sessions.ValueKind != JsonValueKind.Array)
		{
			return rows;
		}

		foreach (JsonElement item in sessions.EnumerateArray().Reverse())
		{
			if (item.ValueKind == JsonValueKind.Object)
			{
				string version = item.TryGetProperty("version", out JsonElement versionEl) ? versionEl.ToString() : "";
				string start = item.TryGetProperty("start", out JsonElement startEl) ? startEl.ToString() : "";
				long secs = (item.TryGetProperty("seconds", out JsonElement secsEl) && secsEl.TryGetInt64(out long parsed)) ? parsed : 0;
				rows.Add(string.Join(" · ", new string[3]
				{
					version,
					start,
					Cached(secs),
				}.Where((string x) => !string.IsNullOrWhiteSpace(x))));
			}
		}

		return rows;
	}
}

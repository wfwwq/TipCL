using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TipCL.Core.Launcher.Layout;

// 来源: Windows 版冻结件 wt-r31-merge @0a304a7(lead 裁定 R32-D6:布局 v2 模型层以冻结件为准)
//       Windows 参考仓库 src/TipCL/Core/Launcher/Layout/LayoutV1Rpc.cs(SYS-01 / AP-01 的落地件)
// 移植口径(R32-MAC-SYS01):**逐字搬**,不做 WPF/Avalonia 适配 —— 本文件零 WPF / 零 Avalonia 依赖,
// 只碰 Core 的 PyDict / PyJson / ValueError 与同目录的 LayoutV2 / LayoutStoreV2 / LayoutSchema;
// 这四件在 mac 树与 Windows 冻结件逐字节相同(见 progress/R32-PARITY.md §5.1),所以没有第 2 套语义。

/// <summary>
/// 一次布局 RPC 调用的结果(<see cref="LayoutV1Rpc"/> 用)。不用 out 参数是为了让测试与调用方都读得顺。
/// </summary>
public sealed class LayoutRpcResult
{
	/// <summary>改动是否**真的落到 v2 真源**(就是 <see cref="LayoutStoreV2.Save"/> 的返回值)。</summary>
	public bool Saved { get; set; }

	/// <summary>受影响的方案名;没动方案表时是当前的 <see cref="LayoutV2.ActiveProfile"/>。</summary>
	public string Profile { get; set; } = "";

	/// <summary>人类可读的中文说明(直接进返回值,告诉调用方"到底做了什么 / 为什么没做")。</summary>
	public string Message { get; set; } = "";
}

/// <summary>
/// <b>SYS-01 / AP-01(R31 布局审计 P2):把布局 v1 的 7 个 RPC 接到 v2 真源上。</b>
///
/// <para><b>病</b>:这 7 个 RPC(<c>get_layout</c> / <c>save_layout</c> / <c>save_layout_profile</c> /
/// <c>activate_layout_profile</c> / <c>delete_layout_profile</c> / <c>reset_layout</c> / <c>import_layout</c>)
/// 过去只读写 <c>config.json</c> 的 <c>ui_layout</c> / <c>ui_layouts</c> / <c>ui_layout_profile</c> 三个键,
/// 而 v2 落地后**这三个键在本进程内 0 个消费者**(旧画布 <c>DashHost</c> 不进可视树,
/// 见 <c>Pages/LaunchPage.cs</c> 的 DashHost 段与 <c>docs/layout-v2-design.md</c> 的能力清单)
/// → 调用方拿到 <c>saved:true</c> 却屏幕上什么都不变。这是"静默无效",比报错更坏。</para>
///
/// <para><b>治</b>:不删任何一个 RPC(名字 / 签名 / 返回字段都留着),把它们**转调 v2 真源**
/// (<c>ui_layout_v2.json</c>,<see cref="LayoutStoreV2"/>)。语义只挑 v2 里真的存在的那些:</para>
/// <list type="bullet">
/// <item><c>get_layout</c> → v2 快照:<c>doc</c> = v2 的 LAYOUT 对象(页 / 块 / 壳 / 自建块 / 分栏),
///   <c>profile</c> / <c>profiles</c> / <c>default</c> 全部取自 v2,并新增 <c>pages</c>(紧凑摘要)与
///   <c>api</c>=<c>"v2"</c> 标记;<c>min_sizes</c> 沿用 v1 的冻结表(没有删)。</item>
/// <item><c>save_layout</c> → 把入参里的**顺序 + 显隐**并进目标页(v2 侧唯一的那份实现
///   <see cref="LayoutStoreV2.MergeV1Order"/>,与 AI 工具 <c>layout_apply</c> 的 <c>blocks</c> 同一口径)。</item>
/// <item><c>save_layout_profile</c> / <c>activate_layout_profile</c> / <c>delete_layout_profile</c> /
///   <c>reset_layout</c> → 直接操作 v2 的 <see cref="LayoutV2.Profiles"/> / <see cref="LayoutV2.ActiveProfile"/>
///   与真源 <see cref="LayoutV2.Layout"/>。</item>
/// <item><c>import_layout</c> → v2 形状的整机方案 = 整套 LAYOUT 替换 + 方案表**按名字合并**(与 AI 工具的
///   <c>json</c> 参数同一口径);v1 形状(<c>items[]</c>)= 只并入顺序与显隐(与 M-01/D-04 的
///   「旧 v1 文件只并入顺序与显隐」同一口径)。</item>
/// </list>
///
/// <para><b>明确不做</b>(写下来免得后人以为丢了功能):这些入口**不再**能改画布几何(x/y/w/h)、
/// 自建块定义、分栏、壳层、顶栏页签 —— v1 从来没有这些概念,它们归 v2 的其他入口
/// (AI 的 <c>layout_apply</c> / <c>layout_v2_apply</c>、设置 → 布局、直接编辑 <c>ui_layout_v2.json</c>)。
/// 另外:<b>只改目标页里已经存在的区块</b>的先后与显隐,入参里多出来的 id 一律**不新建**
/// (凭空塞块会让界面出现「区块不可用」占位);被忽略的 id 会在返回值里报出来,不静默丢。</para>
///
/// <para><b>不再静默</b>:方案名不存在时 <c>activate_layout_profile</c> 过去会"清成默认布局"当没发生,
/// 现在抛 <see cref="ValueError"/>;<c>saved</c> 的含义从 v1 的"没换方案名"改成"v2 真源真的写成功了"。</para>
///
/// <para>零 WPF / 零 Avalonia 依赖:net8 测试宿主可直接加载(net8 宿主碰到 WPF 类型会加载失败)。</para>
/// </summary>
public static class LayoutV1Rpc
{
	/// <summary>返回值里的接口标记:<c>"v2"</c> 表示 doc 已经是 v2 形状。</summary>
	public const string ApiVersion = "v2";

	/// <summary>v1 画布(启动页仪表盘)在 v2 里对应的 pageId。</summary>
	public const string CanvasPageId = "launch";

	/// <summary>返回值里给调用方看的迁移说明(不改字段名,只加一个说明键)。</summary>
	public const string LegacyNote =
		"布局 v1 的这条 RPC 已转调 v2 真源(ui_layout_v2.json):doc 现在是 v2 的 LAYOUT 对象(页/块/壳/自建块/分栏)。"
		+ "旧 config.json 的 ui_layout / ui_layouts / ui_layout_profile 三个键已不再有任何效果;"
		+ "画布几何(x/y/w/h)、自建块、分栏、壳层请走 v2(layout_v2_apply 或设置 → 布局)。";

	/// <summary>入参形态(v1 的 doc 与 v2 的 doc 都能收)。</summary>
	public enum Shape
	{
		/// <summary>认不出来。</summary>
		Unknown,

		/// <summary>v2 信封:有 <c>kind=tipcl-layout</c> 且 <c>version=2</c>。</summary>
		EnvelopeV2,

		/// <summary>v2 的 LAYOUT 对象(有 pages / shell / widgets / splits 之一,没有 items)。</summary>
		BodyV2,

		/// <summary>v1 画布文档(有 <c>items[]</c>)。</summary>
		DocV1
	}

	// ————— 1. get_layout —————

	/// <summary>v1 <c>get_layout</c> 的 v2 版:v2 真源快照。零副作用。</summary>
	public static PyDict Snapshot()
	{
		LayoutV2 doc = LayoutStoreV2.Load();
		PyDict map = new PyDict();
		map["api"] = ApiVersion;
		map["doc"] = doc.Layout.ToDict();
		map["profile"] = doc.ActiveProfile;
		map["profiles"] = ProfileNames(doc);
		map["default"] = LayoutV2.CreateDefault().Layout.ToDict();
		map["pages"] = PageSummary(doc);
		map["legacy"] = LegacyNote;
		return map;
	}

	/// <summary>方案名(排序,便于调用方比对)。</summary>
	public static List<object?> ProfileNames(LayoutV2? doc)
	{
		if (doc == null || doc.Profiles == null)
		{
			return new List<object?>();
		}
		return doc.Profiles.Keys.OrderBy((string k) => k, StringComparer.Ordinal).Cast<object?>().ToList();
	}

	/// <summary>每页的紧凑摘要:模式 / 列数 / 间距 / 区块顺序(隐藏的加 <c>(hidden)</c>)。</summary>
	public static Dictionary<string, object?> PageSummary(LayoutV2? doc)
	{
		Dictionary<string, object?> pages = LayoutJson.Map();
		if (doc == null || doc.Layout == null || doc.Layout.Pages == null)
		{
			return pages;
		}
		foreach (KeyValuePair<string, PageLayoutV2> kv in doc.Layout.Pages)
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
			{
				continue;
			}
			Dictionary<string, object?> page = LayoutJson.Map();
			page["mode"] = kv.Value.Mode;
			page["cols"] = (long)kv.Value.Cols;
			page["gap"] = kv.Value.Gap;
			page["blocks"] = BlockLines(kv.Value);
			pages[kv.Key] = page;
		}
		return pages;
	}

	// ————— 2. save_layout —————

	/// <summary>
	/// v1 <c>save_layout</c> 的 v2 版:把 <paramref name="doc"/> 里的顺序 + 显隐并进目标页并落盘。
	/// <paramref name="expectedProfile"/> 给非空时做乐观并发检查(与 v1 一致:对不上就一个字节都不写)。
	/// 目标页:v1 形状的 doc 恒为 <see cref="CanvasPageId"/>;v2 形状按各自 pageId。
	/// </summary>
	public static LayoutRpcResult SaveCanvas(object? doc, string? expectedProfile = null)
	{
		LayoutV2 current = LayoutStoreV2.Load();
		string expected = (expectedProfile ?? "").Trim();
		LayoutRpcResult result = new LayoutRpcResult
		{
			Profile = current.ActiveProfile
		};
		if (expected.Length != 0 && !string.Equals(expected, current.ActiveProfile, StringComparison.Ordinal))
		{
			result.Saved = false;
			result.Message = "方案已经被换成「" + current.ActiveProfile + "」了,这次保存没有写盘;先 get_layout 看最新布局再重试。";
			return result;
		}
		LayoutV2 next = current.Clone();
		List<string> ignored = new List<string>();
		int touched = MergeInto(next, doc, ignored);
		if (touched == 0)
		{
			result.Saved = false;
			result.Message = (ignored.Count != 0)
				? ("没有可改的区块:入参里的 " + string.Join(" / ", ignored) + " 在这一页里不存在。这些入口只改页里已有的区块(不新建),要加区块请走 v2 的 layout_v2_apply。")
				: "入参里没有能识别的区块顺序(doc.items[].type 或 doc.pages[].blocks[].id 都没有)。";
			return result;
		}
		// saved = v2 真源真的写成功了(不再是 v1 那个"没换方案名"的含义)
		result.Saved = LayoutStoreV2.Save(next);
		LayoutStoreV2.RefreshV1Cache();
		result.Profile = next.ActiveProfile;
		result.Message = "已把 " + touched + " 个页面的区块顺序 / 显隐写进 v2 真源"
			+ ((ignored.Count != 0) ? (";入参里多出来的 " + string.Join(" / ", ignored) + " 没有新建(这一页里不存在)") : "")
			+ "。方案表与当前方案名都没动。";
		return result;
	}

	// ————— 3. save_layout_profile —————

	/// <summary>
	/// v1 <c>save_layout_profile</c> 的 v2 版:把当前布局(或入参给的布局)存成方案 <paramref name="name"/> 并激活。
	/// 空名字抛 <see cref="ValueError"/>(与 v1 同一句话);方案表满(<see cref="LayoutSchema.MaxProfiles"/>)时
	/// 也抛 —— 过去会由 Sanitize 静默丢掉,等于骗调用方"存上了"。
	/// </summary>
	public static LayoutRpcResult SaveProfile(string? name, object? doc = null)
	{
		LayoutV2 next = LayoutStoreV2.Load();
		string profile = (name ?? "").Trim();
		if (profile.Length == 0)
		{
			throw new ValueError("方案名称不能为空");
		}
		LayoutBody body;
		List<string> ignored = new List<string>();
		if (doc == null)
		{
			body = next.Layout.Clone();
		}
		else
		{
			LayoutBody? given = ReadBody(doc);
			if (given != null)
			{
				body = given;
			}
			else
			{
				// v1 形状(或形状不明):只取它能表达的顺序 + 显隐,套在当前布局上
				LayoutV2 merged = next.Clone();
				int touched = MergeInto(merged, doc, ignored);
				if (touched == 0)
				{
					throw new ValueError("不是有效的布局文档");
				}
				body = merged.Layout;
			}
		}
		bool isNew = !next.Profiles.ContainsKey(profile);
		if (isNew && next.Profiles.Count >= LayoutSchema.MaxProfiles)
		{
			throw new ValueError("布局方案最多 " + LayoutSchema.MaxProfiles + " 个,先删掉几个再存(delete_layout_profile)。");
		}
		next.Profiles[profile] = body;
		next.ActiveProfile = profile;
		bool saved = LayoutStoreV2.Save(next);
		LayoutStoreV2.RefreshV1Cache();
		return new LayoutRpcResult
		{
			Saved = saved,
			Profile = profile,
			Message = "已把当前布局存成方案「" + profile + "」并激活"
				+ ((ignored.Count != 0) ? (";入参里多出来的 " + string.Join(" / ", ignored) + " 没有新建") : "") + "。"
		};
	}

	// ————— 4. activate_layout_profile —————

	/// <summary>
	/// v1 <c>activate_layout_profile</c> 的 v2 版:把方案快照应用到当前布局。
	/// 空名字 = 回到直接编辑态(布局清成"各页自带默认");**名字不存在时抛 <see cref="ValueError"/>** ——
	/// v1 在这里会静默清空布局,那种"你做错了,我把你的布局清了"正是本包要消灭的静默行为。
	/// </summary>
	public static LayoutRpcResult Activate(string? name)
	{
		LayoutV2 next = LayoutStoreV2.Load();
		string profile = (name ?? "").Trim();
		if (profile.Length == 0)
		{
			next.Layout = new LayoutBody();
			next.ActiveProfile = "";
			bool saved = LayoutStoreV2.Save(next);
			LayoutStoreV2.RefreshV1Cache();
			return new LayoutRpcResult
			{
				Saved = saved,
				Profile = "",
				Message = "方案名为空:已回到直接编辑态(布局清成各页自带默认,命名方案都还在)。"
			};
		}
		if (next.Profiles == null || !next.Profiles.TryGetValue(profile, out LayoutBody? body) || body == null)
		{
			throw new ValueError("布局方案「" + profile + "」不存在,当前的布局一个字节都没动;先用 get_layout 看现有方案名。");
		}
		next.Layout = body.Clone();
		next.ActiveProfile = profile;
		bool saved2 = LayoutStoreV2.Save(next);
		LayoutStoreV2.RefreshV1Cache();
		return new LayoutRpcResult
		{
			Saved = saved2,
			Profile = profile,
			Message = "已应用方案「" + profile + "」。"
		};
	}

	// ————— 5. delete_layout_profile —————

	/// <summary>v1 <c>delete_layout_profile</c> 的 v2 版。返回 false = 没有这个方案(调用方据此报 ValueError)。</summary>
	public static bool Delete(string? name)
	{
		LayoutV2 next = LayoutStoreV2.Load();
		string profile = (name ?? "").Trim();
		if (next.Profiles == null || !next.Profiles.ContainsKey(profile))
		{
			return false;
		}
		next.Profiles.Remove(profile);
		if (string.Equals(next.ActiveProfile, profile, StringComparison.Ordinal))
		{
			// 删掉的正是生效方案 → 回到直接编辑态(布局留原样,不连布局一起清,免得用户莫名其妙丢东西)
			next.ActiveProfile = "";
		}
		LayoutStoreV2.Save(next);
		LayoutStoreV2.RefreshV1Cache();
		return true;
	}

	// ————— 6. reset_layout —————

	/// <summary>
	/// v1 <c>reset_layout</c> 的 v2 版:布局清成"各页自带默认"(= 空 LAYOUT),方案表与方案名照 v1 的语义处理
	/// (方案留着,这样"恢复默认"之后还能切回去)。
	/// </summary>
	public static LayoutRpcResult ResetToDefault()
	{
		LayoutV2 next = LayoutStoreV2.Load();
		next.Layout = new LayoutBody();
		next.ActiveProfile = "";
		bool saved = LayoutStoreV2.Save(next);
		LayoutStoreV2.RefreshV1Cache();
		return new LayoutRpcResult
		{
			Saved = saved,
			Profile = "",
			Message = "布局已恢复默认(各页回到自带默认布局);命名方案 " + next.Profiles.Count + " 个都还在。"
		};
	}

	// ————— 7. import_layout —————

	/// <summary>
	/// v1 <c>import_layout</c> 的 v2 版。两种入参两种语义(与 D-04 / M-01 定下的同一套规则):
	/// v2 形状(整机方案)→ **整套 LAYOUT 替换** + 方案表按名字合并(不整表换掉,原有方案都在)+
	/// active_profile 取文件里的值;v1 形状(items[])→ **只并入顺序与显隐**(目标页见 <see cref="CanvasPageId"/>)。
	/// 认不出来抛 <see cref="ValueError"/>(v1 的"不是有效的布局文件")。
	/// </summary>
	public static LayoutRpcResult Import(object? doc)
	{
		LayoutV2 next = LayoutStoreV2.Load();
		Shape shape = ShapeOf(doc);
		if (shape == Shape.EnvelopeV2 || shape == Shape.BodyV2)
		{
			LayoutV2? parsed = ReadEnvelope(doc);
			if (parsed == null)
			{
				throw new ValueError("不是有效的布局文件");
			}
			next.Layout = parsed.Layout;
			// 方案表按名字合并(不整表换掉):文件里没带的方案照旧保留,同名才用文件里的覆盖
			foreach (KeyValuePair<string, LayoutBody> kv in parsed.Profiles)
			{
				if (kv.Value != null && (next.Profiles.ContainsKey(kv.Key) || next.Profiles.Count < LayoutSchema.MaxProfiles))
				{
					next.Profiles[kv.Key] = kv.Value;
				}
			}
			// active_profile 取文件里的值(与 AI 工具 layout_apply 的 json 参数同一口径);
			// 那份方案没进来(被上限挡住 / 名字空)= 直接编辑态,不留一个和当前布局不符的方案名
			next.ActiveProfile = ((parsed.ActiveProfile != null && next.Profiles.ContainsKey(parsed.ActiveProfile)) ? parsed.ActiveProfile : "");
			bool saved = LayoutStoreV2.Save(next);
			LayoutStoreV2.RefreshV1Cache();
			return new LayoutRpcResult
			{
				Saved = saved,
				Profile = next.ActiveProfile,
				Message = "已按整机 v2 方案导入:整套布局替换,方案表按名字合并(原有方案都在)。"
			};
		}
		if (shape == Shape.DocV1)
		{
			List<string> ignored = new List<string>();
			int touched = MergeInto(next, doc, ignored);
			if (touched == 0)
			{
				return new LayoutRpcResult
				{
					Saved = false,
					Profile = next.ActiveProfile,
					Message = (ignored.Count != 0)
						? ("这份 v1 布局里的 " + string.Join(" / ", ignored) + " 在 v2 的启动页里不存在,没有可导入的内容(这些入口只改页里已有的区块)。")
						: "这份 v1 布局里没有可用的卡片。"
				};
			}
			bool saved2 = LayoutStoreV2.Save(next);
			LayoutStoreV2.RefreshV1Cache();
			return new LayoutRpcResult
			{
				Saved = saved2,
				Profile = next.ActiveProfile,
				Message = "已导入 v1 布局:只并入顺序与显隐(画布几何在 v2 里没有对应概念)"
					+ ((ignored.Count != 0) ? (";忽略了 " + string.Join(" / ", ignored)) : "") + "。"
			};
		}
		throw new ValueError("不是有效的布局文件");
	}

	// ————— 入参识别 —————

	/// <summary>认一下入参是哪一版布局文档(只读,给测试与调用方判断用)。</summary>
	public static Shape ShapeOf(object? doc)
	{
		JsonElement root;
		if (!TryRoot(doc, out root))
		{
			return Shape.Unknown;
		}
		if (LayoutJson.TryArray(root, "items", out JsonElement _))
		{
			return Shape.DocV1;
		}
		if (PropText(root, "kind", "") == LayoutSchema.Kind)
		{
			return Shape.EnvelopeV2;
		}
		if (LayoutJson.TryObject(root, "pages", out JsonElement _)
			|| LayoutJson.TryObject(root, "shell", out JsonElement _)
			|| LayoutJson.TryObject(root, "splits", out JsonElement _)
			|| LayoutJson.TryArray(root, "widgets", out JsonElement _))
		{
			return Shape.BodyV2;
		}
		return Shape.Unknown;
	}

	/// <summary>入参 → v2 的 LAYOUT body(只对 v2 形状有效;其余返回 null)。</summary>
	private static LayoutBody? ReadBody(object? doc)
	{
		JsonElement root;
		if (!TryRoot(doc, out root))
		{
			return null;
		}
		Shape shape = ShapeOf(doc);
		if (shape == Shape.BodyV2)
		{
			return LayoutSchema.SanitizeBody(LayoutBody.FromJson(root), 0);
		}
		if (shape == Shape.EnvelopeV2)
		{
			LayoutV2? parsed = ReadEnvelope(doc);
			return parsed?.Layout;
		}
		return null;
	}

	/// <summary>入参 → v2 信封(只对 v2 形状有效;其余返回 null)。</summary>
	private static LayoutV2? ReadEnvelope(object? doc)
	{
		LayoutV2? parsed = LayoutV2.FromJsonText(JsonOf(doc));
		if (parsed != null)
		{
			return parsed;
		}
		JsonElement root;
		if (!TryRoot(doc, out root) || ShapeOf(doc) != Shape.BodyV2)
		{
			return null;
		}
		// 只有 LAYOUT 对象(没有信封):补一个信封再按同一套规矩 Sanitize
		LayoutV2 envelope = LayoutV2.CreateDefault();
		envelope.Layout = LayoutBody.FromJson(root);
		return LayoutSchema.Sanitize(envelope);
	}

	// ————— 顺序 / 显隐 的合并(两种入参形状 → 同一份实现) —————

	/// <summary>
	/// 把入参里的顺序 + 显隐并进 <paramref name="next"/> 的目标页,返回改动的页面数。
	/// 共用实现是 <see cref="LayoutStoreV2.MergeV1Order"/>(AI 工具与 Pages 的 LayoutEngine.MergeV1 走同一个);
	/// 这里多加一条规矩:**只改目标页里已经存在的块**,入参里多出来的 id 记进 <paramref name="ignored"/> 不新建。
	/// </summary>
	private static int MergeInto(LayoutV2 next, object? doc, List<string> ignored)
	{
		if (next == null || next.Layout == null)
		{
			return 0;
		}
		Shape shape = ShapeOf(doc);
		if (shape == Shape.DocV1)
		{
			List<LayoutV1Block> ordered = V1Items(doc);
			if (ordered.Count == 0)
			{
				return 0;
			}
			return MergePage(next, CanvasPageId, ordered, ignored) ? 1 : 0;
		}
		if (shape == Shape.BodyV2 || shape == Shape.EnvelopeV2)
		{
			LayoutBody? body = ReadBody(doc);
			if (body == null || body.Pages == null)
			{
				return 0;
			}
			int touched = 0;
			foreach (KeyValuePair<string, PageLayoutV2> kv in body.Pages)
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Value.Blocks == null || kv.Value.Blocks.Count == 0)
				{
					continue;
				}
				List<LayoutV1Block> ordered = kv.Value.Blocks
					.Where((BlockLayout b) => b != null && !string.IsNullOrEmpty(b.Id))
					.Select((BlockLayout b) => new LayoutV1Block { Id = b.Id, Hidden = b.Hidden })
					.ToList();
				if (ordered.Count != 0 && MergePage(next, kv.Key, ordered, ignored))
				{
					touched++;
				}
			}
			return touched;
		}
		return 0;
	}

	/// <summary>并一页;返回是否真的动了(v2 里没有这一页 / 一个 id 都对不上 = 没动)。</summary>
	private static bool MergePage(LayoutV2 next, string pageId, List<LayoutV1Block> ordered, List<string> ignored)
	{
		if (!next.Layout.Pages.TryGetValue(pageId, out PageLayoutV2? existing) || existing == null)
		{
			// 真源里还没有这一页 ⇒ 没有任何"已有区块"可改。凭空按 v1 的卡片名建页会往界面塞「区块不可用」占位,
			// 所以这里选择"什么都不做 + 报出来",而不是编一份页面出来。
			foreach (LayoutV1Block block in ordered)
			{
				ignored.Add(block.Id);
			}
			return false;
		}
		HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
		if (existing.Blocks != null)
		{
			foreach (BlockLayout block2 in existing.Blocks)
			{
				if (block2 != null && !string.IsNullOrEmpty(block2.Id))
				{
					known.Add(block2.Id);
				}
			}
		}
		List<LayoutV1Block> kept = new List<LayoutV1Block>();
		foreach (LayoutV1Block block3 in ordered)
		{
			if (known.Contains(block3.Id))
			{
				kept.Add(block3);
			}
			else if (!ignored.Contains(block3.Id))
			{
				ignored.Add(block3.Id);
			}
		}
		if (kept.Count == 0)
		{
			return false;
		}
		next.Layout.Pages[pageId] = LayoutStoreV2.MergeV1Order(existing, kept);
		return true;
	}

	// ————— 小工具 —————

	/// <summary>v1 画布文档的 items[] → 顺序 + 显隐(按数组顺序,即 v1 写入时的 z 序;类型白名单沿用冻结的 v1 契约)。</summary>
	private static List<LayoutV1Block> V1Items(object? doc)
	{
		List<LayoutV1Block> list = new List<LayoutV1Block>();
		JsonElement root;
		if (!TryRoot(doc, out root) || !LayoutJson.TryArray(root, "items", out JsonElement items))
		{
			return list;
		}
		foreach (JsonElement item in items.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			string type = PropText(item, "type", "");
			string id = PropText(item, "id", "");
			string blockId = (type.Length != 0) ? type : id;
			if (blockId.Length == 0 || !UiLayout.IsKnownCard(blockId))
			{
				continue;
			}
			list.Add(new LayoutV1Block
			{
				Id = blockId,
				Hidden = PropBool(item, "hidden", false)
			});
		}
		return list;
	}

	private static List<object?> BlockLines(PageLayoutV2 page)
	{
		List<object?> lines = new List<object?>();
		if (page.Blocks == null)
		{
			return lines;
		}
		foreach (BlockLayout block in page.Blocks)
		{
			if (block != null && !string.IsNullOrEmpty(block.Id))
			{
				lines.Add(block.Hidden ? (block.Id + "(hidden)") : block.Id);
			}
		}
		return lines;
	}

	/// <summary>
	/// 任意可 JSON 化的入参(PyDict / Dictionary / JsonElement)或者**一段 JSON 文本**(调用方从文件读来的,
	/// 与 <see cref="LayoutStoreV2.ParseImport"/> 的用法一致)→ JSON 文本,失败返回空串。
	/// </summary>
	private static string JsonOf(object? doc)
	{
		if (doc == null)
		{
			return "";
		}
		if (doc is JsonElement element)
		{
			return (element.ValueKind == JsonValueKind.Undefined) ? "" : element.GetRawText();
		}
		if (doc is string text2)
		{
			// 整份文档的文本:直接当 JSON 用(不是被引号包起来的一个字符串)
			string trimmed = text2.TrimStart();
			return trimmed.StartsWith("{", StringComparison.Ordinal) ? text2 : "";
		}
		try
		{
			return PyJson.Dumps(doc, 2, ensureAscii: false);
		}
		catch (Exception)
		{
			return "";
		}
	}

	private static bool TryRoot(object? doc, out JsonElement root)
	{
		root = default(JsonElement);
		string json = JsonOf(doc);
		if (json.Length == 0)
		{
			return false;
		}
		try
		{
			using JsonDocument parsed = JsonDocument.Parse(json);
			if (parsed.RootElement.ValueKind != JsonValueKind.Object)
			{
				return false;
			}
			root = parsed.RootElement.Clone();
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	// LayoutJson 的 Text/Bool 收的是"属性元素"而不是名字,这里补一对按名字取值的包装(只本文件用)
	private static JsonElement Prop(JsonElement obj, string name)
	{
		if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value))
		{
			return value;
		}
		return default(JsonElement);
	}

	private static string PropText(JsonElement obj, string name, string dflt)
	{
		return LayoutJson.Text(Prop(obj, name), dflt);
	}

	private static bool PropBool(JsonElement obj, string name, bool dflt)
	{
		return LayoutJson.Bool(Prop(obj, name), dflt);
	}
}
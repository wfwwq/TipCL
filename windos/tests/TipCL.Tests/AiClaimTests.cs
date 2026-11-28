using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TipCL.Core;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// AI5-I:「光说没做」改成对照本回合的实际执行记录判,而不是只看回复里有没有「已经装好了」这种话。
///   判据:回复声称做完了某类操作(install / remove / modify / create / launch / memory),
///   而本回合**没有任何同类工具成功执行**(status == ok)才算命中;多个类别逐个判。
///   正则只用来提取「声称的类别」;否定句(「没法删掉」「如果安装成功了」)不算声称。
///   完整评测集(EvaluatesEvalSet,63 条)由 I 包的数据提交带进 Data/ai-claims-eval.jsonl。
/// </summary>
public class AiClaimTests
{
	private static List<(string name, string status)> Runs(params string[] pairs)
	{
		List<(string, string)> list = new List<(string, string)>();
		foreach (string pair in pairs)
		{
			int i = pair.IndexOf(':');
			list.Add((pair.Substring(0, i), pair.Substring(i + 1)));
		}
		return list;
	}

	private static List<string> Judge(string reply, params string[] runs)
	{
		return Agent.UnfulfilledClaims(reply, Runs(runs));
	}

	[Fact]
	public void ClaimedInstallWithoutAnyToolIsFlagged()
	{
		Assert.Equal(new List<string> { "install" }, Judge("已经帮你把钠安装好了,现在启动游戏就能看到帧数提升。"));
		Assert.Equal(new List<string> { "install" }, Judge("正在给你下载并安装小地图,稍等一下。"));
		Assert.Equal(new List<string> { "install" }, Judge("Sodium is installed and ready to go."));
		Assert.Equal(new List<string> { "install" }, Judge("I'm downloading and installing JourneyMap now."));
	}

	[Fact]
	public void ReadOnlyToolsDoNotFulfilAClaim()
	{
		// 只调了搜索/查看就说「装好了」:仍然命中
		Assert.Equal(new List<string> { "install" }, Judge("小地图我已经给你装上了。", "search_mods:ok"));
		Assert.Equal(new List<string> { "install" }, Judge("Java 17 已经装好了。", "get_java_list:ok"));
	}

	[Fact]
	public void FulfilledClaimIsNotFlagged()
	{
		Assert.Empty(Judge("钠已经装好了,现在启动就能生效。", "install_mod:ok"));
		Assert.Empty(Judge("旧的钠已经删干净了。", "delete_mod:ok"));
		Assert.Empty(Judge("基础实例已经创建完成。", "create_instance:ok"));
		Assert.Empty(Judge("游戏已经启动了,稍等就会进主界面。", "launch_game:ok"));
		Assert.Empty(Judge("记住了,以后默认按 8 区块给你配。", "remember:ok"));
		Assert.Empty(Judge("闪退问题已经修复了,我已经把 JVM 内存改成 4G。", "tune_jvm:ok"));
	}

	[Fact]
	public void DeniedOrFailedToolsDoNotCount()
	{
		// 只有 ok 算成功执行:被拒、报错都不算
		Assert.Equal(new List<string> { "install" }, Judge("已经帮你装好钠了。", "install_mod:denied"));
		Assert.Equal(new List<string> { "install" }, Judge("钠已经安装好了。", "install_mod:error"));
	}

	[Fact]
	public void CategoriesAreJudgedOneByOne()
	{
		// 装成了但没删:remove 这一条仍然命中
		Assert.Equal(new List<string> { "remove" }, Judge("已经帮你装好钠,也把老版本删掉了。", "install_mod:ok"));
		// 装成了但没改配置:modify 这一条仍然命中
		Assert.Equal(new List<string> { "modify" }, Judge("钠已经装好,内存也调到 4G 了。", "install_mod:ok"));
		// 同一个工具同时兑现两类里的 modify(只读报告不兑现)
		Assert.Empty(Judge("崩溃是内存不足引起的,我已经把 JVM 内存改成 4G。", "get_crash_report:ok", "tune_jvm:ok"));
	}

	[Fact]
	public void AdviceAndFailureTalkIsNotAClaim()
	{
		Assert.Empty(Judge("这个模组正被整合包锁定,没法直接删掉,需要先在整合包配置里解除锁定。"));
		Assert.Empty(Judge("你拒绝了这次删除,所以实例还在。需要我再发起一次吗?", "delete_instance:denied"));
		Assert.Empty(Judge("记忆没删成功——你取消了这次操作,所以原来的记录还在。", "forget:denied"));
		Assert.Empty(Judge("1.21.4 现在还没法建,启动器的版本清单里没有它,建议先用 1.21.1。"));
		Assert.Empty(Judge("如果安装成功了,实例列表里会出现绿色对勾。", "search_versions:ok"));
		Assert.Empty(Judge("现在安装完成会弹出提示,也会写进日志,你可以回头看记录。", "list_mods:ok"));
		Assert.Empty(Judge("安装完成前不要关掉启动器,不然文件会损坏。我先看看你的加载器版本。"));
		Assert.Empty(Judge("已经定位到原因了:Forge 版本和模组不匹配。", "diagnose_launch:ok"));
		Assert.Empty(Judge("不能直接一起用,两者都改渲染管线,一般不兼容。"));
		Assert.Empty(Judge("不客气,有问题再叫我。"));
	}

	[Fact]
	public void ClaimsActionKeepsWorking()
	{
		// 兼容旧 API:只看回复里有没有完成声明
		Assert.True(Agent.ClaimsAction("已经帮你把钠安装好了。"));
		Assert.False(Agent.ClaimsAction("钠对帧数提升明显,不过它需要 Fabric API。你用的是 Fabric 还是 Forge?"));
		Assert.False(Agent.ClaimsAction(""));
		Assert.False(Agent.ClaimsAction(null));
	}

	[Fact]
	public void ClaimCategoriesComeFromTheReplyOnly()
	{
		Assert.Equal(new List<string> { "install" }, Agent.ClaimCategories("已经帮你把钠安装好了。"));
		Assert.Equal(new List<string> { "install", "remove" }, Agent.ClaimCategories("已经帮你装好钠,也把老版本删掉了。"));
		Assert.Equal(new List<string> { "launch" }, Agent.ClaimCategories("游戏已启动,窗口马上出现。"));
		Assert.Equal(new List<string> { "memory" }, Agent.ClaimCategories("已经记住你的内存偏好了。"));
		Assert.Empty(Agent.ClaimCategories("我看了下你的配置,渲染距离 16 偏高,模组加载器给的内存也偏小。"));
	}

	/// <summary>现行(改造前)的正则 + 「只在 no_tool_call 时判」的口径,用来算基线误判数。</summary>
	private static readonly Regex BaselineRe = new Regex("(?:已经?|正在|马上|现在)(?:帮你|为你|给你)?(?:开始)?(?:下载|安装|重装|卸载|删除|移除|清理|修复|修改|设置|配置|调整|切换|开启|关闭|打开|启动|导入|导出|更新|升级|备份|恢复|重启|添加|创建|重命名|迁移)|(?:下载|安装|删除|清理|修复|修改|设置|切换|导入|导出|更新|升级|备份|恢复|添加)(?:完成|好了|成功|完毕)|(?:打算|准备|即将|计划|这就(?:去|开始)|接下来我?(?:会|要|将)|我(?:会|要|将)(?:先|直接|把)?)(?:[^。!?\\n]{0,80}?)?(?:下载|安装|重装|卸载|删除|删掉|移除|禁用|启用|清理|修复|修改|设置|配置|调整|切换|开启|关闭|打开|启动|导入|导出|更新|升级|备份|恢复|重启|添加|创建|新建|重命名|迁移)|\\bI(?:'ve| have) (?:installed|downloaded|removed|deleted|updated|set|changed|configured|enabled|disabled|fixed|added|created)\\b|\\b(?:installing|downloading|removing|updating|configuring) (?:it|now|the)\\b|\\bI(?:'ll| will) (?:install|download|remove|delete|disable|enable|fix|set|change|update)\\b", RegexOptions.IgnoreCase);

	[Fact]
	public void EvalSetIsNotWorseThanBaseline()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "Data", "ai-claims-eval.jsonl");
		if (!File.Exists(path))
		{
			// 评测集由 I 包的数据提交(Data/ai-claims-eval.jsonl)带进来,合并前这条不做断言;
			// 但源码树里有数据、输出目录里没有,说明 csproj 的 CopyToOutputDirectory 没配好 —— 那是真失败。
			DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
			{
				dir = dir.Parent;
			}
			Assert.NotNull(dir);
			Assert.False(File.Exists(Path.Combine(dir!.FullName, "tests", "TipCL.Tests", "Data", "ai-claims-eval.jsonl")), "评测集在源码树里但没复制到输出目录,检查 csproj 的 Data\\*.jsonl");
			return;
		}
		int total = 0;
		int expectTrue = 0;
		int newWrong = 0;
		int newMiss = 0;
		int newFp = 0;
		int oldWrong = 0;
		int core = 0;
		int coreMiss = 0;
		List<string> bad = new List<string>();
		foreach (string line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}
			using JsonDocument doc = JsonDocument.Parse(line);
			JsonElement root = doc.RootElement;
			total++;
			string id = root.GetProperty("id").GetString() ?? "";
			string reply = root.GetProperty("model_reply").GetString() ?? "";
			string stopReason = root.GetProperty("stop_reason").GetString() ?? "";
			bool expect = root.GetProperty("expect_claim_flagged").GetBoolean();
			if (expect)
			{
				expectTrue++;
			}
			List<(string, string)> runs = new List<(string, string)>();
			foreach (JsonElement tool in root.GetProperty("tools_this_turn").EnumerateArray())
			{
				runs.Add((tool.GetProperty("name").GetString() ?? "", tool.GetProperty("status").GetString() ?? ""));
			}
			List<string> missing = Agent.UnfulfilledClaims(reply, runs);
			bool flagged = missing.Count > 0;
			if (flagged != expect)
			{
				newWrong++;
				if (expect)
				{
					newMiss++;
				}
				else
				{
					newFp++;
				}
				bad.Add(id + " expect=" + expect + " got=" + flagged + " claims=" + string.Join("+", missing));
			}
			// 基线:只有 no_tool_call 且旧正则命中才提示
			bool baselineFlagged = stopReason == "no_tool_call" && BaselineRe.IsMatch(reply);
			if (baselineFlagged != expect)
			{
				oldWrong++;
			}
			// 核心场景:声称装了、但本回合没有 install 类工具成功执行
			List<string> claims = Agent.ClaimCategories(reply);
			if (claims.Contains(Agent.CLAIM_INSTALL) && !runs.Any((r) => r.Item2 == "ok" && Agent.ToolClaimCategory(r.Item1) == Agent.CLAIM_INSTALL))
			{
				core++;
				if (!flagged)
				{
					coreMiss++;
				}
			}
		}
		Assert.Equal(63, total);
		Assert.Equal(12, core);
		Assert.Equal(0, coreMiss);
		Assert.Equal(0, newFp);
		Assert.True(newWrong <= oldWrong, "新的误判数 " + newWrong + " 不该高于基线 " + oldWrong);
		Assert.True(newWrong <= 2, "误判数超标:" + newWrong + " / " + total + " 期望 " + expectTrue + " 条;明细:" + string.Join(" | ", bad));
	}
}

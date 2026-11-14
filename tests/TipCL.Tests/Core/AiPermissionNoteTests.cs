// M4-H R21d:参考版 Permission.PermissionNote(Permission.cs:256-276)的对位 —— 「把当前权限档位
// 写成一句话告知模型」这一面。
//
// 参考版这句话是**独立的一条 system 消息**(身份 / 当前状态 / 权限设置 / 长期记忆,消费点
// Agent.cs:348 与 :368)。Linux 侧的系统消息是**合并在一条**里的(AiAgent.SystemMessage,既有结构
// 不推翻),所以那句话作为独立段落插在「运行时上下文之后、长期记忆之前」;崩溃修复会话(AiFixView)
// 自己拼系统消息,同一个位置。
//
// 六句断言一律**逐字节**比较(这一批句子里全是全角逗号/分号,抄错一个标点就是一条假绿或假红,
// 见 R19 的教训),而且必须另有负例 ——「任何一句非空中文都算过」这种断言等于没断言。
// 本轮只补「告知」这一面:裁决(Decide)/确认卡/拒绝理由(D-48)一行没动,见 Decide_IsUnchanged_ByTheNote。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPermissionNoteTests
{
    /// <summary>参考版 Permission.cs:256-276 的六句(逐字;由脚本从参考仓源码的字节直接注入)。</summary>
    private const string NoteYolo = "[权限设置] 用户选了「全自动」：写操作直接执行，不会弹确认。你仍要先用一句话说明将要做什么。";

    private const string NotePlan = "[权限设置] 用户选了「只看不动」：你只能查看和诊断，不能安装/删除/改配置。";

    private const string NoteAuto = "[权限设置] 写操作会直接执行；删除实例、删除模组前仍会先询问，要等用户点了才执行。";

    private const string NoteDontAsk = "[权限设置] 用户选了「不询问」：需要确认的写操作会被直接拒绝，只读查看照常。";

    private const string NoteEdit = "[权限设置] 文件类修改直接执行；安装、删除、启动前会弹确认，要等用户点了才执行。";

    private const string NoteDefault = "[权限设置] 安装、删除、禁用、改配置、启动这类写操作执行前都会弹确认，用户点了才会真的执行；查看类操作不弹。被拒绝时不要重复发起同一操作，改为说明原因。";

    /// <summary>「只看不动」档的拒绝理由(逐字取自 ToolRunner.DecideRaw,用来钉裁决没变)。</summary>
    private const string PlanDenyReason = "当前是「只看不动」模式，不能执行这类操作";

    private sealed class FakeApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static ToolRunner Runner(string mode) => new(new FakeApi()) { PermissionMode = mode };

    private static (string decision, string reason) Pair(ToolRunner.PermissionVerdict verdict)
        => (verdict.decision, verdict.reason);

    /// <summary>同时按住档位与「不询问」开关,跑完两个键一起还原(用例共享 Config.CONFIG 这份全局状态)。</summary>
    private static void WithMode(string mode, bool dontAsk, Action body)
    {
        object? beforeMode = Config.CONFIG.Get("ai_permission_mode");
        object? beforeAsk = Config.CONFIG.Get("ai_permission_dont_ask");
        try
        {
            Config.CONFIG.Set("ai_permission_mode", mode);
            Config.CONFIG.Set("ai_permission_dont_ask", dontAsk);
            body();
        }
        finally
        {
            Config.CONFIG.Set("ai_permission_mode", beforeMode ?? "default");
            Config.CONFIG.Set("ai_permission_dont_ask", beforeAsk ?? false);
        }
    }

    /// <summary>临时放一条长期记忆,跑完把记忆表整份还原(这样「那句话在记忆之前」才验得出来)。</summary>
    private static void WithMemory(string item, Action body)
    {
        List<string> before = AiMemory.All();
        try
        {
            AiMemory.Clear();
            AiMemory.Add(item);
            body();
        }
        finally
        {
            AiMemory.Clear();
            foreach (string line in before)
            {
                AiMemory.Add(line);
            }
        }
    }

    // ─────────────────── 1) 档位 → 句子(含别名与非法值,逐字节) ───────────────────

    [Fact]
    public void PermissionNote_EveryMode_MatchesTheReferenceVerbatim()
    {
        // 全自动:yolo 与它的别名
        Assert.Equal(NoteYolo, ToolRunner.PermissionNote("yolo"));
        Assert.Equal(NoteYolo, ToolRunner.PermissionNote("bypassPermissions"));
        // 只看不动
        Assert.Equal(NotePlan, ToolRunner.PermissionNote("plan"));
        // 写直接执行:acceptEdits 与参考版另外三个别名
        Assert.Equal(NoteAuto, ToolRunner.PermissionNote("auto"));
        Assert.Equal(NoteAuto, ToolRunner.PermissionNote("acceptEdits"));
        Assert.Equal(NoteAuto, ToolRunner.PermissionNote("autoEdit"));
        Assert.Equal(NoteAuto, ToolRunner.PermissionNote("build"));
        // 不询问
        Assert.Equal(NoteDontAsk, ToolRunner.PermissionNote("dontAsk"));
        // 文件类修改
        Assert.Equal(NoteEdit, ToolRunner.PermissionNote("edit"));
        // 默认:Linux 自己的 default / custom、空值、大小写不对的、以及认不出的档位名
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote("default"));
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote("custom"));
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote(""));
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote(null));
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote("Default"));
        Assert.Equal(NoteDefault, ToolRunner.PermissionNote("不了解的档位"));

        // 负例一:六句互不相同(复制粘贴写重了立刻红)
        var six = new[] { NoteYolo, NotePlan, NoteAuto, NoteDontAsk, NoteEdit, NoteDefault };
        Assert.Equal(6, six.Distinct(StringComparer.Ordinal).Count());
        // 负例二:每句都带「[权限设置] 」前缀,而且默认档拿不到「全自动」那句、反之亦然
        Assert.All(six, s => Assert.True(s.StartsWith("[权限设置] ", StringComparison.Ordinal), s));
        Assert.NotEqual(NoteYolo, ToolRunner.PermissionNote("default"));
        Assert.NotEqual(NoteDefault, ToolRunner.PermissionNote("yolo"));
        // 负例三:别名给的是「同一句」,不是各写一份相近的话(前面已逐字节比过,这里再钉死归属)
        Assert.NotEqual(NotePlan, ToolRunner.PermissionNote("acceptEdits"));
        Assert.NotEqual(NoteEdit, ToolRunner.PermissionNote("acceptEdits"));
    }

    /// <summary>
    /// 无参重载读的就是裁决读的那一份配置:ai_permission_mode 与裁决用的同一份归一化
    /// (ToolRunner.NormalizeMode),ai_permission_dont_ask 决定要不要换成「会被直接拒绝」那句。
    /// </summary>
    [Fact]
    public void PermissionNote_ReadsTheSameConfigKeysAsTheVerdict()
    {
        WithMode("yolo", false, () => Assert.Equal(NoteYolo, ToolRunner.PermissionNote()));
        WithMode("plan", false, () => Assert.Equal(NotePlan, ToolRunner.PermissionNote()));
        WithMode("acceptEdits", false, () => Assert.Equal(NoteAuto, ToolRunner.PermissionNote()));
        WithMode("default", false, () => Assert.Equal(NoteDefault, ToolRunner.PermissionNote()));
        WithMode("", false, () => Assert.Equal(NoteDefault, ToolRunner.PermissionNote()));
        // 「不询问」开关打开:句子换成「会被直接拒绝」那句(D-48 的语义)
        WithMode("default", true, () => Assert.Equal(NoteDontAsk, ToolRunner.PermissionNote()));
        WithMode("acceptEdits", true, () => Assert.Equal(NoteDontAsk, ToolRunner.PermissionNote()));
        // 但 yolo(全放行)与 plan(本来就拒)两档下这个开关根本不生效,句子不该跟着换
        WithMode("yolo", true, () => Assert.Equal(NoteYolo, ToolRunner.PermissionNote()));
        WithMode("plan", true, () => Assert.Equal(NotePlan, ToolRunner.PermissionNote()));
    }

    /// <summary>
    /// 那句话必须与**真实裁决**对得上:「不询问」= 需要确认的写操作直接拒绝(D-48),
    /// 既不是「任何档位都拒绝」,也不是「只是说说」。
    /// </summary>
    [Fact]
    public void DontAskSentence_MatchesTheActualDontAskSemantics()
    {
        WithMode("default", true, () =>
        {
            Assert.Equal(NoteDontAsk, ToolRunner.PermissionNote());
            // 同一档位、同一开关下的真实裁决:写操作 ask → deny(理由与参考版逐字相同)
            Assert.Equal(("deny", "用户开启了「不询问」模式"), Pair(Runner("default").Decide("install_mod")));
        });
        WithMode("default", false, () =>
        {
            // 对照:开关关着时句子是默认那句、裁决是 ask;只读查看照常 allow(句子后半句说的)
            Assert.Equal(NoteDefault, ToolRunner.PermissionNote());
            Assert.Equal("ask", Runner("default").Decide("install_mod").decision);
            Assert.Equal("allow", Runner("default").Decide("list_mods").decision);
        });
    }

    // ───────────── 2) 真的进了系统消息(运行时上下文之后、长期记忆之前) ─────────────

    [Fact]
    public void SystemMessage_CarriesTheNote_BetweenRuntimeContextAndMemory()
    {
        WithMemory("R21d 用例写进去的一条记忆", () =>
        {
            string content = Py.S(AiAgent.SystemMessage("demo", "1.20.1").Get("content"));
            string note = ToolRunner.PermissionNote();
            int runtimeAt = content.IndexOf(AiAgent.RuntimeContext("demo", "1.20.1"), StringComparison.Ordinal);
            int noteAt = content.IndexOf(note, StringComparison.Ordinal);
            int memoryAt = content.IndexOf("# 你之前记住的事", StringComparison.Ordinal);

            Assert.True(runtimeAt >= 0, "运行时上下文没进系统消息:\n" + content);
            Assert.True(noteAt > runtimeAt, "权限那句话必须在运行时上下文**之后**:\n" + content);
            Assert.True(memoryAt > noteAt, "权限那句话必须在长期记忆**之前**:\n" + content);
            // 只挂一次,别在拼接里重复
            Assert.Equal(noteAt, content.LastIndexOf(note, StringComparison.Ordinal));
            // 仍是既有那条 system 消息里的段落(Linux 侧合并成一条,不新增消息)
            Assert.Equal("system", Py.S(AiAgent.SystemMessage("demo", "1.20.1").Get("role")));
        });
    }

    [Fact]
    public void SystemMessage_NoteFollowsTheCurrentMode()
    {
        // 是「当前」档位,不是一句写死的常量
        WithMode("yolo", false, () =>
            Assert.True(Py.S(AiAgent.SystemMessage("demo").Get("content")).Contains(NoteYolo, StringComparison.Ordinal)));
        WithMode("default", false, () =>
        {
            string content = Py.S(AiAgent.SystemMessage("demo").Get("content"));
            Assert.True(content.Contains(NoteDefault, StringComparison.Ordinal));
            Assert.False(content.Contains(NoteYolo, StringComparison.Ordinal));
        });
    }

    /// <summary>崩溃修复会话那条路径(AiFixView 自己拼系统消息):同一个位置,一句话说明要做什么。</summary>
    [Fact]
    public void FixViewSystemMessage_CarriesTheNote_AtTheSamePosition()
    {
        var report = PyDict.Of("instance", "demo", "version", "1.20.1");
        string content = AiFixView.FixSystemMessage(report, "demo", "1.20.1");
        string note = ToolRunner.PermissionNote();
        int runtimeAt = content.IndexOf(AiAgent.RuntimeContext("demo", "1.20.1"), StringComparison.Ordinal);
        int noteAt = content.IndexOf(note, StringComparison.Ordinal);

        Assert.True(runtimeAt >= 0, "运行时上下文没进修复会话的系统消息:\n" + content);
        Assert.True(noteAt > runtimeAt, "修复会话:权限那句话也要在运行时上下文之后:\n" + content);
        // 修复会话自己的约定(「崩溃修复」)还在,没被这句话挤掉
        Assert.True(content.Contains("崩溃修复", StringComparison.Ordinal));
    }

    // ───────────── 3) 裁决一点没变(本轮不许动 D-48 那一套) ─────────────

    /// <summary>
    /// 同一档位下的裁决与理由仍是本轮改动前那一批(Core/AiPermissionReasonTests 的逐档断言原样保留,
    /// 这里再钉一条「加了那句话以后还是这些」;不改、也不放宽那边已有的断言)。
    /// </summary>
    [Fact]
    public void Decide_IsUnchanged_ByTheNote()
    {
        Assert.Equal(("ask", "写操作需要确认"), Pair(Runner("default").Decide("install_mod")));
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(Runner("default").Decide("delete_mod")));
        Assert.Equal(("ask", "高风险操作需要确认"), Pair(Runner("default").Decide("launch_game")));
        Assert.Equal(("allow", "只读操作"), Pair(Runner("default").Decide("list_mods")));
        Assert.Equal(("deny", PlanDenyReason), Pair(Runner("plan").Decide("install_mod")));
        Assert.Equal(("allow", "只读操作"), Pair(Runner("plan").Decide("list_mods")));
        Assert.Equal(("allow", "免确认模式"), Pair(Runner("yolo").Decide("delete_instance")));
        Assert.Equal(("allow", "写操作直接执行"), Pair(Runner("acceptEdits").Decide("install_mod")));
        Assert.Equal(("ask", "删除操作仍需确认"), Pair(Runner("acceptEdits").Decide("delete_mod")));
        Assert.Equal(("ask", "写操作需要确认"), Pair(Runner("custom").Decide("install_mod")));
    }
}

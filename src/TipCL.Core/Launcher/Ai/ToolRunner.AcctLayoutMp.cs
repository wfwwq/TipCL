// AI-acct-layout-mp:账号 / 皮肤 / 联机 / 排版 七个工具的执行体(ToolRunner 的 partial)。
// 来源: Windows 版参考仓库 windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs 的同名分支 + LayoutTools.Run。
// 走法与 ToolRunner 里其它工具一致:有页面门面的能力走 IApi(账号 = 设置页「账号」用的 get_account_rows /
// add_offline_account / set_active_account / set_account_skin;联机 = 联机页用的 terracotta_*),
// 纯 Core 的能力直接调 Core(布局 = LayoutStoreV2)。权限 / 确认 / 预览 / 检查点都在 RunAsync 与 AiAgent 里统一处理,
// 这里只负责「已经放行之后怎么做」。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>联机:等内核起来 / 等房间号时的轮询间隔(毫秒;测试可调小)。</summary>
    internal static int MultiplayerPollMs = MultiplayerTiming.DefaultPollMs;

    /// <summary>联机:已装未启动时,最多等内核起来多久(毫秒)。AI2-stability:&lt; 0 = 按配置(MultiplayerTiming.StartWaitMs,默认 15000);测试可直接赋值。</summary>
    internal static int MultiplayerStartWaitMs = -1;

    /// <summary>联机:开房后最多等房间号出现多久(毫秒;房间号要等房主在游戏里「对局域网开放」后才有)。AI2-stability:&lt; 0 = 按配置(默认 6000)。</summary>
    internal static int MultiplayerRoomWaitMs = -1;

    private static int StartWaitLimitMs() => MultiplayerStartWaitMs >= 0 ? MultiplayerStartWaitMs : MultiplayerTiming.StartWaitMs();

    private static int RoomWaitLimitMs() => MultiplayerRoomWaitMs >= 0 ? MultiplayerRoomWaitMs : MultiplayerTiming.RoomWaitMs();

    /// <summary>本包工具的分发(由 ExecuteAsync 的 default 分支转进来)。</summary>
    private async Task<object?> RunAcctLayoutMpAsync(string name, PyDict args, CancellationToken ct)
    {
        switch (name)
        {
            case "list_accounts":
                return AcctLayoutMpTools.SafeAccountRows(await AccountRowsAsync(ct).ConfigureAwait(false));

            case "add_offline_account":
            {
                string user = Py.Strip(Py.S(args.Get("username")));
                if (!AcctLayoutMpTools.ValidOfflineName(user))
                {
                    return "用户名只能是 3-16 位英文、数字或下划线";
                }
                // AddAccount 会按名字**替换**同名账号:同名的正版/外置账号会被离线账号顶掉(令牌一起丢),先拦住
                PyDict? same = FindRow(await AccountRowsAsync(ct).ConfigureAwait(false), user);
                if (same != null)
                {
                    string type = Py.S(Py.Or(same.Get("type"), "offline"));
                    return type == "offline"
                        ? "已经有离线账号 " + user + ",不用重复添加(要用它就调用 switch_account)"
                        : "已经有同名的" + TypeText(type) + "账号 " + user + ",为了不覆盖它,没有添加离线账号;换个名字再试";
                }
                await _api.CallAsync<object>("add_offline_account", new { username = user }, ct).ConfigureAwait(false);
                return "已添加离线账号 " + user + "(已设为当前账号)";
            }

            case "switch_account":
            {
                string want = Py.Strip(Py.S(args.Get("name")));
                List<object?> rows = await AccountRowsAsync(ct).ConfigureAwait(false);
                PyDict? hit = FindRow(rows, want);
                if (hit == null)
                {
                    List<PyDict> loose = rows.OfType<PyDict>()
                        .Where(r => string.Equals(Py.S(r.Get("name")), want, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    hit = loose.Count == 1 ? loose[0] : null;
                }
                if (hit == null)
                {
                    string names = string.Join("、", rows.OfType<PyDict>().Select(r => Py.S(r.Get("name"))).Where(n => n.Length > 0));
                    return "没有叫「" + want + "」的账号" + (names.Length > 0 ? ";现有账号:" + names : ";还没有任何账号,可以先 add_offline_account");
                }
                object? active = await _api.CallAsync<object>("set_active_account", new { name = Py.S(hit.Get("name")) }, ct).ConfigureAwait(false);
                return "当前账号:" + Py.S(active);
            }

            case "set_skin":
            {
                string path = AcctLayoutMpTools.SkinPathArg(args);
                if (path.Length == 0 || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
                {
                    return "找不到 png 皮肤文件:" + path;
                }
                List<object?> rows = await AccountRowsAsync(ct).ConfigureAwait(false);
                string account = Py.Strip(Py.S(args.Get("name")));
                PyDict? row = account.Length > 0
                    ? FindRow(rows, account)
                    : rows.OfType<PyDict>().FirstOrDefault(r => Py.Truthy(r.Get("active")));
                if (row == null)
                {
                    return account.Length > 0 ? "没有叫「" + account + "」的账号" : "还没有当前账号,先 add_offline_account 或 switch_account";
                }
                account = Py.S(row.Get("name"));
                string type = Py.S(Py.Or(row.Get("type"), "offline"));
                if (type != "offline")
                {
                    return account + " 是" + TypeText(type) + "账号:自定义皮肤只对离线账号有效,正版和皮肤站账号的皮肤要到各自的网站上改";
                }
                string model = AcctLayoutMpTools.SkinModelArg(args);
                await _api.CallAsync<object>("set_account_skin", new { name = account, path, model }, ct).ConfigureAwait(false);
                return "已给 " + account + " 换上皮肤(" + model + ")。离线账号进游戏即生效。";
            }

            case "multiplayer":
                return await MultiplayerAsync(args, ct).ConfigureAwait(false);

            case "layout_get":
            case "layout_apply":
                return LayoutTools.Run(name, args);

            default:
                return ToolErr(name, "unknown_tool", "没有这个工具: " + name, "");
        }
    }

    private async Task<List<object?>> AccountRowsAsync(CancellationToken ct)
        => await _api.CallAsync<List<object?>>("get_account_rows", null, ct).ConfigureAwait(false) ?? new List<object?>();

    private static PyDict? FindRow(IEnumerable<object?> rows, string name)
        => name.Length == 0 ? null : rows.OfType<PyDict>().FirstOrDefault(r => Py.S(r.Get("name")) == name);

    private static string TypeText(string type) => type switch
    {
        "microsoft" => "正版",
        "authlib" or "nide8" => "外置登录",
        _ => type,
    };

    // ————— 陶瓦联机(照 Windows ExtraTools.Multiplayer 的 action 语义) —————

    private async Task<PyDict> SnapshotAsync(CancellationToken ct)
        => Py.D(await _api.CallAsync<object>("terracotta_snapshot", null, ct).ConfigureAwait(false));

    private async Task<object?> MultiplayerAsync(PyDict args, CancellationToken ct)
    {
        string action = Py.Lower(Py.S(Py.Or(args.Get("action"), "status")).Trim());
        PyDict snap = await SnapshotAsync(ct).ConfigureAwait(false);
        if (action != "status" && !Py.Truthy(snap.Get("supported")))
        {
            return "当前系统不支持陶瓦联机";
        }
        if (action is "host" or "join")
        {
            if (!Py.Truthy(snap.Get("installed")))
            {
                await StartPrepareAsync(ct).ConfigureAwait(false);
                return "陶瓦联机组件还没装,已开始下载(看左侧「下载任务」),装好后再试一次。";
            }
            if (!Py.Truthy(snap.Get("running")))
            {
                // Linux 联机页是「打开本页才自动起内核」;AI 入口没有这一步,所以这里先起内核再继续
                await StartPrepareAsync(ct).ConfigureAwait(false);
                snap = await WaitSnapshotAsync(s => Py.Truthy(s.Get("running")), StartWaitLimitMs(), ct, MultiplayerTiming.WaitStart).ConfigureAwait(false);
                if (!Py.Truthy(snap.Get("running")))
                {
                    return "联机内核正在启动,几秒后再调用一次 multiplayer " + action + "。";
                }
            }
        }
        switch (action)
        {
            case "host":
            {
                await _api.CallAsync<object>("terracotta_host", null, ct).ConfigureAwait(false);
                PyDict after = await WaitSnapshotAsync(s => Py.S(s.Get("room")).Length > 0, RoomWaitLimitMs(), ct, MultiplayerTiming.WaitRoom).ConfigureAwait(false);
                string room = Py.S(after.Get("room"));
                if (room.Length > 0)
                {
                    return PyDict.Of("ok", true, "room", room, "state", Py.S(after.Get("label")),
                        "message", "房间已开好,房间号:" + room + "。把它发给朋友,让朋友在联机页或对我说「加入房间 " + room + "」。");
                }
                return PyDict.Of("ok", true, "room", "", "state", Py.S(after.Get("label")),
                    "message", "已开始开房:请先在游戏里打开单人世界,按 Esc →「对局域网开放」。几秒后用 multiplayer status 拿房间号发给朋友。");
            }
            case "join":
            {
                string room = Py.Strip(Py.S(args.Get("room")));
                if (room.Length == 0)
                {
                    return "要填朋友给的房间号";
                }
                await _api.CallAsync<object>("terracotta_join", new { room }, ct).ConfigureAwait(false);
                return "正在加入房间 " + room + "。连上后进游戏「多人游戏」,双击「陶瓦联机大厅」。";
            }
            case "stop":
                await _api.CallAsync<object>("terracotta_idle", null, ct).ConfigureAwait(false);
                return "已退出联机";
            case "status":
                return PyDict.Of(
                    "supported", Py.Truthy(snap.Get("supported")),
                    "installed", Py.Truthy(snap.Get("installed")),
                    "running", Py.Truthy(snap.Get("running")),
                    "state", Py.S(snap.Get("label")),
                    "room", Py.S(snap.Get("room")),
                    "error", Py.S(snap.Get("error")),
                    "game_running", Py.Truthy(snap.Get("game_running")));
            default:
                return "不认识的 action:" + action + "(只能是 status / host / join / stop)";
        }
    }

    /// <summary>起「准备陶瓦联机」后台任务(装组件 + 起内核),任务 id 交给宿主做任务卡。</summary>
    private async Task StartPrepareAsync(CancellationToken ct)
    {
        object? result = await _api.CallAsync<object>("terracotta_prepare", null, ct).ConfigureAwait(false);
        string taskId = result is string s ? s : Py.S(Py.Or(Py.D(result).Get("task_id"), Py.D(result).Get("id")));
        if (taskId.Length > 0)
        {
            TaskStarted?.Invoke(taskId, "准备陶瓦联机", "multiplayer");
        }
    }

    /// <summary>轮询联机快照直到条件满足或超时(返回最后一次快照)。AI2-stability:实际耗时记进日志(MultiplayerTiming.Record)。</summary>
    private async Task<PyDict> WaitSnapshotAsync(Func<PyDict, bool> done, int timeoutMs, CancellationToken ct, string what = "")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int polls = 0;
        PyDict snap = await SnapshotAsync(ct).ConfigureAwait(false);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        while (!done(snap) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(Math.Max(1, MultiplayerPollMs), ct).ConfigureAwait(false);
            snap = await SnapshotAsync(ct).ConfigureAwait(false);
            polls++;
        }
        MultiplayerTiming.Record(what.Length > 0 ? what : "wait", sw.ElapsedMilliseconds, timeoutMs, done(snap), polls);
        return snap;
    }
}

// AI2-stability:联机(multiplayer 工具)的等待时长 —— 具名常量 + 可配置 + 记录实际耗时。
//
// 「等内核起来最多 15 秒」「开房后等房间号最多 6 秒」原先是 ToolRunner 里两个写死的数,来源是估计(没在真网络 + 第二台
// 机器上量过,见 PARITY §10.4)。这里把它们收成具名常量,允许用 config.json 覆盖,并把每次等待的**实际耗时**写进日志
// 与一个小的环形记录(Recent),等有真机数据时可以据此调整默认值:
//   ai_multiplayer_start_wait_ms   等陶瓦内核进入 running(默认 15000)
//   ai_multiplayer_room_wait_ms    开房后等房间号出现(默认 6000)
// 取值夹在 [0, 120000];非法值回落默认。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher.Ai;

/// <summary>联机等待参数与实际耗时记录。</summary>
public static class MultiplayerTiming
{
    /// <summary>等联机内核起来的默认上限(毫秒;估计值:解压 + 拉起 + 连公共节点)。</summary>
    public const int DefaultStartWaitMs = 15000;

    /// <summary>开房后等房间号的默认上限(毫秒;估计值:房主还要在游戏里「对局域网开放」后才有房间号)。</summary>
    public const int DefaultRoomWaitMs = 6000;

    /// <summary>轮询联机快照的默认间隔(毫秒)。</summary>
    public const int DefaultPollMs = 500;

    /// <summary>可配置的上限(毫秒)。</summary>
    public const int MaxWaitMs = 120000;

    public const string StartWaitKey = "ai_multiplayer_start_wait_ms";

    public const string RoomWaitKey = "ai_multiplayer_room_wait_ms";

    /// <summary>等待种类:内核启动。</summary>
    public const string WaitStart = "start";

    /// <summary>等待种类:房间号。</summary>
    public const string WaitRoom = "room";

    /// <summary>一次等待的实测结果。</summary>
    public sealed record Wait(string What, long ElapsedMs, int LimitMs, bool Satisfied, int Polls, DateTime At);

    private const int Keep = 20;

    private static readonly ConcurrentQueue<Wait> RecentWaits = new();

    /// <summary>当前生效的「等内核起来」上限。</summary>
    public static int StartWaitMs() => Read(StartWaitKey, DefaultStartWaitMs);

    /// <summary>当前生效的「等房间号」上限。</summary>
    public static int RoomWaitMs() => Read(RoomWaitKey, DefaultRoomWaitMs);

    private static int Read(string key, int dflt)
    {
        try
        {
            object? raw = Config.CONFIG.Get(key);
            if (raw == null || raw is bool || (raw is string s && Py.Strip(s).Length == 0))
            {
                return dflt;
            }
            long v = Py.Int(raw);
            return v < 0 ? dflt : (int)Math.Min(MaxWaitMs, v);
        }
        catch (Exception)
        {
            return dflt;
        }
    }

    /// <summary>记一次等待:写日志 + 放进环形记录(最近 20 次)。</summary>
    public static Wait Record(string what, long elapsedMs, int limitMs, bool satisfied, int polls)
    {
        var w = new Wait(what, Math.Max(0, elapsedMs), limitMs, satisfied, polls, DateTime.Now);
        RecentWaits.Enqueue(w);
        while (RecentWaits.Count > Keep && RecentWaits.TryDequeue(out _))
        {
        }
        try
        {
            Utils.log.Info("multiplayer wait %s: elapsed %s ms / limit %s ms, satisfied=%s, polls=%s",
                what, w.ElapsedMs, limitMs, satisfied ? "true" : "false", polls);
        }
        catch (Exception)
        {
            // 日志写不进去不影响联机
        }
        return w;
    }

    /// <summary>最近的等待记录(旧 → 新)。</summary>
    public static List<Wait> Recent() => RecentWaits.ToList();
}

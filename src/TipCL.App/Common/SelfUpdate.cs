using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using TipCL.Core;

namespace TipCL.App.Common;

/// <summary>
/// 自更新任务的"发起 + 结果回报"小门面(M4-H R22)。
///
/// 为什么需要它:下载/校验失败发生在**后台任务**里(`Api.start_self_update`),而任务失败只体现在任务页上 ——
/// 从对话框或设置页发起更新的那条路上,用户**什么都看不到**(真机验证时实测:失败前后三张截图逐字节相同)。
/// 所以发起之后要盯着这个任务的 `finished` 事件,失败就把原因说出来。
///
/// 成功不在这里报:任务成功会 emit `update_staged`,由外壳弹「更新已下载 / 立即重启 / 稍后重启」。
/// </summary>
internal static class SelfUpdate
{
    /// <summary>发起自更新任务;失败时用 <paramref name="toast"/> 把原因告诉用户(成功保持安静)。</summary>
    public static async Task StartAsync(IApi api, Action<string, string, ToastKind> toast)
    {
        string id;
        try
        {
            id = await api.TryCallAsync<string>("start_self_update") ?? "";
        }
        catch (Exception ex)
        {
            toast("更新失败", ex.Message, ToastKind.Error);
            return;
        }
        if (id.Length == 0)
        {
            toast("更新失败", "没能启动更新任务", ToastKind.Warning);
            return;
        }

        var api2 = InProcessApi.Instance;
        Action<ApiEvent>? onEvent = null;
        onEvent = ev =>
        {
            if (ev.Event != "finished" || ev.TaskId != id) return;
            api2.BridgeEvent -= onEvent;   // 一次性的:这个任务已经有结论了
            if (ev.Success) return;        // 成功交给 update_staged 那条路
            string why = (ev.Message ?? "").Trim();
            string body = why.Length > 0 ? why : "下载或校验没有通过,可以到「下载任务」页看详情";
            Dispatcher.UIThread.Post(() => toast("更新失败", body, ToastKind.Error));
        };
        api2.BridgeEvent += onEvent;
    }
}

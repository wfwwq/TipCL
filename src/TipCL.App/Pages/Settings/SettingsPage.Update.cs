// 设置页「检查更新」(M4-H R22)。
//
// 照 Windows `SettingsPage.CheckUpdateAsync`(参考版 `SettingsPage.cs:552` 的按钮 + `:1812` 的进度提示):
// 手动查一次清单 → 已是最新就报一句、有新版就问要不要现在下载。
// **下载完成之后的事不在这里**:任务会把包下好并发 `update_staged` 事件,由外壳(`MainWindow`)问
// "立即重启还是稍后"并真的替换文件(见 `Updater.ApplyStaged` 的注释:Linux 是当场替换)。
using System;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    private async Task CheckUpdateAsync()
    {
        // 这里会真发一次 HTTP(最多 12 秒):CallAsync 内部跑在 Task.Run 上,所以 await 它不会卡界面。
        // (移植侧没有参考版的 Dlg.Busy,所以用 Toast 报结果。)
        PyDict? info = await Api.TryCallAsync<PyDict>("check_update");
        if (info == null)
        {
            Toast("检查更新失败", "拿不到更新清单,检查网络后再试", ToastKind.Warning);
            return;
        }
        string current = Py.S(info["current"]);
        string latest = Py.S(info["latest"]);
        if (!Py.Truthy(info["has_update"]))
        {
            // 注意这条分支里有两种情况:"确实是最新" 与 "清单说新版但 sha256 不合法被拒"(ok=false)。
            // 后者是有意的安全兜底,**不能**显示成"已是最新版本"——要把原因如实说出来。
            if (!Py.Truthy(info["ok"]))
            {
                Toast("更新被拒绝", Py.S(info["message"]), ToastKind.Warning);
                return;
            }
            Toast("已是最新版本", current + (latest.Length > 0 && latest != current ? " (清单:" + latest + ")" : ""), ToastKind.Success);
            return;
        }
        string body = ("TipCL " + latest + " 已发布。\n" + Py.S(info["notes"])).Trim();
        if (await Dlg.Confirm("发现新版本", body + "\n\n现在下载更新?下载完会提示你重启。", "下载更新", "以后再说"))
            await SelfUpdate.StartAsync(Api, Toast);   // 发起 + 盯失败(下载/校验失败要在界面上说出来)
    }
}

// AI2-i18n-ux:AI 页接执行器的「阶段进度」(ToolRunner.ToolProgress)。
// apply_instance_upgrade 里 install_game 是同步的,整段要跑很久;以前步骤行一直只有转圈,
// 现在每进一段(核对模组 / 复制实例 / 安装版本 / 替换模组 / 预检)就把「第 n/5 步:xx」写到该步骤行右侧的结果栏
// 和底部状态条上。共享的 AiPage.cs 只在执行器初始化里加了一行。
using TipCL.Core.Launcher.Ai;

namespace TipCL.App.Pages.Ai;

public sealed partial class AiPage
{
    /// <summary>
    /// 把阶段文字落到对应工具的步骤行(没有那一行时只更新状态条)。返回被更新的那一行(测试用)。
    /// 只在 UI 线程调(执行器回调已经 Post 过来)。
    /// </summary>
    internal ToolLine? ShowToolProgress(string tool, string stage)
    {
        _status.Text = stage;
        if (!_toolLines.TryGetValue(tool, out ToolLine? line))
        {
            return null;
        }
        line.SetState(ToolState.Running);
        line.SetResult(stage);
        _group?.Refresh();
        return line;
    }
}

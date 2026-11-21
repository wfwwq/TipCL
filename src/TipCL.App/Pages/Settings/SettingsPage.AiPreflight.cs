// AI-new-features:设置 → AI 助手 →「启动前 AI 预检」开关(默认开,直接写 config.json 的 ai_preflight_enabled)。
// 不进设置页的整页保存流:开关一拨即落盘,整页「保存」不带这个键,因此不会把它覆盖掉。
using Avalonia.Controls;
using Avalonia.VisualTree;
using TipCL.App.Ui;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    private CheckBox? _aiPreflight;

    private Control AiPreflightField()
    {
        _aiPreflight = UiClass.Switch(AiPreflight.IsEnabled(), (s, _) =>
        {
            if (s is CheckBox box)
            {
                AiPreflight.SetEnabled(box.IsChecked == true);
            }
        });
        // 回到设置页时按配置刷新(别处改过 config 也能看到真实状态)
        _aiPreflight.AttachedToVisualTree += (_, _) => _aiPreflight.IsChecked = AiPreflight.IsEnabled();
        return UiClass.Field(I18nCore.Tr("启动前 AI 预检"), _aiPreflight,
            I18nCore.Tr("点「启动游戏」时先检查 Java、前置模组、加载器、内存与磁盘;发现问题会显示提示条,可一键交给 AI 修复"));
    }
}

using System;
using Avalonia;

namespace TipCL.App.Ui;

/// <summary>
/// AI4-other:全局设计令牌(DECISIONS-UI 第 3 节 = launch_v3 规则 7)。**只由 AI4-other 包定义 / 修改**,
/// 其他包只引用,不改定义。
/// <list type="bullet">
/// <item>圆角只有两档:<see cref="RCtl"/> = 8(按钮、输入、标签)/ <see cref="RCard"/> = 12(卡片、组件、浮层)。
/// 资源键 <c>CR.Ctl</c> / <c>CR.Card</c>(Themes/Controls.axaml)。</item>
/// <item>字号只有五档:<see cref="FsCaption"/> 12(说明、标签、次要)/ <see cref="FsBody"/> 13(正文、按钮、输入)/
/// <see cref="FsTitle"/> 15(卡片标题、组件标题)/ <see cref="FsHeading"/> 21(页标题)/ <see cref="FsDisplay"/> 34(大数字、主标题)。
/// 资源键 <c>FS.Caption</c> / <c>FS.Body</c> / <c>FS.Title</c> / <c>FS.Heading</c> / <c>FS.Display</c>。</item>
/// </list>
/// 例外(与 v3 效果图自身一致,不算「第三档圆角」):由形状决定的全圆角(开关轨道、圆点、头像、数字角标、
/// 进度条、页签下划线等,半径 = 高 / 2 或 999),以及图标字形 / 首字母图标里的字号(属于图形,不是文字档)。
/// </summary>
public static class Tok
{
    // ===== 圆角 =====
    /// <summary>8:按钮、输入框、下拉框、标签 / 胶囊、列表行。</summary>
    public const double RCtl = 8;
    /// <summary>12:卡片、组件、浮层(弹出菜单、提示、对话框、Toast)。</summary>
    public const double RCard = 12;
    public static CornerRadius CrCtl => new(RCtl);
    public static CornerRadius CrCard => new(RCard);

    // ===== 字号 =====
    public const double FsCaption = 12;
    public const double FsBody = 13;
    public const double FsTitle = 15;
    public const double FsHeading = 21;
    public const double FsDisplay = 34;

    /// <summary>五档字号(升序)。</summary>
    public static readonly double[] FontSizes = { FsCaption, FsBody, FsTitle, FsHeading, FsDisplay };

    /// <summary>
    /// 把任意字号收拢到最近的一档:≤12.5 → 12;≤13.9 → 13;≤18 → 15;≤27 → 21;其余 → 34。
    /// (12.5 归 12:v2 / v3 里 12.5 都是说明 / 次要字;13.5 归 13:v2 的 13.5 正文在 v3 里是 13;
    /// 14~18 的小标题归 15。)非正数 / NaN 原样返回(交给 Avalonia 默认值)。
    /// </summary>
    public static double Font(double size)
    {
        if (double.IsNaN(size) || size <= 0) return size;
        if (size <= 12.5) return FsCaption;
        if (size < 14) return FsBody;
        if (size <= 18) return FsTitle;
        if (size <= 27) return FsHeading;
        return FsDisplay;
    }

    /// <summary>
    /// 把任意圆角收拢到两档:0 保持直角;≥ 999(药丸 / 全圆)保持;(0, 10) → 8;[10, 999) → 12。
    /// 10 归 12:现存的 10 全是卡片 / 气泡 / 浮层(AI 卡片、Toast、设置色块外框)。
    /// </summary>
    public static double Radius(double r)
    {
        if (double.IsNaN(r) || r <= 0) return 0;
        if (r >= 999) return r;
        return r < 10 ? RCtl : RCard;
    }

    public static CornerRadius Cr(double r) => new(Radius(r));
}

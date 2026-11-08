using System;
using System.Collections.Generic;
using Avalonia;
using UiFacade = TipCL.App.Ui.Ui;
using Avalonia.Media;

namespace TipCL.App.Common;

/// <summary>
/// M2 页面矢量图标库：把 Windows 版 Segoe MDL2 字形（Services/Ico.cs 语义）整理为
/// Geometry.Parse 兼容的 Path Data 常量。风格与外壳一致：16×16 视窗、线宽 1.4、圆头圆角。
/// 用法：&lt;Path Data="{x:Static common:IconLib.PlayData}"/&gt; 配 StrokeWidth 1.4 /
/// StrokeLineCap Round / StrokeJoinCap Round；星形等实心图标按需 Fill。
/// </summary>
public static class IconLib
{
    // ── 启动页 ──
    public const string PlayData = "M5,3 L12.5,8 L5,13 Z";
    public const string StopData = "M4.5,4.5 H11.5 V11.5 H4.5 Z";
    public const string RocketData = "M9.5,2.5 C11.5,3 13,4.5 13.5,6.5 L10,10 L6,6 Z M6,6 L3.5,7.5 L5,9 M10,10 L8.5,12.5 L7,11 M4.5,11.5 L2.5,13.5";
    public const string WifiData = "M2.5,6 A8,8 0 0 1 13.5,6 M4.5,8.5 A5,5 0 0 1 11.5,8.5 M6.5,11 A2,2 0 0 1 9.5,11 M8,13.2 L8.01,13.2";

    // ── 通用导航/操作 ──
    public const string GearData = "M8,5.5 A2.5,2.5 0 1 1 8,10.5 A2.5,2.5 0 0 1 8,5.5 Z M8,1.5 V3 M8,13 V14.5 M1.5,8 H3 M13,8 H14.5 M3.4,3.4 L4.5,4.5 M11.5,11.5 L12.6,12.6 M3.4,12.6 L4.5,11.5 M11.5,4.5 L12.6,3.4";
    public const string SearchData = "M7,2.5 A4.5,4.5 0 1 1 7,11.5 A4.5,4.5 0 0 1 7,2.5 Z M10.5,10.5 L14,14";
    public const string RefreshData = "M13,8 A5,5 0 1 1 11.3,4.2 M11,1.8 L11.6,4.6 L8.8,5.2";
    public const string AddData = "M8,3 V13 M3,8 H13";
    public const string CheckData = "M3.5,8.5 L6.5,11.5 L12.5,4.5";
    public const string CloseData = "M4,4 L12,12 M12,4 L4,12";
    public const string BackData = "M9.5,3 L4.5,8 L9.5,13";
    public const string ChevronDownData = "M4,6.5 L8,10.5 L12,6.5";
    public const string ChevronRightData = "M6.5,4 L10.5,8 L6.5,12";
    public const string ChevronLeftData = "M9.5,4 L5.5,8 L9.5,12";
    public const string MoreData = "M3,8 L3.01,8 M8,8 L8.01,8 M13,8 L13.01,8";
    public const string OpenExternalData = "M6,3.5 H2.5 V13.5 H12.5 V10 M9,2.5 H13.5 V7 M13.5,2.5 L7.5,8.5";
    public const string SortData = "M5,3 V13 M5,13 L3,10.8 M5,13 L7,10.8 M11,13 V3 M11,3 L9,5.2 M11,3 L13,5.2";
    public const string FilterData = "M2.5,3.5 H13.5 L9.5,8.5 V12.5 L6.5,14 V8.5 Z";

    // ── 实例库 ──
    public const string FolderData = "M2,4.5 A1,1 0 0 1 3,3.5 H6.2 L7.8,5.5 H13 A1,1 0 0 1 14,6.5 V12 A1,1 0 0 1 13,13 H3 A1,1 0 0 1 2,12 Z";
    public const string BoxData = "M8,2 L13.5,4.8 V11.2 L8,14 L2.5,11.2 V4.8 Z M2.5,4.8 L8,7.6 L13.5,4.8 M8,7.6 V14";
    public const string TrashData = "M3,4.5 H13 M6.5,4.5 V3 H9.5 V4.5 M4.5,4.5 L5,13 H11 L11.5,4.5 M6.8,7 V10.5 M9.2,7 V10.5";
    public const string StarData = "M8,2.5 L9.9,6.2 L14,6.8 L11,9.7 L11.7,13.8 L8,11.9 L4.3,13.8 L5,9.7 L2,6.8 L6.1,6.2 Z";
    public const string GridData = "M2.5,2.5 H7 V7 H2.5 Z M9,2.5 H13.5 V7 H9 Z M2.5,9 H7 V13.5 H2.5 Z M9,9 H13.5 V13.5 H9 Z";
    public const string ImportData = "M8,2.5 V9 M5.2,6.5 L8,9.3 L10.8,6.5 M2.5,11.5 V13.5 H13.5 V11.5";
    public const string ExportData = "M8,9.5 V2.5 M5.2,5.3 L8,2.5 L10.8,5.3 M2.5,11.5 V13.5 H13.5 V11.5";
    public const string DownloadData = "M8,2.5 V10 M4.8,7 L8,10.2 L11.2,7 M3,13.5 H13";

    // ── 插件 ──
    public const string PuzzleData = "M3,5.5 H5.2 A1.4,1.4 0 1 1 8.2,5.5 H13 V8.3 A1.4,1.4 0 1 0 13,11.3 V13 H3 Z";
    public const string LightningData = "M9,2 L4,9 H7.5 L6.5,14 L12,7 H8.5 Z";
    public const string ShieldData = "M8,2 L13,4 V8 C13,11 11,13 8,14 C5,13 3,11 3,8 V4 Z";

    // ── 设置 ──
    public const string PaletteData = "M8,2.5 A5.5,5.5 0 0 1 13.5,8 C13.5,9.5 12.5,10 11.5,10 H10 A1.2,1.2 0 0 0 9,11.5 A1.3,1.3 0 0 1 7.8,13.5 A5.5,5.5 0 1 1 8,2.5 Z M5.5,6.2 L5.51,6.2 M8,4.8 L8.01,4.8 M10.5,6.2 L10.51,6.2";
    public const string UserData = "M8,3 A2.5,2.5 0 1 1 8,8 A2.5,2.5 0 0 1 8,3 Z M3,13.5 C3,10.5 5,9 8,9 C11,9 13,10.5 13,13.5";
    public const string ServerData = "M2.5,3 H13.5 V7 H2.5 Z M2.5,9 H13.5 V13 H2.5 Z M4.5,5 L4.51,5 M4.5,11 L4.51,11";
    public const string BroomData = "M9,2.5 L13.5,7 M11.2,4.8 L6,10 M6,10 C4,10.5 3,12 2.5,13.5 C4.5,13.5 6.5,13 7.5,11.5";
    public const string EditData = "M3,13 L3.6,10.2 L11,2.8 A1.06,1.06 0 0 1 12.5,2.8 L13.2,3.5 A1.06,1.06 0 0 1 13.2,5 L5.8,12.4 Z";
    public const string CopyData = "M5.5,5.5 H13 V13 H5.5 Z M10.5,5.5 V3 H3 V10.5 H5.5";
    public const string SaveData = "M3,3 H11 L13,5 V13 H3 Z M5.5,3 V6.5 H10.5 V3 M5.5,13 V9.5 H10.5 V13";
    public const string MoonData = "M13,9.5 A5.5,5.5 0 1 1 6.5,3 A4.3,4.3 0 0 0 13,9.5 Z";
    public const string SunData = "M8,5 A3,3 0 1 1 8,11 A3,3 0 0 1 8,5 Z M8,1.5 V3 M8,13 V14.5 M1.5,8 H3 M13,8 H14.5 M3.4,3.4 L4.5,4.5 M11.5,11.5 L12.6,12.6 M3.4,12.6 L4.5,11.5 M11.5,4.5 L12.6,3.4";

    // ── 状态 ──
    public const string InfoData = "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M8,7 V11.5 M8,4.5 L8.01,4.5";
    public const string WarningData = "M8,2.5 L14,13 H2 Z M8,6.5 V9.5 M8,11.2 L8.01,11.2";
    public const string ErrorData = "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M5.5,5.5 L10.5,10.5 M10.5,5.5 L5.5,10.5";
    public const string SuccessData = "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M5.5,8.2 L7.5,10.2 L10.8,6.2";

    // ── 内容 ──
    public const string ClockData = "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M8,5 V8 L10.5,9.5";
    public const string ListData = "M5,4 H13.5 M5,8 H13.5 M5,12 H13.5 M2.5,4 L2.51,4 M2.5,8 L2.51,8 M2.5,12 L2.51,12";
    public const string WorldData = "M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 Z M2.5,8 H13.5 M8,2.5 C10.5,4.5 10.5,11.5 8,13.5 C5.5,11.5 5.5,4.5 8,2.5 Z";
    public const string ImageData = "M2.5,3.5 H13.5 V12.5 H2.5 Z M2.5,10.5 L6,7 L9,10 L11,8 L13.5,10.5 M5.5,6 L5.51,6";
    public const string HomeData = "M2.5,8 L8,3 L13.5,8 M4,7 V13.5 H12 V7";
    public const string ChatData = "M2.5,3 H13.5 V11 H8 L4.5,14 V11 H2.5 Z";
    public const string SendData = "M14,2 L2,7 L6.5,9 L8.5,13.5 Z M6.5,9 L14,2";
    public const string CloudData = "M5,12.5 A3,3 0 0 1 5.2,6.5 A4,4 0 0 1 12.9,7.6 A2.5,2.5 0 0 1 12.5,12.5 Z";

    /// <summary>语义键（对齐 Windows 版 Services/Ico.cs 命名）→ Path Data 的全量字典。</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["Play"] = PlayData,
        ["Stop"] = StopData,
        ["Rocket"] = RocketData,
        ["Wifi"] = WifiData,
        ["Gear"] = GearData,
        ["Search"] = SearchData,
        ["Refresh"] = RefreshData,
        ["Add"] = AddData,
        ["Check"] = CheckData,
        ["Close"] = CloseData,
        ["Back"] = BackData,
        ["Chevron"] = ChevronDownData,
        ["ChevronRight"] = ChevronRightData,
        ["ChevronLeft"] = ChevronLeftData,
        ["More"] = MoreData,
        ["Open"] = OpenExternalData,
        ["Sort"] = SortData,
        ["Filter"] = FilterData,
        ["Folder"] = FolderData,
        ["Box"] = BoxData,
        ["Package"] = BoxData,
        ["Trash"] = TrashData,
        ["Star"] = StarData,
        ["Grid"] = GridData,
        ["Import"] = ImportData,
        ["Export"] = ExportData,
        ["Download"] = DownloadData,
        ["Puzzle"] = PuzzleData,
        ["Lightning"] = LightningData,
        ["Shield"] = ShieldData,
        ["Palette"] = PaletteData,
        ["User"] = UserData,
        ["Server"] = ServerData,
        ["Broom"] = BroomData,
        ["Edit"] = EditData,
        ["Copy"] = CopyData,
        ["Save"] = SaveData,
        ["Moon"] = MoonData,
        ["Sun"] = SunData,
        ["Info"] = InfoData,
        ["Warning"] = WarningData,
        ["Error"] = ErrorData,
        ["Success"] = SuccessData,
        ["Clock"] = ClockData,
        ["List"] = ListData,
        ["World"] = WorldData,
        ["Image"] = ImageData,
        ["Home"] = HomeData,
        ["Chat"] = ChatData,
        ["Send"] = SendData,
        ["Cloud"] = CloudData,
    };

    /// <summary>按语义键取 Path Data，缺省回 Info 图标。</summary>
    public static string Of(string key)
        => All.TryGetValue(key, out var data) ? data : InfoData;

    /// <summary>构建一个 16×16 描边图标 Path（线宽 1.4 圆头，颜色走 DynamicResource 键）。</summary>
    public static Avalonia.Controls.Shapes.Path Make(string key, string colorKey = "B.Ink", double size = 16)
    {
        var p = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(Of(key)),
            Width = size,
            Height = size,
            Stretch = Avalonia.Media.Stretch.Uniform,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        p[!Avalonia.Controls.Shapes.Path.StrokeProperty] = Res.Bind(colorKey);
        return p;
    }

    /// <summary>校验全部 Path Data 可解析（测试用）；返回解析失败列表。</summary>
    public static List<string> ValidateAll()
    {
        var bad = new List<string>();
        foreach (var kv in All)
        {
            try
            {
                if (Geometry.Parse(kv.Value) == null) bad.Add(kv.Key);
            }
            catch (Exception)
            {
                bad.Add(kv.Key);
            }
        }
        return bad;
    }
}

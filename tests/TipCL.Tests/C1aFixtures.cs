using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TipCL.Tests;

/// <summary>
/// C1a(主题色板 + 图标字体 + Lucide 图标系统)的测试夹具。
///
/// 两条来源纪律(每条用例在注释里标明期望值属于哪一种):
/// - **Windows 源**:<c>D:\项目\tipcl\windos\tipcl\src\TipCL\Themes\Palette.Light|Dark.xaml</c>
///   与 <c>Resources\fonts\TipCLIcons.ttf</c> —— 本包开工时逐项对拍过,并把结果**钉死**在
///   <see cref="PinnedLight"/> / <see cref="PinnedDark"/> 里,所以用例不依赖那条路径;
///   路径在的时候另有一组交叉复核用例(路径不在就打印一行原因、直接返回,不判失败)。
/// - **本机实测**:Avalonia 11.3.23 headless + Skia 下的解析结果(色板取值、avares 字体族、
///   Geometry.Parse 的 bounds 等)。
///
/// 本文件不联网、不起进程;所有临时文件都会落在 <see cref="TestHome.Root"/> 之下(本包其实不写临时文件)。
/// </summary>
internal static class C1aFixtures
{
    /// <summary>Windows 版源码基准(只读;路径不在时相关用例只打印原因)。</summary>
    internal const string WindowsRoot = @"D:\项目\tipcl\windos\tipcl";

    /// <summary>色板键总数(7 个 C.* 颜色 + 54 个刷子)。</summary>
    internal const int PaletteKeyCount = 61;

    /// <summary>
    /// 起一次 headless Avalonia(<c>UseHeadlessDrawing = false</c> + Skia)。
    ///
    /// 为什么必须起:Avalonia 的 <c>Geometry.Parse</c> / 资源字典求值 / 字体管理都要
    /// <c>IPlatformRenderInterface</c>,不起平台会抛
    /// <c>InvalidOperationException: Unable to locate 'Avalonia.Platform.IPlatformRenderInterface'</c>
    /// (本机实测,A1 的 smoke 也是同一套配置)。
    ///
    /// **C1b-fix 起这里只做转发**:平台与 <c>Dispatcher.UIThread</c> 一律由
    /// <see cref="AvaloniaSession"/> 在自己的**专用后台线程**上起(那才是 Dispatcher 线程),
    /// 本方法只是 <see cref="AvaloniaSession.Ensure"/> 的别名,供 <c>Hosted.Show</c> 之类的老调用点继续用。
    ///
    /// 历史与原因(为什么不能在本线程直接 <c>SetupWithoutStarting()</c>):
    /// - 原先的写法把 <c>Dispatcher.UIThread</c> 绑在**第一个调用它的那个线程**上。xunit v2 走 VSTest
    ///   通道时每个测试类跑在自己的 worker 线程上,于是「哪个类先碰到 Avalonia 哪个类就独占 Dispatcher
    ///   线程」,其它类里构造控件必抛 <c>Call from invalid thread</c>(C1b 打回的真因,见 PROGRESS.md 的
    ///   C1b-fix 小节)。当时两条通道一条绿一条红、同一条通道还时绿时红,就是线程调度决定的。
    /// - 老写法还要把当前线程的 <c>SynchronizationContext</c> 换回来:Avalonia 起 Dispatcher 时会顺带
    ///   把当前线程的上下文换掉,而本进程的测试执行器是在主线程上同步阻塞等 <c>async</c> 用例的
    ///   (实测是 <c>Ported\WriteJsonTests.cs</c> 的并发写用例),留着 Avalonia 的上下文会让续体永久排队。
    ///   现在把平台整个挪到会话线程上,调用线程的 <c>SynchronizationContext</c> 一个字都不用动。
    ///
    /// **纪律**:<c>SetupWithoutStarting()</c> 在整个测试工程里只有 <c>AvaloniaSession</c> 一处调用。
    /// </summary>
    internal static void EnsureAvalonia()
    {
        AvaloniaSession.Ensure();
    }

    /// <summary>从测试输出目录往上找仓库根(找 <c>TipCL.Mac.sln</c>)。</summary>
    internal static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TipCL.Mac.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("往上找不到 TipCL.Mac.sln");
        }

        return dir.FullName;
    }

    /// <summary>仓库内某个文件的绝对路径(不存在就抛,用例里用它表示"文件必须存在")。</summary>
    internal static string RepoPath(params string[] relative)
    {
        string[] parts = new string[relative.Length + 1];
        parts[0] = RepoRoot();
        Array.Copy(relative, 0, parts, 1, relative.Length);
        string path = Path.Combine(parts);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("仓库里找不到文件: " + string.Join("/", relative), path);
        }

        return path;
    }

    /// <summary>Windows 基准仓里的文件路径;**那条路径不在时返回 null**(相关用例打印原因后直接返回)。</summary>
    internal static string? WindowsPathOrNull(params string[] relative)
    {
        if (!Directory.Exists(WindowsRoot))
        {
            return null;
        }

        string[] parts = new string[relative.Length + 1];
        parts[0] = WindowsRoot;
        Array.Copy(relative, 0, parts, 1, relative.Length);
        string path = Path.Combine(parts);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 把一份色板 axaml 解析成"规范化条目"列表(键序照文件顺序):
    /// <c>Color|键|#RRGGBB</c> / <c>Brush|键|#RRGGBB|不透明度</c> /
    /// <c>Gradient|键|#色@偏移|#色@偏移|#色@偏移</c>。
    /// 数字一律用不变文化 + "R" 格式,免得 <c>0.10</c> 和 <c>0.1</c> 判成不等。
    /// </summary>
    internal static List<string> PaletteEntries(string axamlPath)
    {
        XDocument doc = XDocument.Load(axamlPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var lines = new List<string>();
        foreach (XElement element in doc.Root!.Elements())
        {
            string key = element.Attribute(x + "Key")?.Value ?? "(no key)";
            switch (element.Name.LocalName)
            {
                case "Color":
                    lines.Add("Color|" + key + "|" + NormalizeHex(element.Value.Trim()));
                    break;
                case "SolidColorBrush":
                    lines.Add(
                        "Brush|" + key + "|" + NormalizeHex(element.Attribute("Color")?.Value ?? "?") +
                        "|" + NormalizeNumber(element.Attribute("Opacity")?.Value));
                    break;
                case "LinearGradientBrush":
                    var stops = new List<string>();
                    // 注意:axaml 的根节点带默认命名空间(https://github.com/avaloniaui),
                    // 子元素都在那个命名空间里,所以用 LocalName 过滤,不能写 Elements("GradientStop")。
                    foreach (XElement stop in element.Elements().Where(e => e.Name.LocalName == "GradientStop"))
                    {
                        stops.Add(NormalizeHex(stop.Attribute("Color")?.Value ?? "?") + "@" + NormalizeNumber(stop.Attribute("Offset")?.Value));
                    }

                    lines.Add("Gradient|" + key + "|" + string.Join("|", stops));
                    break;
                default:
                    lines.Add("?|" + key + "|" + element.Name.LocalName);
                    break;
            }
        }

        return lines;
    }

    /// <summary>色板 axaml 的键列表(文件顺序)。</summary>
    internal static List<string> PaletteKeys(string axamlPath)
    {
        return PaletteEntries(axamlPath).Select(line => line.Split('|')[1]).ToList();
    }

    /// <summary>不透明度 / 偏移的规范化写法。</summary>
    internal static string NormalizeNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "1";
        }

        return double.Parse(text, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>颜色十六进制统一成大写(值本身不动,只统一大小写)。</summary>
    internal static string NormalizeHex(string text)
    {
        return text.ToUpperInvariant();
    }

    /// <summary>把 Grading 条目拆成 (键, 类型, 值) —— 用例里按需取。</summary>
    internal static (string Key, string Kind, string[] Payload) Split(string line)
    {
        string[] parts = line.Split('|');
        return (parts[1], parts[0], parts.Skip(2).ToArray());
    }

    /// <summary>从 ttf 的 cmap 表里读出全部码位(fmt 4 与 fmt 12;与 Ported\IconFontTests 同一套算法)。</summary>
    internal static HashSet<int> FontCodepoints(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int U16(int o) => (b[o] << 8) | b[o + 1];
        uint U32(int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        int numTables = U16(4);
        int cmap = -1;
        for (int i = 0; i < numTables; i++)
        {
            int r = 12 + (16 * i);
            if (b[r] == 'c' && b[r + 1] == 'm' && b[r + 2] == 'a' && b[r + 3] == 'p')
            {
                cmap = (int)U32(r + 8);
            }
        }

        if (cmap < 0)
        {
            throw new InvalidDataException("ttf 里没有 cmap 表: " + path);
        }

        var set = new HashSet<int>();
        int n = U16(cmap + 2);
        for (int i = 0; i < n; i++)
        {
            int sub = cmap + (int)U32(cmap + 4 + (8 * i) + 4);
            int fmt = U16(sub);
            if (fmt == 4)
            {
                int segX2 = U16(sub + 6);
                int ends = sub + 14;
                int starts = ends + segX2 + 2;
                for (int s = 0; s < segX2 / 2; s++)
                {
                    for (int c = U16(starts + (2 * s)); c <= U16(ends + (2 * s)) && c != 0xFFFF; c++)
                    {
                        set.Add(c);
                    }
                }
            }
            else if (fmt == 12)
            {
                uint groups = U32(sub + 12);
                for (uint g = 0; g < groups; g++)
                {
                    int o = sub + 16 + (12 * (int)g);
                    for (uint c = U32(o); c <= U32(o + 4); c++)
                    {
                        set.Add((int)c);
                    }
                }
            }
        }

        return set;
    }

    /// <summary>ttf 的 name 表里某个 nameID 的字符串(nameID 1 = Family, 4 = Full name, 6 = PostScript)。</summary>
    internal static string? FontNameString(string path, int nameId)
    {
        byte[] b = File.ReadAllBytes(path);
        int U16(int o) => (b[o] << 8) | b[o + 1];
        uint U32(int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        int numTables = U16(4);
        int name = -1;
        for (int i = 0; i < numTables; i++)
        {
            int r = 12 + (16 * i);
            if (b[r] == 'n' && b[r + 1] == 'a' && b[r + 2] == 'm' && b[r + 3] == 'e')
            {
                name = (int)U32(r + 8);
            }
        }

        if (name < 0)
        {
            return null;
        }

        int count = U16(name + 2);
        int strings = name + U16(name + 4);
        for (int i = 0; i < count; i++)
        {
            int r = name + 6 + (12 * i);
            if (U16(r + 6) != nameId)
            {
                continue;
            }

            int platform = U16(r);
            int length = U16(r + 8);
            int offset = U16(r + 10);
            int start = strings + offset;
            return platform == 3
                ? System.Text.Encoding.BigEndianUnicode.GetString(b, start, length)
                : System.Text.Encoding.ASCII.GetString(b, start, length);
        }

        return null;
    }

    /// <summary>PNG 的 IHDR 宽高(只读前 33 字节,不解码整张图)。</summary>
    internal static (int Width, int Height) PngSize(byte[] png)
    {
        if (png.Length < 33 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
        {
            throw new InvalidDataException("不是 PNG");
        }

        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (width, height);
    }

    /// <summary>
    /// 把 <c>Palette.Keys</c>(C# 里的键表)与 <see cref="PinnedLight"/> 对齐;
    /// 用例用它对照 axaml 与代码两份键表,防止只改一处。
    /// </summary>
    internal static List<string> PinnedKeys(string[] pinned)
    {
        return pinned.Select(line => line.Split('|')[1]).ToList();
    }

    /// <summary>
    /// 浅色色板:逐项钉死 Windows 版 Themes\Palette.Light.xaml 的键名/键序/取值。
    /// 格式见 <see cref="PaletteEntries"/>;不透明度 1 表示没有 Opacity 属性(或缺省)。
    /// </summary>
    internal static readonly string[] PinnedLight =
    {
        "Color|C.Accent|#2E9B6B",
        "Color|C.AccentStrong|#1F7A52",
        "Color|C.AccentText|#1F7A52",
        "Color|C.AccentDeep|#1F7A52",
        "Color|C.AccentLite|#4FBE8B",
        "Color|C.Canvas|#F5F5F5",
        "Color|C.Paper|#FFFFFF",
        "Brush|B.Accent|#2E9B6B|1",
        "Brush|B.AccentStrong|#1F7A52|1",
        "Brush|B.OnAccentStrong|#FFFFFF|1",
        "Brush|B.AccentText|#1F7A52|1",
        "Brush|B.AccentTint|#F3F9F6|1",
        "Brush|B.AccentDeep|#1F7A52|1",
        "Brush|B.AccentLite|#4FBE8B|1",
        "Brush|B.AccentSoft|#E7F5EE|1",
        "Brush|B.AccentSoft2|#D7EEE2|1",
        "Brush|B.AccentBorder|#BFE3D2|1",
        "Brush|B.OnAccent|#FFFFFF|1",
        "Brush|B.Canvas|#F5F5F5|1",
        "Brush|B.Paper|#FFFFFF|1",
        "Brush|B.Paper2|#FAFAFA|1",
        "Brush|B.Side|#FAFAFA|1",
        "Brush|B.Chrome|#FFFFFF|1",
        "Brush|B.Line|#E6E6E6|1",
        "Brush|B.LineSoft|#F0F0F0|1",
        "Brush|B.Hover|#F2F2F2|1",
        "Brush|B.Ink|#262626|1",
        "Brush|B.InkSoft|#565656|1",
        "Brush|B.InkMuted|#666666|1",
        "Brush|B.InkFaint|#6B6B6B|1",
        "Brush|B.Danger|#D64545|1",
        "Brush|B.DangerSoft|#FCEBEA|1",
        "Brush|B.Warn|#D68A17|1",
        "Brush|B.WarnSoft|#FDF3E2|1",
        "Brush|B.Info|#2F72C4|1",
        "Brush|B.InfoSoft|#EAF2FC|1",
        "Brush|B.Ok|#2E9B6B|1",
        "Brush|B.OnOk|#000000|1",
        "Brush|B.Scroll|#CFCFCF|1",
        "Brush|B.ScrollHover|#B0B0B0|1",
        "Brush|B.Mask|#1A1A1A|0.34",
        "Brush|B.Glass|#F3F9F6|0.76",
        "Brush|B.GlassStrong|#F8FBF9|0.88",
        "Brush|B.GlassBorder|#BFDCCF|0.68",
        "Gradient|B.BannerFill|#237F59@0|#1F7A52@0.52|#185F40@1",
        "Brush|B.OnBanner|#FFFFFF|1",
        "Gradient|B.LogoFill|#25835C@0|#195F42@1",
        "Brush|B.OnLogo|#FFFFFF|1",
        "Brush|B.Card|#FFFFFF|1",
        "Brush|B.CardBorder|#00FFFFFF|1",
        "Brush|B.TitleBar|#FFFFFF|0.92",
        "Brush|B.TitleBarLine|#000000|0.06",
        "Brush|B.TitleSep|#000000|0.1",
        "Brush|B.CaptionInk|#555555|1",
        "Brush|B.CaptionHot|#C42B1C|1",
        "Brush|B.OnCaptionHot|#FFFFFF|1",
        "Brush|B.SegRail|#000000|0.05",
        "Brush|B.SegOn|#FFFFFF|1",
        "Brush|B.RowLine|#F2F2F2|1",
        "Brush|B.RowHover|#FAFAFA|1",
        "Gradient|B.PageWash|#EDEDED@0|#F7F7F7@0.6|#F3F3F3@1",
    };

    /// <summary>深色色板:逐项钉死 Windows 版 Themes\Palette.Dark.xaml(键名/键序与浅色完全相同)。</summary>
    internal static readonly string[] PinnedDark =
    {
        "Color|C.Accent|#3FB07C",
        "Color|C.AccentStrong|#267A56",
        "Color|C.AccentText|#79C8A3",
        "Color|C.AccentDeep|#79C8A3",
        "Color|C.AccentLite|#5FCB98",
        "Color|C.Canvas|#161616",
        "Color|C.Paper|#1E1E1E",
        "Brush|B.Accent|#3FB07C|1",
        "Brush|B.AccentStrong|#267A56|1",
        "Brush|B.OnAccentStrong|#FFFFFF|1",
        "Brush|B.AccentText|#79C8A3|1",
        "Brush|B.AccentTint|#1C2420|1",
        "Brush|B.AccentDeep|#79C8A3|1",
        "Brush|B.AccentLite|#5FCB98|1",
        "Brush|B.AccentSoft|#1B3A2C|1",
        "Brush|B.AccentSoft2|#204636|1",
        "Brush|B.AccentBorder|#2A5B45|1",
        "Brush|B.OnAccent|#FFFFFF|1",
        "Brush|B.Canvas|#161616|1",
        "Brush|B.Paper|#1E1E1E|1",
        "Brush|B.Paper2|#242424|1",
        "Brush|B.Side|#1A1A1A|1",
        "Brush|B.Chrome|#1A1A1A|1",
        "Brush|B.Line|#333333|1",
        "Brush|B.LineSoft|#282828|1",
        "Brush|B.Hover|#2A2A2A|1",
        "Brush|B.Ink|#EDEDED|1",
        "Brush|B.InkSoft|#C0C0C0|1",
        "Brush|B.InkMuted|#9A9A9A|1",
        "Brush|B.InkFaint|#8A8A8A|1",
        "Brush|B.Danger|#E8615F|1",
        "Brush|B.DangerSoft|#3A2321|1",
        "Brush|B.Warn|#E5A73C|1",
        "Brush|B.WarnSoft|#3A2F1C|1",
        "Brush|B.Info|#5A9CE8|1",
        "Brush|B.InfoSoft|#1E2A3A|1",
        "Brush|B.Ok|#3FB07C|1",
        "Brush|B.OnOk|#000000|1",
        "Brush|B.Scroll|#3F3F3F|1",
        "Brush|B.ScrollHover|#555555|1",
        "Brush|B.Mask|#000000|0.54",
        "Brush|B.Glass|#1C2420|0.72",
        "Brush|B.GlassStrong|#202824|0.86",
        "Brush|B.GlassBorder|#536B5E|0.62",
        "Gradient|B.BannerFill|#2A855E@0|#267A56@0.52|#1D6043@1",
        "Brush|B.OnBanner|#FFFFFF|1",
        "Gradient|B.LogoFill|#2A855E@0|#1D6043@1",
        "Brush|B.OnLogo|#FFFFFF|1",
        "Brush|B.Card|#232323|1",
        "Brush|B.CardBorder|#0FFFFFFF|1",
        "Brush|B.TitleBar|#1C1C1C|0.92",
        "Brush|B.TitleBarLine|#FFFFFF|0.07",
        "Brush|B.TitleSep|#FFFFFF|0.14",
        "Brush|B.CaptionInk|#C8C8C8|1",
        "Brush|B.CaptionHot|#C42B1C|1",
        "Brush|B.OnCaptionHot|#FFFFFF|1",
        "Brush|B.SegRail|#FFFFFF|0.06",
        "Brush|B.SegOn|#2E2E2E|1",
        "Brush|B.RowLine|#2C2C2C|1",
        "Brush|B.RowHover|#282828|1",
        "Gradient|B.PageWash|#161616@0|#1B1B1B@0.6|#181818@1",
    };

    /// <summary>
    /// 图标字体的码位抽查表:来源是 Windows 版 <c>Services\Ico.cs</c> 的常量(Named Glyph,全在
    /// U+E700..U+F5B0 私有区)。mac 版 Ico.cs 已按字节级复制进来,这张表用来钉住 ttf 的覆盖。
    /// </summary>
    internal static readonly ushort[] SampledIconCodepoints =
    {
        0xE768, // Ico.Play
        0xE71A, // Ico.Stop
        0xE7C1, // Ico.Rocket
        0xE80F, // Ico.Home
        0xE896, // Ico.Download
        0xE713, // Ico.Gear
        0xE77B, // Ico.User
        0xE99A, // Ico.Robot
        0xE8BD, // Ico.Chat
        0xE74D, // Ico.Trash
        0xE72C, // Ico.Refresh
        0xE721, // Ico.Search
        0xE710, // Ico.Add
        0xE73E, // Ico.Check
        0xE711, // Ico.Close
        0xE734, // Ico.Star
        0xE790, // Ico.Palette
        0xEC61, // Ico.Success
        0xEA86, // Ico.Puzzle
        0xEDE1, // Ico.Export
        0xF5B0, // Ico.Play2
        0xE8BB, // Ico.CloseWin
    };
}

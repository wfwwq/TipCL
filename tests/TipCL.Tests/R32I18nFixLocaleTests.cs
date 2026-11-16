// R32-I18NFIX(mac 树):布局 v2 面「缺键」守卫 + 词表对齐守卫。
//
// 背景:mac 的布局 v2 交付件全部走 `I18n.L(...)`,但 `src/TipCL.Core/Resources/locales/{zh_CN,en}.json`
// 是旧版子集(R32-I18N-ACCEPT 实测:mac 表比 lin/win 少 222 键,`mac − lin = 0`)。缺键**看不出来**:
// `I18n.L` 在 `Current == "zh_CN"` 时找不到就 `return zh`(中文原文),英文界面下于是**显示中文** ——
// 这条缺陷没有任何像素能证明,必须靠键集合断言钉住。
// 本包按 lead 的扩范围决定**一次补齐**:先补布局面 112 键,再按「与 Linux/Windows 拉齐」补 137 键
// (全部从 lin/win 词表原样拷贝,含 en 译文,不自译;唯一一条刻意不收的是 Windows 那侧的全角标点
// 变体「写入布局文件失败,重启后会丢失」,理由见下面 `ImportWriteFailureKey_...`)。
//
// 四条用例的「红法」(R32-D25:「测试全绿」不等于「有覆盖」):
//   1. `LayoutV2_AddedKeys_...`       —— 把 `AddedKeys` 里任意一条从 en/zh_CN 删掉 ⇒ 红;
//   2. `LayoutV2_LoadBearingKeys_...` —— 删掉/改掉关键键的译文 ⇒ 红(含 DEGRADE-FIX 点名的
//      「区块不可用」:R32-I18N-ACCEPT 确认 mac 表当时没有这条,走的是缺键回退);
//   3. `ImportWriteFailureKey_...`    —— 把 mac 源码的字面量改成全角(或把表里的键改成全角)⇒ 红;
//   4. `LocaleTables_AreAlignedAndOrphanFree` —— 删掉任意一条对齐键、或让 zh/en 出现孤儿 ⇒ 红。
//
// 不碰磁盘、不开窗口:只切 `I18n.Load(...)` 的当前语言(收尾还原),外加只读源码/夹具文本。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

public class R32I18nFixLocaleTests
{
    /// <summary>
    /// 本包补进 mac 两份词表的**布局 v2 面**键(键 = 中文原文;共 112 条)。
    /// 分账:103 条从 Linux 词表原样拷贝(含 en),9 条三树都没有、本次新造(见报告「补键清单」)。
    /// 与 Linux/Windows 拉齐的另外 137 条在 fixtures/r32-i18n-aligned-keys.json 里,由第 4 条用例覆盖。
    /// </summary>
    private static readonly string[] AddedKeys =
    {
        // ── BasicWidgets / BlockHost / DangerousWidgets ──
        "在布局 JSON 里给这个区块填 settings.text 即可写便签。",
        "已进入自定义布局",
        "按住区块拖动即可移动,完成编辑后自动保存",
        "网页地址不合法",
        "当前系统不能内嵌网页",
        "按钮动作不合法",
        "启动器还没准备好",
        "这个动作没能执行",
        // ── LayoutEditor ──
        "重做",
        "吸附到栅格",
        "全部折叠",
        "把所有区块收成标题栏,方便拖动排列(只在编辑时有效,不影响保存的布局)",
        "按住区块拖动移动 · 拖右/下边角缩放",
        "区块不可用",
        "重置失败",
        "本页已恢复默认布局",
        "全部展开",
        "内置区块不能移除",
        "没有可撤销的操作",
        "没有可重做的操作",
        "已重做",
        "没有可粘贴的区块",
        "按住区块拖动移动",
        "折叠(仅编辑时)",
        "展开",
        // ── LayoutJsonEditor ──
        "内容是空的",
        "内容太长(超过 {0} KB)",
        "不是合法的 JSON:{0}",
        "最外层必须是 JSON 对象",
        "缺少必填字段 kind(应为 {0})",
        "kind 必须是 {0}(当前 {1})",
        "缺少必填字段 version(应为 {0})",
        "version 必须是 {0}(当前 {1})",
        "未知字段:保留但不生效",
        "缺少 layout:应用后会得到一份空布局",
        "必须是对象:已按空处理",
        "必须是对象:该页已忽略",
        "必须是对象:该页分栏已忽略",
        "比例必须是数字:该处已忽略",
        "比例夹到 {0}..{1}(当前 {2})",
        "必须是对象:该自建区块已忽略",
        "未知类型 {0}:块会保留为「不可用」",
        "侧栏宽夹到 {0}..{1}(当前 {2})",
        "必须是数组:已回落到默认页签",
        "未知页签 {0}:已丢弃",
        "单页 version 必须是 {0}:该页已按默认处理",
        "未知模式 {0}:已按 stack 处理",
        "列数只认 2/3/4/6/12:已吸附到 {0}",
        "必须是对象:该块已忽略",
        "缺少块 id:该块已忽略",
        "块 id 重复({0}):重复的那块已忽略",
        "未知外观 {0}:已按 plain 处理",
        "只认 true / false:已按 false 处理",
        "取值不合法({0}):已回落到 {1}",
        "取值超出 {0}..{1}:已夹取(当前 {2})",
        // ── LayoutProfiles ──
        "方案名不能为空",
        "方案名太长(最多 {0} 个字)",
        "方案「{0}」已经存在",
        "上一次布局",
        // ── LayoutShellHooks ──
        "自定义布局...",
        "重置本页布局",
        "布局设置...",
        "这个页面没有可拖动的区块",
        "页签、密度、分栏比例可以在「设置 → 布局」里调整",
        // ── LayoutWidgetAdd ──
        "快捷启动",
        "时钟",
        "分隔线",
        "网页(危险)",
        "按钮(危险)",
        "布局文档不可用",
        "没有选择部件类型",
        "未知的部件类型:{0}",
        "{0}属于危险类型,需在布局 JSON 里显式开启",
        "自建区块 {0} · {1}",
        "目标页面布局不可用",
        "区块 id 为空",
        // ── PageLayoutIO ──
        "文件里没有可导入的页面",
        "已导入 {0} 个页面的布局",
        "写入布局文件失败,重启后会丢失",
        // ── PlaytimeWidget ──
        "全部实例累计",
        "读不到游戏时长数据。",
        "还没有游戏时长记录",
        // ── PluginBlocks ──
        "插件区块不可用",
        "提供它的插件被禁用或卸载了;可以在编辑布局里移除这个区块。",
        // ── ShortcutWidget ──
        "快捷启动没配置实例",
        "在布局 JSON 里给这个区块填 settings.instance(可选 settings.version)。",
        "快捷启动配置太长",
        "实例名 / 版本号都不应超过 128 个字符。",
        "稍等一下再点这个按钮。",
        // ── WidgetCommon ──
        "没有填写网页地址",
        "网页地址太长",
        "网页地址必须以 https:// 开头",
        "只允许 https 网页,禁止本地文件与脚本地址",
        "网页地址格式不对",
        "只允许 https 网页(http 与其它协议一律拒绝)",
        "网页地址缺少主机名",
        "没有填写按钮动作",
        "按钮动作要写成 类型:参数(例如 page:launch)",
        "不支持的按钮动作:{0}(只允许 page / launch / folder / rpc)",
        "按钮动作缺少参数",
        "只允许打开启动器数据目录(folder:data)",
        "这个 RPC 不在白名单里:{0}",
        "按钮动作缺少实例名",
        // ── WidgetUI ──
        "自建区块不可用",
        "布局文档里没有这个区块的定义。",
        "未知的区块类型:{0}",
        "布局文档里找不到 {0} 的定义;可以重新添加同名区块。",
        "已禁用的自建区块",
        "web / button 属于危险类型,默认关闭;要在设置里显式开启(填 settings.enabled = true)才会渲染。",
        "网页",
        "按钮",
        "自建区块",
    };

    /// <summary>承载关键路径的键 + 期望译文(逐字)。删掉/改掉其中任意一条 ⇒ 本用例红。</summary>
    private static readonly (string Key, string En)[] LoadBearing =
    {
        ("区块不可用", "Block unavailable"),
        ("写入布局文件失败,重启后会丢失", "Failed to write the layout file; changes will be lost after a restart"),
        ("页签、密度、分栏比例可以在「设置 → 布局」里调整", "Tabs, density and split ratios can be adjusted in Settings → Layout"),
        ("这个页面没有可拖动的区块", "This page has no draggable blocks"),
        ("自定义布局...", "Custom layout..."),
        ("重置本页布局", "Reset this page's layout"),
        ("布局设置...", "Layout settings..."),
        ("上一次布局", "Previous layout"),
        ("网页(危险)", "Web page (dangerous)"),
        ("按钮(危险)", "Button (dangerous)"),
        ("自建区块 {0} · {1}", "Custom block {0} · {1}"),
        ("布局文档不可用", "Layout document unavailable"),
        ("已进入自定义布局", "Entered custom layout"),
        ("全部折叠", "Collapse all"),
        ("按住区块拖动移动 · 拖右/下边角缩放", "Drag to move · drag the right or bottom edge to resize"),
    };

    private static bool HasCjk(string s)
        => s.Any(c => (c >= '\u2e80' && c <= '\u9fff') || (c >= '\uff00' && c <= '\uffef'));

    /// <summary>缺键在英文界面下就是「显示中文」:每条都必须在 en 表里真的翻出来(且不含 CJK)。</summary>
    [Fact]
    public void LayoutV2_AddedKeys_ResolveToRealEnglish_NotTheChineseKey()
    {
        string prev = I18n.Current;
        try
        {
            I18n.Load("en");
            var bad = new List<string>();
            foreach (string key in AddedKeys)
            {
                string got = I18n.L(key);
                // 缺键时 I18n.L 返回中文原文 ⇒ 这里就是我们守卫的那种"中英混排"
                if (got == key || HasCjk(got))
                {
                    bad.Add(key + " -> " + got);
                }
            }

            Assert.True(bad.Count == 0,
                "有 " + bad.Count + " 条布局 v2 文案在 en 下没翻出来(等于回显中文键):"
                + string.Join(" / ", bad));
        }
        finally
        {
            I18n.Load(prev);
        }
    }

    /// <summary>关键路径的译文逐字钉死(与上面那条互补:上面只证明"翻了",这条证明"翻对了")。</summary>
    [Fact]
    public void LayoutV2_LoadBearingKeys_HaveExactEnglish()
    {
        string prev = I18n.Current;
        try
        {
            I18n.Load("en");
            foreach ((string key, string expect) in LoadBearing)
            {
                Assert.Equal(expect, I18n.L(key));
            }
        }
        finally
        {
            I18n.Load(prev);
        }
    }

    /// <summary>
    /// 「写入布局文件失败…」:ACCEPT 登记的跨树标点不一致(Mac 半角 / Windows 全角)。
    /// 词表键与调用点必须是**同一个**键 —— 源码里的字面量与词表里的键逐字节相同,且都是半角逗号。
    /// </summary>
    [Fact]
    public void ImportWriteFailureKey_MatchesTheMacSourceLiteral()
    {
        const string HalfWidthKey = "写入布局文件失败,重启后会丢失";
        const string FullWidthKey = "写入布局文件失败，重启后会丢失";

        string src = File.ReadAllText(C1aFixtures.RepoPath("src", "TipCL.App", "Pages", "PageLayoutIO.cs"));
        Assert.Contains("I18n.L(\"" + HalfWidthKey + "\")", src, StringComparison.Ordinal);
        Assert.DoesNotContain(FullWidthKey, src, StringComparison.Ordinal);

        string prev = I18n.Current;
        try
        {
            I18n.Load("en");
            // 词表里认这个键(半角):能翻出来,而不是回显中文
            Assert.Equal("Failed to write the layout file; changes will be lost after a restart", I18n.L(HalfWidthKey));
        }
        finally
        {
            I18n.Load(prev);
        }
    }

    /// <summary>
    /// **键集合一致性(扩范围后的主守卫)**:mac 的 zh_CN / en 键集合必须完全相同(无孤儿),
    /// 且「与 Linux/Windows 对齐补进来的 137 键 + 布局面新造的 9 键」逐键都在两份表里。
    /// 去掉任意一条补键 ⇒ 红;把 Windows 那份全角标点变体混进来(或把半角那条删掉)也 ⇒ 红。
    /// 清单来自 fixtures/r32-i18n-aligned-keys.json(由 _i18nfix_tmp/tools/align_mac.py 生成,
    /// 源头写在该文件的 sourceLinux / sourceWindows 两行)。
    /// </summary>
    [Fact]
    public void LocaleTables_AreAlignedAndOrphanFree()
    {
        PyDict zh = PyJson.Loads(CoreResources.ReadText("locales/zh_CN.json")) as PyDict
                    ?? throw new InvalidOperationException("zh_CN.json 解析不出对象");
        PyDict en = PyJson.Loads(CoreResources.ReadText("locales/en.json")) as PyDict
                    ?? throw new InvalidOperationException("en.json 解析不出对象");

        // 1) 键数(与 CoreResourcesTests 同一事实的两处引用;去掉任意一条补键两面都红)
        Assert.Equal(3022, zh.Count);
        Assert.Equal(3022, en.Count);

        // 2) 无孤儿:两边键集合逐键相同
        List<string> onlyZh = zh.Keys.Where(k => !en.ContainsKey(k)).ToList();
        List<string> onlyEn = en.Keys.Where(k => !zh.ContainsKey(k)).ToList();
        Assert.True(onlyZh.Count == 0 && onlyEn.Count == 0,
            "zh/en 键集合不一致:只在 zh 的 " + onlyZh.Count + " 条、只在 en 的 " + onlyEn.Count + " 条");

        // 3) 对齐清单逐键都在(zh 与 en 都要有)
        using var doc = JsonDocument.Parse(File.ReadAllText(
            C1aFixtures.RepoPath("tests", "TipCL.Tests", "fixtures", "r32-i18n-aligned-keys.json")));
        List<string> aligned = ReadArray(doc, "alignedFromLinuxWindows");
        List<string> authored = ReadArray(doc, "layoutSurfaceAuthored");

        Assert.Equal(137, aligned.Count);   // 从 Linux/Windows 词表原样拷贝的部分
        Assert.Equal(9, authored.Count);    // 三树都没有、布局面新造的部分
        List<string> missing = aligned.Concat(authored)
            .Where(k => !zh.ContainsKey(k) || !en.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0,
            "有 " + missing.Count + " 条对齐键在 mac 词表里丢了:" + string.Join(" / ", missing));

        // 4) 唯一一条刻意不收的 Windows 侧键(全角标点变体)必须**不在**表里
        List<string> excluded = ReadArray(doc, "excludedWindowsOnly");
        string onlyExcluded = Assert.Single(excluded);
        Assert.False(zh.ContainsKey(onlyExcluded), "全角标点变体不该进 zh_CN:" + onlyExcluded);
        Assert.False(en.ContainsKey(onlyExcluded), "全角标点变体不该进 en:" + onlyExcluded);
    }

    private static List<string> ReadArray(JsonDocument doc, string name)
        => doc.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? "").ToList();
}

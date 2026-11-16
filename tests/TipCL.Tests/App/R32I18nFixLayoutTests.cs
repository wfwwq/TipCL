// R32-I18NFIX:布局 v2 面的「缺键」与「导入/导出对话框标题」两道守卫。
//
// 为什么需要这两条守卫(照 R32-D25 的口径 —— 「测试全绿」不等于「有覆盖」):
//   1. Linux 布局 v2 面的 15 个文案键从来不在 `zh_CN.json` / `en.json` 里。缺键**看不见**:
//      `I18nCore.Tr(key)` 找不到就 `return dflt ?? key`,而 key 就是中文原文 ⇒ 中文界面照旧、
//      英文界面**显示中文**。这类缺陷没有任何像素能证明(除非把语言切成 en),所以必须靠
//      「键集合」断言钉住:把补进去的键从前两份表里删掉任意一条,本文件立刻红。
//   2. `LayoutFx` 的两个文件对话框标题曾经是裸字面量 `"导出布局"` / `"导入布局"` —— 同一个串在
//      工具条**按钮**上走了 `I18nCore.Tr`,只有对话框标题漏了 i18n(对照 Windows 的
//      `Pages/PageLayoutIO.cs:25/:48` 是 `I18n.L(...)`)。把标题收回词表后,这里断言它在
//      `en` 下真的翻出来 —— 若把 `ExportDialogTitle()` 改回返回字面量,这条红。
//   3. `LayoutText.L` 原先是 `return key;` 的恒等假门面(`Core/Layout/PageLayoutIO.cs`),
//      被 v2 编辑器的导出/导入 Toast 与 `PageLayoutStore` 的错误消息直接调用。现已改走真表,
//      这里钉住「它确实查表」,防止再退回假门面。
//
// 不碰磁盘、不开窗口:只读内嵌词表 + 切 `I18nCore.CurrentLang`(纯字段),收尾必还原。
using System.Collections.Generic;
using System.Linq;
using TipCL.App.Pages.Layout;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Layout;
using Xunit;

namespace TipCL.Tests.App;

public class R32I18nFixLayoutTests
{
    /// <summary>
    /// 本次补进两份词表的 15 个布局 v2 面键(键 = 中文原文,值 = 期望的 en 译文)。
    /// 这份清单就是断言对象本身:任何一条从 zh_CN / en 里被删掉,整条用例变红。
    /// </summary>
    private static readonly (string Key, string En)[] Added =
    {
        ("按住区块拖动即可移动,完成编辑后自动保存", "Drag the block to move it; changes are saved automatically when you finish editing"),
        ("全部折叠", "Collapse all"),
        ("把所有区块收成标题栏,方便拖动排列(只在编辑时有效,不影响保存的布局)", "Collapse every block to its title bar for easier dragging (editing only; the saved layout is untouched)"),
        ("按住区块拖动移动 · 拖右/下边角缩放", "Drag to move · drag the right or bottom edge to resize"),
        ("重置失败", "Reset failed"),
        ("本页已恢复默认布局", "This page was restored to its default layout"),
        ("全部展开", "Expand all"),
        ("按住区块拖动移动", "Drag the block to move it"),
        ("折叠(仅编辑时)", "Collapse (editing only)"),
        ("展开", "Expand"),
        ("上一次布局", "Previous layout"),
        ("重置本页布局", "Reset this page's layout"),
        ("布局设置...", "Layout settings..."),
        ("这个页面没有可拖动的区块", "This page has no draggable blocks"),
        ("页签、密度、分栏比例可以在「设置 → 布局」里调整", "Tabs, density and split ratios can be adjusted in Settings → Layout"),
    };

    private static PyDict Table(string name)
        => PyJson.Loads(CoreResources.ReadText("locales/" + name)) as PyDict
           ?? throw new InvalidOperationException("内嵌词表 locales/" + name + " 解析不出对象");

    private static bool HasCjk(string s)
        => s.Any(c => (c >= '\u2e80' && c <= '\u9fff') || (c >= '\uff00' && c <= '\uffef'));

    /// <summary>缺键在英文界面下就是「显示中文」:两份表都有键 + en 真的翻过(且不含 CJK)。</summary>
    [Fact]
    public void LayoutV2_AddedKeys_ExistInBothTablesWithRealEnglish()
    {
        PyDict zh = Table("zh_CN.json");
        PyDict en = Table("en.json");

        var onlyZh = new List<string>();
        var onlyEn = new List<string>();
        var notTranslated = new List<string>();
        foreach ((string key, string expect) in Added)
        {
            if (!zh.ContainsKey(key)) onlyZh.Add(key);
            if (!en.ContainsKey(key)) onlyEn.Add(key);
            string got = I18nCore.Tr(key, "en");
            // Tr 缺键时 return key —— 只要 en 里没有这条,这里就等于中文原文
            if (got == key || HasCjk(got)) notTranslated.Add(key + " -> " + got);
            // 显式 lang 走的就是运行时那条 Lookup,顺带钉住译文本身
            Assert.Equal(expect, got);
        }

        Assert.True(onlyZh.Count == 0, "zh_CN.json 缺 " + onlyZh.Count + " 条:" + string.Join(" / ", onlyZh));
        Assert.True(onlyEn.Count == 0, "en.json 缺 " + onlyEn.Count + " 条:" + string.Join(" / ", onlyEn));
        Assert.True(notTranslated.Count == 0,
            "这些键在 en 下回落到中文(等于没翻):" + string.Join(" / ", notTranslated));
    }

    /// <summary>文件对话框标题必须查词表(此前是裸字面量,只有按钮走了 i18n)。</summary>
    [Fact]
    public void LayoutDialogTitles_ResolveThroughTheWordTable()
    {
        string prev = I18nCore.CurrentLang;
        try
        {
            I18nCore.CurrentLang = "en";
            Assert.Equal("Export layout", LayoutFx.ExportDialogTitle());
            Assert.Equal("Import layout", LayoutFx.ImportDialogTitle());

            I18nCore.CurrentLang = "zh_CN";
            Assert.Equal("导出布局", LayoutFx.ExportDialogTitle());
            Assert.Equal("导入布局", LayoutFx.ImportDialogTitle());
        }
        finally
        {
            I18nCore.CurrentLang = prev;
        }
    }

    /// <summary>`LayoutText.L` 不许再是 `return key;` 的恒等假门面。</summary>
    [Fact]
    public void LayoutText_Facade_QueriesTheWordTable()
    {
        string prev = I18nCore.CurrentLang;
        try
        {
            I18nCore.CurrentLang = "en";
            Assert.Equal("Exported", LayoutText.L("已导出"));
            Assert.Equal("Export failed", LayoutText.L("导出失败"));
            Assert.Equal("Invalid layout file", LayoutText.L("布局文件无效"));
            // 带参路径:先查表再 Py.Fmt,不是 string.Format(中文原文)
            Assert.Equal("Imported layouts for 3 pages", LayoutText.L("已导入 {0} 个页面的布局", 3));

            I18nCore.CurrentLang = "zh_CN";
            Assert.Equal("已导出", LayoutText.L("已导出"));
            Assert.Equal("已导入 3 个页面的布局", LayoutText.L("已导入 {0} 个页面的布局", 3));
        }
        finally
        {
            I18nCore.CurrentLang = prev;
        }
    }
}

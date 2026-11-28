// 自定义布局 v3(2026-11-20)Linux 侧词表守卫。
//
// 为什么要这一条:Linux 的 `I18nCore.Tr(key)` 缺键时 `return dflt ?? key`,key 就是中文原文 ⇒
// 中文界面照旧、英文界面**显示中文**,没有任何像素能证明(见 R32I18nFixLayoutTests 的同类说明)。
// v3 一次新增了约 180 条布局文案(代理 C 编辑态 / 代理 D 设置 → 布局,清单在工作区 tmp/v3_strings/{C,D}.txt,
// 由代理 E 统一入表)。这里扫源码里**每一个**带中文的 `I18nCore.Tr("…")` 字面量,要求它在内嵌的
// zh_CN.json 与 en.json 里都有键 —— 以后谁在布局面加文案忘了入表,这条当场红。
//
// 依赖源码树:找不到仓库根(GOAL.md + tools/i18n-audit.py)时安静返回,与 I18nBaselineTests 同口径。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.Layout;

public class LayoutV3I18nTests
{
    private static readonly Regex LiteralOfTr = new("I18nCore\\.Tr\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    private static readonly Regex Cjk = new("[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef]", RegexOptions.Compiled);

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs/dev/GOAL.md"))
                && File.Exists(Path.Combine(dir.FullName, "tools", "i18n-audit.py")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '0': sb.Append('\0'); break;
                case 'u' when i + 4 < s.Length:
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                    i += 4;
                    break;
                default: sb.Append(n); break;
            }
        }
        return sb.ToString();
    }

    private static PyDict Table(string name)
        => PyJson.Loads(CoreResources.ReadText("locales/" + name)) as PyDict
           ?? throw new InvalidOperationException("内嵌词表 locales/" + name + " 解析不出对象");

    private static IEnumerable<string> LayoutSources(string root)
    {
        string app = Path.Combine(root, "src", "TipCL.App");
        foreach (string f in Directory.EnumerateFiles(Path.Combine(app, "Pages", "Layout"), "*.cs", SearchOption.AllDirectories))
        {
            yield return f;
        }
        yield return Path.Combine(app, "Pages", "Settings", "SettingsPage.Layout.cs");
        yield return Path.Combine(app, "Controls", "PageLayoutHost.cs");
    }

    [Fact]
    public void EveryLayoutTrLiteral_HasKeysInBothTables()
    {
        string? root = RepoRoot();
        if (root is null)
        {
            return;
        }
        PyDict zh = Table("zh_CN.json");
        PyDict en = Table("en.json");
        var missing = new List<string>();
        int checkedCount = 0;
        foreach (string file in LayoutSources(root))
        {
            if (!File.Exists(file))
            {
                continue;
            }
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (Match m in LiteralOfTr.Matches(File.ReadAllText(file)))
            {
                string key = Unescape(m.Groups[1].Value);
                if (!Cjk.IsMatch(key))
                {
                    continue;
                }
                checkedCount++;
                if (!zh.ContainsKey(key) || !en.ContainsKey(key))
                {
                    missing.Add(rel + ": " + key + (zh.ContainsKey(key) ? "" : " [缺 zh_CN]") + (en.ContainsKey(key) ? "" : " [缺 en]"));
                }
            }
        }
        Assert.True(checkedCount > 100, "只扫到 " + checkedCount + " 条布局文案,源码路径可能变了");
        Assert.True(missing.Count == 0, "布局面有 " + missing.Count + " 条文案没入词表(英文界面会显示中文):\n" + string.Join("\n", missing.Distinct()));
    }

    /// <summary>v3 编辑态 / 设置 → 布局的几条关键文案在 en 下真的翻出来(不是回落到中文)。</summary>
    [Theory]
    [InlineData("导出本页…", "Export this page…")]
    [InlineData("导入到本页…", "Import into this page…")]
    [InlineData("导出整套布局…", "Export whole layout…")]
    [InlineData("拖动块调整顺序；网格模式下拖块的右边缘改宽度；悬停能看到块名和隐藏 / 删除按钮。", "Drag a block to reorder it; in grid mode drag its right edge to change the width; hover to see the block name and the hide / delete buttons.")]
    [InlineData("网页、按钮组件默认关闭，可在 设置 → 布局 → 高级 里开启。", "Web and button widgets are off by default; turn them on in Settings → Layout → Advanced.")]
    [InlineData("调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。", "Arrange navigation and page layouts, or save them as profiles. Changes apply instantly and can be reset at any time.")]
    public void V3KeyStrings_TranslateInEnglish(string key, string expect)
    {
        Assert.Equal(expect, I18nCore.Tr(key, "en"));
        Assert.Equal(key, I18nCore.Tr(key, "zh_CN"));
    }
}

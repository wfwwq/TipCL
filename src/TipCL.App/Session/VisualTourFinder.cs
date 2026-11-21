// AI3-visual:--visual-tour 的找控件逻辑(按 Name / 可见文字 / 提示 / 占位 / 类型)。
// i18n:ignore-file —— 开发者巡检工具,不含用户界面文案。
//
// 「层」= 一个 TopLevel:主窗口、它打开的浮层(PopupRoot,无头平台上每个 Popup / ContextMenu 都是独立 TopLevel)、
// 对话框(Owned Window)。找控件时**最上层优先**:对话框 → 浮层 → 主窗口,同一层里按可视树文档顺序。
// 命中的控件如果是更早命中的控件的子孙(按钮和它模板里的 TextBlock 同文字),只算一次。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace TipCL.App.Session;

internal static class TourFinder
{
    /// <summary>按选择器在若干层里找出全部命中(最上层优先,去掉「祖先已命中」的重复)。</summary>
    public static List<Control> FindAll(IEnumerable<Visual> layersTopFirst, TourSelector sel)
    {
        var hits = new List<Control>();
        if (sel == null || sel.IsEmpty) return hits;
        foreach (Visual layer in layersTopFirst)
        {
            if (layer == null) continue;
            var inLayer = new List<Control>();
            foreach (Visual v in layer.GetVisualDescendants().Prepend(layer))
            {
                if (v is not Control c) continue;
                if (!IsShown(c)) continue;
                if (!Matches(c, sel)) continue;
                if (inLayer.Any(h => h.IsVisualAncestorOf(c))) continue;
                // 覆盖层浮层既单列一层、又在宿主窗口的树里:上层已经命中过的不再重复计数
                if (hits.Any(h => ReferenceEquals(h, c) || h.IsVisualAncestorOf(c))) continue;
                inLayer.Add(c);
            }
            hits.AddRange(inLayer);
        }
        return hits;
    }

    /// <summary>取第 sel.Index 个命中;没有返回 null。</summary>
    public static Control? Find(IEnumerable<Visual> layersTopFirst, TourSelector sel)
    {
        List<Control> all = FindAll(layersTopFirst, sel);
        int i = sel.Index < 0 ? all.Count + sel.Index : sel.Index;
        return i >= 0 && i < all.Count ? all[i] : null;
    }

    /// <summary>控件此刻画得出来:有效可见、有尺寸、挂在某棵可视树上。</summary>
    public static bool IsShown(Control c)
        => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && c.GetVisualRoot() != null;

    /// <summary>单个控件是否满足选择器(各条件取「与」)。</summary>
    public static bool Matches(Control c, TourSelector sel)
    {
        if (sel.Name.Length > 0 && !string.Equals(c.Name, sel.Name, StringComparison.Ordinal)) return false;
        if (sel.Type.Length > 0 && !IsOfType(c, sel.Type)) return false;
        if (sel.Within.Length > 0 && !c.GetVisualAncestors().OfType<Control>().Any(a => string.Equals(a.Name, sel.Within, StringComparison.Ordinal))) return false;
        if (sel.Tip.Length > 0 && !string.Equals(TipOf(c), sel.Tip, StringComparison.Ordinal)) return false;
        if (sel.Watermark.Length > 0 && !(c is TextBox tb && string.Equals(tb.Watermark, sel.Watermark, StringComparison.Ordinal))) return false;
        if (sel.Text.Length > 0 || sel.Contains.Length > 0)
        {
            // 只给文字:控件自身的文字要对上;同时给了类型:该类型控件里任一段文字对上即可(「文字为 X 的按钮」)
            bool deep = sel.Type.Length > 0;
            if (!TextMatches(c, sel, deep)) return false;
        }
        return true;
    }

    private static bool TextMatches(Control c, TourSelector sel, bool deep)
    {
        if (Accept(OwnText(c), sel)) return true;
        if (!deep) return false;
        foreach (Visual d in c.GetVisualDescendants())
        {
            if (d is Control dc && Accept(OwnText(dc), sel)) return true;
        }
        return false;
    }

    private static bool Accept(string? text, TourSelector sel)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string t = Normalize(text);
        if (sel.Text.Length > 0 && !string.Equals(t, Normalize(sel.Text), StringComparison.Ordinal)) return false;
        if (sel.Contains.Length > 0 && t.IndexOf(Normalize(sel.Contains), StringComparison.Ordinal) < 0) return false;
        return true;
    }

    /// <summary>空白归一:首尾去空白、连续空白压成一个(换行也算)。</summary>
    internal static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>控件「自己」的文字:TextBlock 的 Text(或 Inlines 拼起来)、字符串 Content、MenuItem 的字符串 Header。</summary>
    public static string? OwnText(Control c)
    {
        switch (c)
        {
            case TextBlock tb:
                if (!string.IsNullOrEmpty(tb.Text)) return tb.Text;
                if (tb.Inlines is { Count: > 0 } inl) return InlineText(inl);
                return null;
            case HeaderedSelectingItemsControl hs when hs.Header is string h1:
                return h1;
            case HeaderedItemsControl hi when hi.Header is string h2:
                return h2;
            case HeaderedContentControl hc when hc.Header is string h3:
                return h3;
            case ContentControl cc when cc.Content is string s:
                return s;
        }
        return null;
    }

    private static string InlineText(InlineCollection inlines)
    {
        var sb = new StringBuilder();
        foreach (Inline i in inlines)
        {
            if (i is Run r) sb.Append(r.Text);
            else if (i is LineBreak) sb.Append(' ');
            else if (i is Span sp) sb.Append(InlineText(sp.Inlines));
        }
        return sb.ToString();
    }

    /// <summary>ToolTip 的文字(只认字符串提示;控件型提示取其中第一段文字)。</summary>
    public static string? TipOf(Control c)
    {
        object? tip = ToolTip.GetTip(c);
        return tip switch
        {
            null => null,
            string s => s,
            TextBlock tb => tb.Text,
            Control ctl => ctl.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
            _ => tip.ToString(),
        };
    }

    /// <summary>类型名匹配:自身或任一基类的短名 / 全名等于给定值。</summary>
    public static bool IsOfType(Control c, string typeName)
    {
        for (Type? t = c.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            if (string.Equals(t.Name, typeName, StringComparison.Ordinal) || string.Equals(t.FullName, typeName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>控件在它所在 TopLevel 坐标系里的中心点;算不出来(没挂树)返回 null。</summary>
    public static Point? CenterInTop(Control c)
    {
        if (TopLevel.GetTopLevel(c) is not TopLevel top) return null;
        return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top);
    }

    /// <summary>
    /// 要点的位置:先取中心;中心处命中测试落不到目标上(文字中心恰好是字间空隙、边上叠了别的元素)时,
    /// 在目标范围内按 5×3 网格找第一个「点得中」的点 —— 和人点在看得见的字上一样;都不行就回到中心。
    /// </summary>
    public static Point? ClickPoint(TopLevel top, Control c)
    {
        Point? center = CenterInTop(c);
        if (center is not Point pc) return null;
        if (HitCheck(top, c, pc).Ok) return pc;
        double w = c.Bounds.Width, h = c.Bounds.Height;
        foreach (double fy in new[] { 0.5, 0.3, 0.7 })
        {
            foreach (double fx in new[] { 0.5, 0.3, 0.7, 0.15, 0.85 })
            {
                Point? p = c.TranslatePoint(new Point(w * fx, h * fy), top);
                if (p is Point q && HitCheck(top, c, q).Ok) return q;
            }
        }
        return pc;
    }

    /// <summary>
    /// 命中测试:在 <paramref name="p"/> 处点下去,落到的元素是不是目标本身 / 它的子孙 / 它的祖先(按钮里的文字也算点中按钮)。
    /// 返回 (是否点中, 落点元素的类型名)。
    /// </summary>
    public static (bool Ok, string HitType) HitCheck(TopLevel top, Control target, Point p)
    {
        IInputElement? hit = top.InputHitTest(p);
        if (hit is not Visual hv) return (false, "(无)");
        bool ok = ReferenceEquals(hv, target) || target.IsVisualAncestorOf(hv) || hv.IsVisualAncestorOf(target);
        return (ok, hv.GetType().Name);
    }

    /// <summary>可视树里离 <paramref name="c"/> 最近的 ScrollViewer(先自身、再祖先)。</summary>
    public static ScrollViewer? NearestScrollViewer(Control c)
    {
        if (c is ScrollViewer self) return self;
        return c.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
    }

    /// <summary>某一层里「最大的、真的能滚的」ScrollViewer(内容高于视口);没有返回 null。</summary>
    public static ScrollViewer? LargestScrollable(Visual layer)
    {
        return layer.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(sv => sv.IsEffectivelyVisible && sv.Extent.Height > sv.Viewport.Height + 1)
            .OrderByDescending(sv => sv.Viewport.Width * sv.Viewport.Height)
            .FirstOrDefault();
    }

    /// <summary>一层里可见的文字 / 按钮清单(dump 用):每行「类型 文字 @x,y wxh」。</summary>
    public static List<string> Describe(Visual layer)
    {
        var lines = new List<string>();
        TopLevel? top = layer as TopLevel ?? TopLevel.GetTopLevel(layer);
        foreach (Visual v in layer.GetVisualDescendants())
        {
            if (v is not Control c || !IsShown(c)) continue;
            string? text = OwnText(c);
            string? tip = TipOf(c);
            bool interesting = text != null || tip != null || !string.IsNullOrEmpty(c.Name)
                || c is Button || c is TextBox || c is ComboBox || c is ScrollViewer;
            if (!interesting) continue;
            Point? at = top == null ? null : c.TranslatePoint(new Point(0, 0), top);
            string pos = at is Point p ? $"@{p.X:F0},{p.Y:F0} {c.Bounds.Width:F0}x{c.Bounds.Height:F0}" : "@?";
            var sb = new StringBuilder();
            sb.Append(c.GetType().Name);
            if (!string.IsNullOrEmpty(c.Name)) sb.Append(" #").Append(c.Name);
            if (text != null) sb.Append(" \"").Append(Normalize(text)).Append('"');
            if (tip != null) sb.Append(" tip=\"").Append(Normalize(tip)).Append('"');
            if (c is TextBox tb && !string.IsNullOrEmpty(tb.Watermark)) sb.Append(" watermark=\"").Append(tb.Watermark).Append('"');
            if (c is ScrollViewer sv) sb.Append($" scroll={sv.Offset.Y:F0}/{sv.Extent.Height:F0} vp={sv.Viewport.Height:F0}");
            sb.Append(' ').Append(pos);
            lines.Add(sb.ToString());
        }
        return lines;
    }
}

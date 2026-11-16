// R32-LINMORE:设置页内层子视图的「按页布局」接线(把传统控件拼装的子视图变成布局文档里的可编辑页)。
// 起因:R32-ROUND2-SPEC §2.1 表 1 的第 4/5/6/7/10 行(servers / java / playtime / feedback / settings:about)
// 在 Linux 一直是 SettingsSubView(标题 + ScrollViewer),进不了编辑态 —— 设置页「布局」视图的
// 「页面区块」入口也就没有这五页可跳。本文件只加**一个**保护方法,四个子类各自在自己的构造函数末尾调它。
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using TipCL.App.Pages.Layout;
using TipCL.Core.Launcher.Layout;

namespace TipCL.App.Pages.Settings;

internal abstract partial class SettingsSubView
{
    /// <summary>
    /// [R32-LINMORE] 把本子视图交给区块宿主:调用后这一页成为布局文档里的一页
    /// (<paramref name="pageId"/> 进 ui_layout_v2.json),于是它能进编辑态,也能被设置页
    /// 「布局 → 页面区块」当跳转目标、「布局 → 自建区块」当加块目标。
    /// <para>首块就是本页的标题行(基类的 <see cref="Header"/>)—— 与 Windows 逐字一致:
    /// ServersPage / PlaytimePage 的首块(header)与 JavaPage 的首块(intro)传的都是同一个
    /// 标题 Section 控件,不是另外单造的说明卡(R32-ROUND2-SPEC §2.1-5 建议的方案 A 与之不符)。</para>
    /// <para><paramref name="headerBlockId"/> 必须取 Windows 那一页的首块 id(java 用 "intro",
    /// servers / playtime 用 "header"),否则 Linux 导出的布局文件与 Windows/Mac 认不上。</para>
    /// <para>pageId / 块 id 都对齐 Windows 冻结件;contentMargin 传旧 ScrollViewer 的 18,6,18,16
    /// ⇒ 默认(非编辑态)渲染与改动前逐像素一致。</para>
    /// </summary>
    protected BlockHost UseBlockHost(string pageId, string headerBlockId, string headerTitle,
        params (string Id, string Title, Control Content)[] blocks)
    {
        // 先把 _stack 里的子件全摘下来:它们随即要作为区块内容重新挂到宿主下,
        // Avalonia 不允许同一控件有两个父级(R32-MFIN 已踩过)。
        _stack.Children.Clear();
        List<BlockSpec> specs = new()
        {
            new BlockSpec { Id = headerBlockId, Title = headerTitle, Content = Header },
        };
        foreach ((string id, string title, Control content) in blocks)
        {
            specs.Add(new BlockSpec { Id = id, Title = title, Content = content });
        }
        // 出厂默认布局:_stack 原来是 Spacing 10 + ScrollViewer 边距 18,6,18,16 ⇒ 同值搬进文档
        PageLayoutV2 page = new()
        {
            Mode = LayoutSchema.ModeStack,
            Gap = LayoutSchema.GapDefault,
            Pad = new double[] { 18.0, 6.0, 18.0, 16.0 },
        };
        for (int i = 0; i < specs.Count; i++)
        {
            page.Blocks.Add(new BlockLayout { Id = specs[i].Id, Span = 1, Z = i });
        }
        var host = new BlockHost(pageId, specs, new Thickness(18, 6, 18, 16), page);
        Content = host;
        return host;
    }
}
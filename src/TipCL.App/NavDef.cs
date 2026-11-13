using System;
using TipCL.Pages;

namespace TipCL;

/// <summary>
/// 页面定义:页面 id + 标题 + 图标码位 + 建页工厂。
///
/// 与 Windows 版 <c>NavDef.cs</c> 的**唯一差异**:<c>Create</c> 允许为 null。
/// 原因:mac 版到 C2b 为止还没有任何页面(C3 才逐页移植),工厂要由各页面包用
/// <see cref="PageRegistry.Register"/> 注册;Windows 版是直接 <c>() =&gt; new XxxPage()</c>,
/// 在 mac 上那会把页面类型写死在导航表里。见 PROGRESS.md 的 C2b 差异表与 D-92。
/// 其余成员名(<c>Id</c> / <c>Title</c> / <c>Glyph</c> / <c>Create</c>)与顺序一字不改。
/// </summary>
public sealed record NavDef(string Id, string Title, string Glyph, Func<PageBase>? Create);

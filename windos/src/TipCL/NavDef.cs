using System;
using TipCL.Pages;

namespace TipCL;

public sealed record NavDef(string Id, string Title, string Glyph, Func<PageBase> Create);

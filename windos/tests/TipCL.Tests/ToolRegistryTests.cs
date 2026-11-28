using System.Collections.Generic;
using System.Linq;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests;

/// <summary>工具表的一致性：每个工具都有权限元数据、名字不重复、常驻工具都存在。</summary>
public class ToolRegistryTests
{
	private static List<string> Names => Tools.TOOL_SCHEMAS.Select(Tools.SchemaName).ToList();

	[Fact]
	public void NamesAreUnique()
	{
		List<string> dup = Names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
		Assert.Empty(dup);
	}

	[Fact]
	public void EveryToolHasMeta()
	{
		Assert.All(Names, n => Assert.True(ToolMeta.Get(n) != null, "缺少 ToolMeta: " + n));
	}

	[Fact]
	public void CoreToolsExist()
	{
		Assert.All(AiData.CORE_TOOLS, n => Assert.Contains(n, Names));
	}

	[Fact]
	public void KeywordToolsExist()
	{
		Assert.All(AiData.KEYWORD_TOOLS.SelectMany(k => k.extra), n => Assert.Contains(n, Names));
	}

	[Fact]
	public void SchemasHaveObjectParameters()
	{
		foreach (object s in Tools.TOOL_SCHEMAS)
		{
			TipCL.Core.PyDict fn = (TipCL.Core.PyDict)((TipCL.Core.PyDict)s)["function"];
			TipCL.Core.PyDict p = (TipCL.Core.PyDict)fn["parameters"];
			Assert.Equal("object", p["type"]);
			Assert.False(string.IsNullOrWhiteSpace((string)fn["description"]), (string)fn["name"]);
		}
	}

	/// <summary>
	/// B2(2026-11-28):remember / forget 真的会写盘(记忆文件),元信息必须与真实副作用一致 ——
	/// 它们是 write_local 而不是只读。MCP Server 判断「外部调用要不要确认」也是看这一位
	/// (<c>Tools.IsWriteTool</c>),标错等于给外部 agent 开了一条不确认的写路径。
	/// </summary>
	[Fact]
	public void MemoryAndWriteFlags()
	{
		Assert.True(Tools.IsWriteTool("remember"));
		Assert.True(Tools.IsWriteTool("forget"));
		Assert.False(Tools.IsWriteTool("list_mods"));
		Assert.True(Tools.IsWriteTool("delete_mod"));
		Assert.True(Tools.IsWriteTool("restore_save_backup"));
	}

	/// <summary>B2:两个记忆工具的副作用标成 write_local / risk low,与真实行为一致。</summary>
	[Fact]
	public void MemoryToolsDeclareWriteLocal()
	{
		foreach (string name in new[] { "remember", "forget" })
		{
			ToolMetaInfo m = ToolMeta.Get(name);
			Assert.NotNull(m);
			Assert.False(m!.@readonly);
			Assert.Equal("write_local", m.side_effect);
			Assert.Equal("low", m.risk);
		}
	}
}

// M4-H R5:权限确认卡的「改前改后」预览(Core `Launcher/Ai/Preview.cs` 的纯逻辑用例)。
//
// 覆盖四类真实场景:新建 / 追加 / 修改(含删行)/ 大文件截断,外加删除与安装类的摘要。
// 每一类都断言两件事:预览行本身正确,以及**能被 AiUiText.LooksLikeDiff 认出来**
// (认不出来确认卡就不会按 diff 着色,等于白算)。
//
// 只读写临时目录(TestIsolation 已把 TIPCL_HOME 指到临时目录),不联网、不碰真实用户数据。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiPreviewTests
{
    /// <summary>每次都换一个实例名:预览用例会真建目录/文件,不能互相踩。</summary>
    private static string NewInstance(string tag)
    {
        string name = "prev-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(Instances.GetInstancePath(name));
        return name;
    }

    private static string ConfigFile(string instance, string rel = "sodium.toml")
        => Path.Combine(Instances.GetInstancePath(instance), "config", rel);

    private static string ModFile(string instance, string name)
    {
        string dir = Path.Combine(Instances.GetInstancePath(instance), "mods");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>按参数里的 instance 调预览(执行器当前实例就取它,与真实调用一致)。</summary>
    private static PyDict PreviewOf(string tool, PyDict args)
        => Preview.ChangePreview(tool, args, Py.S(args.Get("instance")))!;

    // ───────────────────────────── write_mod_config:逐行 diff ─────────────────────────────

    [Fact]
    public void WriteConfig_NewFile_ShowsAddedLinesAndMarksNew()
    {
        string inst = NewInstance("new");
        PyDict args = PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 1\nb = 2\n");

        PyDict preview = PreviewOf("write_mod_config", args);

        Assert.Equal("diff", Py.S(preview.Get("kind")));
        Assert.Equal("将要写入的变更：", Py.S(preview.Get("head")));
        Assert.False(Py.Truthy(preview.Get("too_large")));
        List<string> lines = Preview.Flatten(preview);
        Assert.Equal("@@ 将要写入的变更：", lines[0]);
        Assert.Contains("（新建文件）", lines);
        Assert.Contains("+a = 1", lines);
        Assert.Contains("+b = 2", lines);
        Assert.Contains("--- a/sodium.toml", lines);
        Assert.Contains("+++ b/sodium.toml", lines);
        Assert.Contains("@@ -0,0 +1,2 @@", lines);
        Assert.True(AiUiText.LooksLikeDiff(lines));
        // 预览是「看」不是「做」:不落盘
        Assert.False(File.Exists(ConfigFile(inst)));
    }

    [Fact]
    public void WriteConfig_Append_ShowsOnlyTheAddedLine()
    {
        string inst = NewInstance("append");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile(inst))!);
        File.WriteAllText(ConfigFile(inst), "a = 1\n");
        PyDict args = PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 1\nb = 2\n");

        List<string> lines = Preview.Flatten(PreviewOf("write_mod_config", args));

        Assert.Contains(" a = 1", lines);          // 没变的行带前导空格
        Assert.Contains("+b = 2", lines);
        Assert.Contains("@@ -1 +1,2 @@", lines);
        Assert.DoesNotContain("-a = 1", lines);    // 不能把没变的行报成删除
        Assert.DoesNotContain("（新建文件）", lines);
        Assert.True(AiUiText.LooksLikeDiff(lines));
    }

    [Fact]
    public void WriteConfig_ModifyAndDeleteLine_ShowsBeforeAndAfter()
    {
        string inst = NewInstance("modify");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile(inst))!);
        File.WriteAllText(ConfigFile(inst), "a = 1\nb = 2\n");

        List<string> changed = Preview.Flatten(
            PreviewOf("write_mod_config", PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 2\nb = 2\n")));
        Assert.Contains("-a = 1", changed);
        Assert.Contains("+a = 2", changed);

        List<string> removed = Preview.Flatten(
            PreviewOf("write_mod_config", PyDict.Of("instance", inst, "path", "sodium.toml", "content", "a = 1\n")));
        Assert.Contains("-b = 2", removed);
        Assert.Contains("@@ -1,2 +1 @@", removed);
        Assert.True(AiUiText.LooksLikeDiff(removed));
    }

    [Fact]
    public void WriteConfig_RejectsEscapingOrEmptyPath()
    {
        string inst = NewInstance("escape");
        Assert.Null(Preview.ChangePreview("write_mod_config",
            PyDict.Of("instance", inst, "path", "../outside.toml", "content", "x"), inst));
        Assert.Null(Preview.ChangePreview("write_mod_config",
            PyDict.Of("instance", inst, "path", "", "content", "x"), inst));
    }

    [Fact]
    public void WriteConfig_HugeChange_TruncatesTo200LinesAndStoresTheRest()
    {
        string inst = NewInstance("huge");
        var body = new List<string>();
        for (int i = 0; i < 400; i++)
        {
            body.Add("k" + i + " = " + i);
        }
        PyDict preview = PreviewOf("write_mod_config",
            PyDict.Of("instance", inst, "path", "big.toml", "content", string.Join("\n", body) + "\n"));

        Assert.True(Py.Truthy(preview.Get("too_large")));
        List<string> lines = Preview.Flatten(preview);
        // 1 行「@@ 将要写入的变更：」+ 1 行「（新建文件）」+ 前 200 行 diff + 1 行去向说明
        Assert.Equal(Preview.MAX_PREVIEW_LINES + 3, lines.Count);
        // 200 行是**连 diff 头一起**数的(--- / +++ / @@ 占掉 3 行,参考版同样如此)
        Assert.Contains("+k0 = 0", lines);
        Assert.Contains("+k196 = 196", lines);
        Assert.DoesNotContain("+k197 = 197", lines);
        Assert.Contains("变更过大", lines[lines.Count - 1]);
        Assert.Contains("cache/ai_results", lines[lines.Count - 1]);
    }

    // ───────────────────────────── 删除 / 启停 / 实例 ─────────────────────────────

    [Fact]
    public void DeleteMod_ListsFilesAndTotalSize()
    {
        string inst = NewInstance("delmod");
        string jar = ModFile(inst, "sodium.jar");
        File.WriteAllBytes(jar, new byte[2048]);

        PyDict preview = PreviewOf("delete_mod", PyDict.Of("instance", inst, "filename", "sodium.jar"));

        Assert.Equal("summary", Py.S(preview.Get("kind")));
        Assert.Equal("将被删除：", Py.S(preview.Get("head")));
        List<string> lines = Preview.Flatten(preview);
        Assert.Contains("- mods/sodium.jar(2.0 KB)", lines);
        Assert.Contains(lines, l => l.StartsWith("共 1 个文件，", StringComparison.Ordinal));
        Assert.True(AiUiText.LooksLikeDiff(lines));
        Assert.True(File.Exists(jar));  // 预览不删东西
    }

    [Fact]
    public void DeleteMod_MissingFile_SaysSoInsteadOfPretending()
    {
        string inst = NewInstance("delmod-missing");
        List<string> lines = Preview.Flatten(
            PreviewOf("delete_mod", PyDict.Of("instance", inst, "filename", "nope.jar")));
        Assert.Contains(lines, l => l.Contains("没找到这个文件"));
    }

    [Fact]
    public void DisableAndEnableMod_ShowTheRename()
    {
        string inst = NewInstance("toggle");
        File.WriteAllText(ModFile(inst, "sodium.jar"), "x");
        PyDict disabledPreview = PreviewOf("disable_mod", PyDict.Of("instance", inst, "filename", "sodium.jar"));
        Assert.Equal("将被改名：", Py.S(disabledPreview.Get("head")));
        Assert.Contains("- mods/sodium.jar → mods/sodium.jar.disabled", Preview.Flatten(disabledPreview));

        File.WriteAllText(ModFile(inst, "lithium.jar.disabled"), "x");
        List<string> enabled = Preview.Flatten(
            PreviewOf("enable_mod", PyDict.Of("instance", inst, "filename", "lithium.jar")));
        Assert.Contains("- mods/lithium.jar.disabled → mods/lithium.jar", enabled);
    }

    [Fact]
    public void DeleteInstance_CountsFilesAndMarksIrreversible()
    {
        string inst = NewInstance("delinst");
        string dir = Instances.GetInstancePath(inst);
        Directory.CreateDirectory(Path.Combine(dir, "mods"));
        File.WriteAllText(Path.Combine(dir, "mods", "a.jar"), "1");
        File.WriteAllText(Path.Combine(dir, "mods", "b.jar"), "22");

        List<string> lines = Preview.Flatten(PreviewOf("delete_instance", PyDict.Of("name", inst)));

        Assert.Contains("- " + inst + "/", lines);
        Assert.Contains(lines, l => l.Contains("共 2 个文件") && l.Contains("不可恢复"));
        Assert.True(Directory.Exists(dir));
    }

    // ───────────────────────────── 安装 / 启动 ─────────────────────────────

    [Fact]
    public void InstallTools_ShowTargetDirectoryAndFileCount()
    {
        string inst = NewInstance("install");
        string dir = Instances.GetInstancePath(inst);

        List<string> mod = Preview.Flatten(PreviewOf("install_mod", PyDict.Of("instance", inst, "name", "sodium")));
        Assert.Equal(Path.Combine(dir, "mods"), mod[1]);
        Assert.Contains("预计新增文件数：1", mod);

        List<string> pack = Preview.Flatten(PreviewOf("install_modpack", PyDict.Of("instance", inst, "name", "fabulously")));
        Assert.Equal(dir, pack[1]);
        Assert.Contains("预计新增文件数：安装时确定（多于 1 个）", pack);

        List<string> java = Preview.Flatten(PreviewOf("download_java", PyDict.Of("major", "17")));
        Assert.Contains(java, l => l.Contains("Java 运行时目录"));

        List<string> world = Preview.Flatten(PreviewOf("install_world", PyDict.Of("instance", inst, "name", "生存")));
        Assert.Equal(Path.Combine(dir, "saves"), world[1]);
        Assert.Contains(world, l => l.Contains("存档目录"));
    }

    [Fact]
    public void LaunchGame_ShowsTargetAsInfo()
    {
        string inst = NewInstance("launch");
        PyDict preview = PreviewOf("launch_game", PyDict.Of("instance", inst, "version", "1.20.1"));
        Assert.Equal("info", Py.S(preview.Get("kind")));
        Assert.Equal("启动目标：", Py.S(preview.Get("head")));
        List<string> lines = Preview.Flatten(preview);
        Assert.Contains("实例：" + inst, lines);
        Assert.Contains("版本：1.20.1", lines);
    }

    [Fact]
    public void ReadOnlyAndUnknownTools_HaveNoPreview()
    {
        Assert.Null(Preview.ChangePreview("list_mods", PyDict.Of("instance", "default"), "default"));
        Assert.Null(Preview.ChangePreview("read_mod_config", PyDict.Of("path", "a.toml"), "default"));
        Assert.Null(Preview.ChangePreview("mcp__demo__ping", new PyDict(), "default"));
        Assert.Empty(Preview.Flatten(null));
    }

    /// <summary>摊平规则照参考版 ShowConfirm:head 以「@@ 」开头、只丢空串(纯空白行照旧保留)。</summary>
    [Fact]
    public void Flatten_AddsHeadPrefixAndDropsEmptyLines()
    {
        PyDict row = PyDict.Of("kind", "summary", "head", "将被删除：",
            "lines", new List<object> { "- mods/a.jar(1.0 KB)", "", "  ", "共 1 个文件，1.0 KB" }, "too_large", false);
        Assert.Equal(new[] { "@@ 将被删除：", "- mods/a.jar(1.0 KB)", "  ", "共 1 个文件，1.0 KB" }, Preview.Flatten(row));

        // head 已经是 @@ 开头的不再套一层
        List<string> raw = Preview.Flatten(PyDict.Of("head", "@@ 汇总", "lines", new List<object> { "+x" }));
        Assert.Equal("@@ 汇总", raw[0]);
    }
}

// AI2-content:数据包 / 资源包 / KubeJS 脚本 / 内容加载检查四个工具。
//
// 覆盖:pack_format 对照(含「查不到就拒绝」)、1.21 起目录单数化、各类 JSON 校验的正反例、函数文件语法、
// PNG 校验、KubeJS 危险用法拦截(常量表里每条规则都有样例命中)、路径穿越拦截(含目录链接逃逸)、
// 确认链(预览 diff / 拒绝不落盘 / plan 档不弹卡 / KubeJS 不给「本对话都允许」)、检查点能撤回、
// validate_content 从日志里按类别抠错误。
// 全部在 TestIsolation 指好的临时 TIPCL_HOME 里跑;要放到「允许范围之外」的东西放在系统临时目录下另一个随机目录,用完删掉。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Pages.Ai;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiContentTests
{
    private sealed class NullApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private static Instance NewInstance(string tag) => Instances.CreateUniqueInstance("ct-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));

    private static string Root(Instance inst) => PPath.Abs(inst.path);

    private static string NewWorld(Instance inst, string name = "World1")
    {
        string dir = Path.Combine(Root(inst), "saves", name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "level.dat"), new byte[] { 1, 2, 3 });
        return dir;
    }

    private static string ReadNorm(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static ToolRunner Runner(Instance inst, string mode = "default", Func<ToolCall, string, string, PyDict?, Task<(bool, bool)>>? confirm = null)
        => new(new NullApi())
        {
            PermissionMode = mode,
            Instance = inst.name,
            Confirm = confirm ?? ((_, _, _, _) => Task.FromResult((true, false))),
        };

    private static async Task<(ToolCall Call, string Text)> Run(ToolRunner runner, string tool, PyDict args)
    {
        var call = new ToolCall("c-" + Guid.NewGuid().ToString("N").Substring(0, 6), tool, args);
        string text = await runner.RunAsync(call);
        return (call, text);
    }

    private static PyDict Json(string text) => (PyDict)PyJson.Loads(text);

    /// <summary>工具失败时回给模型的是 JSON(中文被转义成 \uXXXX),断言前先解出 message + detail。</summary>
    private static string Msg(string text)
    {
        try
        {
            if (PyJson.Loads(text) is PyDict d)
            {
                return Py.S(d.Get("message")) + "\n" + Py.S(d.Get("detail"));
            }
        }
        catch (Exception)
        {
        }
        return text;
    }

    private static PyDict File1(string type, string path, string content, params object[] extra)
    {
        PyDict d = PyDict.Of("type", type, "path", path, "content", content);
        for (int i = 0; i + 1 < extra.Length; i += 2)
        {
            d[(string)extra[i]] = extra[i + 1];
        }
        return d;
    }

    private static PyDict DatapackArgs(string world, string name, string mc, params PyDict[] files)
        => PyDict.Of("world", world, "name", name, "namespace", "demo", "mc_version", mc, "files", files.Cast<object>().ToList());

    private const string ShapedOld = """{"type":"minecraft:crafting_shaped","pattern":["##","##"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"item":"minecraft:diamond","count":1}}""";

    private const string ShapedNew = """{"type":"minecraft:crafting_shaped","pattern":["##","##"],"key":{"#":"minecraft:dirt"},"result":{"id":"minecraft:diamond","count":1}}""";

    private const string ShapedMid = """{"type":"minecraft:crafting_shaped","pattern":["##","##"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"id":"minecraft:diamond","count":1}}""";

    /// <summary>造一段 PNG 头(签名 + IHDR);校验只看前 33 字节,后面补点数据。</summary>
    private static byte[] Png(int w, int h, bool goodSig = true)
    {
        var b = new List<byte>(goodSig ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } : Encoding.ASCII.GetBytes("GIF89a\0\0"));
        b.AddRange(new byte[] { 0, 0, 0, 13 });
        b.AddRange(Encoding.ASCII.GetBytes("IHDR"));
        b.AddRange(new[] { (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w });
        b.AddRange(new[] { (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h });
        b.AddRange(new byte[] { 8, 6, 0, 0, 0, 0, 0, 0, 0 });
        b.AddRange(new byte[16]);
        return b.ToArray();
    }

    private static string FakeJar(string modsDir, string file, string entry, string text)
    {
        Directory.CreateDirectory(modsDir);
        string path = Path.Combine(modsDir, file);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        return path;
    }

    private static void InstallKubeJs(Instance inst)
        => FakeJar(Path.Combine(Root(inst), "mods"), "kubejs-fabric-2001.6.5-build.16.jar", "fabric.mod.json", "{\"schemaVersion\":1,\"id\":\"kubejs\",\"version\":\"2001.6.5\"}");

    private static void Throws<T>(Action act, string contains) where T : Exception
    {
        T ex = Assert.ThrowsAny<T>(act);
        Assert.Contains(contains, ex.Message);
    }

    // ───────────────────────────── 注册 ─────────────────────────────

    [Fact]
    public async Task Tools_AreRegisteredInSchemaMetaDispatch_AndKubeJsNeverAlwaysAllowed()
    {
        foreach (string name in ContentTools.Names)
        {
            Assert.Contains(name, Tools.Names());
            Assert.NotNull(ToolMeta.Get(name));
            Assert.True(await ToolRunner.DispatchesAsync(name), "分发不认 " + name);
            Assert.NotEqual(name, Tools.Label(name));
        }
        Assert.True(ToolMeta.Get("validate_content")!.@readonly);
        Assert.Equal("high", ToolMeta.Get("write_kubejs_script")!.risk);
        Assert.False(ToolMeta.Get("create_datapack")!.@readonly);
        Assert.False(ConfirmCard.AllowAlways("write_kubejs_script"));
        Assert.False(ToolRunner.AllowsAlways("write_kubejs_script"));
        Assert.True(ConfirmCard.AllowAlways("create_datapack"));
        Assert.Contains("create_datapack", AiData.SystemPrompt());
        Assert.Contains(AiData.KEYWORD_TOOLS, k => k.extra.Contains("write_kubejs_script") && k.keys.Contains("KubeJS"));
        Assert.Equal(Tools.Names().Count, Tools.Names().Distinct().Count());
    }

    // ───────────────────────────── 格式对照 / 单复数目录 ─────────────────────────────

    [Theory]
    [InlineData("1.13", 4)]
    [InlineData("1.16.5", 6)]
    [InlineData("1.18.2", 9)]
    [InlineData("1.19.4", 12)]
    [InlineData("1.20.1", 15)]
    [InlineData("1.20.4", 26)]
    [InlineData("1.20.6", 41)]
    [InlineData("1.21", 48)]
    [InlineData("1.21.0", 48)]
    [InlineData("1.21.1", 48)]
    [InlineData("1.21.4", 61)]
    [InlineData("1.21.8", 81)]
    [InlineData("1.21.10", 88)]
    [InlineData("1.21.11", 94)]
    [InlineData("26.1.2", 101)]
    [InlineData("26.3", 121)]
    public void DataPackFormat_MatchesWikiTable(string mc, int expected)
        => Assert.Equal(expected, ContentFormats.DataPackFormat(mc)?.Major);

    [Theory]
    [InlineData("1.13", 4)]
    [InlineData("1.18.2", 8)]
    [InlineData("1.19.3", 12)]
    [InlineData("1.20.1", 15)]
    [InlineData("1.20.4", 22)]
    [InlineData("1.21.1", 34)]
    [InlineData("1.21.4", 46)]
    [InlineData("1.21.8", 64)]
    [InlineData("26.3", 97)]
    public void ResourcePackFormat_MatchesWikiTable(string mc, int expected)
        => Assert.Equal(expected, ContentFormats.ResourcePackFormat(mc)?.Major);

    [Theory]
    [InlineData("1.12.2")]
    [InlineData("1.16.6")]
    [InlineData("1.21.12")]
    [InlineData("1.99")]
    [InlineData("2.0")]
    [InlineData("26.4")]
    [InlineData("1.20.1-forge")]
    [InlineData("")]
    public void UnknownVersions_AreNotGuessed(string mc)
    {
        Assert.Null(ContentFormats.DataPackFormat(mc));
        Assert.Null(ContentFormats.ResourcePackFormat(mc));
    }

    [Fact]
    public void PackMeta_UsesPackFormatBefore1219_AndMinMaxFormatFrom1219()
    {
        PyDict old = Py.D(ContentFormats.PackMeta(ContentFormats.DataPackFormat("1.21.8")!, "1.21.8", "d").Get("pack"));
        Assert.Equal(81L, old.Get("pack_format"));
        Assert.False(old.ContainsKey("min_format"));
        PyDict neu = Py.D(ContentFormats.PackMeta(ContentFormats.DataPackFormat("1.21.10")!, "1.21.10", "d").Get("pack"));
        Assert.Equal(88L, neu.Get("min_format"));
        Assert.Equal(88L, neu.Get("max_format"));
        Assert.False(neu.ContainsKey("pack_format"));
        Assert.Equal("94.1", ContentFormats.FormatText(ContentFormats.DataPackFormat("1.21.11")!));
    }

    [Theory]
    [InlineData("recipe", "1.20.6", "recipes")]
    [InlineData("recipe", "1.21", "recipe")]
    [InlineData("loot_table", "1.20.4", "loot_tables")]
    [InlineData("loot_table", "1.21.4", "loot_table")]
    [InlineData("advancement", "1.18.2", "advancements")]
    [InlineData("advancement", "26.1", "advancement")]
    [InlineData("function", "1.20.1", "functions")]
    [InlineData("function", "1.21.1", "function")]
    [InlineData("recipes", "1.21", "recipe")]
    public void DataDir_PluralBefore121_SingularFrom121(string type, string mc, string expected)
        => Assert.Equal(expected, ContentFormats.DataDir(type, mc));

    [Fact]
    public void TagDir_FollowsTheSameRename()
    {
        Assert.Equal("tags/items", ContentFormats.TagDir("item", "1.20.1"));
        Assert.Equal("tags/item", ContentFormats.TagDir("item", "1.21"));
        Assert.Equal("tags/functions", ContentFormats.TagDir("function", "1.20.4"));
        Assert.Equal("tags/function", ContentFormats.TagDir("function", "1.21.1"));
        Assert.Equal("tags/entity_types", ContentFormats.TagDir("entity_types", "1.19.4"));
    }

    [Fact]
    public async Task CreateDatapack_WritesVersionedDirsAndPackMeta()
    {
        Instance inst = NewInstance("dirs");
        NewWorld(inst, "Old");
        NewWorld(inst, "New");
        ToolRunner runner = Runner(inst);
        foreach ((string world, string mc, string recipe, string recipeDir, string tagDir, string fnDir, long format) in new[]
                 {
                     ("Old", "1.20.4", ShapedOld, "recipes", "tags/items", "functions", 26L),
                     ("New", "1.21.4", ShapedNew, "recipe", "tags/item", "function", 61L),
                 })
        {
            (ToolCall call, string text) = await Run(runner, "create_datapack", DatapackArgs(world, "mypack", mc,
                File1("recipe", "dirt_to_diamond", recipe),
                File1("tag", "shiny", "{\"values\":[\"minecraft:diamond\",\"#minecraft:logs\"]}", "tag_type", "items"),
                File1("function", "init.mcfunction", "say hi\n# 注释\n\ngive @s minecraft:diamond 1")));
            Assert.True(call.status == ToolCallStatus.COMPLETED, text);
            string pack = Path.Combine(Root(inst), "saves", world, "datapacks", "mypack");
            Assert.True(File.Exists(Path.Combine(pack, "data", "demo", recipeDir, "dirt_to_diamond.json")), text);
            Assert.True(File.Exists(Path.Combine(pack, "data", "demo", tagDir.Replace('/', Path.DirectorySeparatorChar), "shiny.json")), text);
            Assert.True(File.Exists(Path.Combine(pack, "data", "demo", fnDir, "init.mcfunction")), text);
            PyDict meta = Py.D(Json(File.ReadAllText(Path.Combine(pack, "pack.mcmeta"))).Get("pack"));
            Assert.Equal(format, meta.Get("pack_format"));
            Assert.Equal("say hi\n# 注释\n\ngive @s minecraft:diamond 1\n", ReadNorm(Path.Combine(pack, "data", "demo", fnDir, "init.mcfunction")));
            PyDict result = Json(text);
            Assert.Equal(mc, result.Get("mc_version"));
            Assert.Contains("/reload", Py.S(result.Get("next_step")));
        }
    }

    [Fact]
    public async Task CreateDatapack_InfersMcFromInstance_AndRejectsUnknownVersionWithoutGuessing()
    {
        Instance inst = NewInstance("infer");
        NewWorld(inst);
        inst.SetMeta("mc_version", "1.20.1");
        ToolRunner runner = Runner(inst);
        PyDict args = PyDict.Of("world", "World1", "name", "auto", "namespace", "demo",
            "files", new List<object> { File1("recipe", "r", ShapedOld) });
        (ToolCall ok, string okText) = await Run(runner, "create_datapack", args);
        Assert.True(ok.status == ToolCallStatus.COMPLETED, okText);
        Assert.Equal("1.20.1", Json(okText).Get("mc_version"));
        Assert.Equal("15", Json(okText).Get("pack_format"));

        PyDict bad = DatapackArgs("World1", "future", "1.99", File1("recipe", "r", ShapedOld));
        (ToolCall rejected, string rejectedText) = await Run(runner, "create_datapack", bad);
        Assert.Equal(ToolCallStatus.FAILED, rejected.status);
        Assert.Contains("不猜格式号", Msg(rejectedText));
        Assert.False(Directory.Exists(Path.Combine(Root(inst), "saves", "World1", "datapacks", "future")));
    }

    // ───────────────────────────── JSON 校验:配方 ─────────────────────────────

    private static object J(string s) => PyJson.Loads(s);

    [Fact]
    public void Recipe_VersionAwareFormats_PositiveCases()
    {
        Assert.Equal("", ContentValidate.Recipe(J(ShapedOld), "1.20.1"));
        Assert.Equal("", ContentValidate.Recipe(J(ShapedMid), "1.20.6"));
        Assert.Equal("", ContentValidate.Recipe(J(ShapedNew), "1.21.4"));
        Assert.Equal("", ContentValidate.Recipe(J("""{"type":"crafting_shapeless","ingredients":[{"item":"minecraft:dirt"},{"tag":"minecraft:logs"}],"result":{"item":"minecraft:diamond"}}"""), "1.19.2"));
        Assert.Equal("", ContentValidate.Recipe(J("""{"type":"minecraft:smelting","ingredient":{"item":"minecraft:rotten_flesh"},"result":"minecraft:leather","experience":0.1,"cookingtime":200}"""), "1.20.1"));
        Assert.Equal("", ContentValidate.Recipe(J("""{"type":"minecraft:smelting","ingredient":"minecraft:rotten_flesh","result":{"id":"minecraft:leather"},"cookingtime":200}"""), "26.3"));
        Assert.Equal("", ContentValidate.Recipe(J("""{"type":"minecraft:stonecutting","ingredient":{"item":"minecraft:stone"},"result":"minecraft:stone_slab","count":2}"""), "1.20.1"));
        Assert.Equal("", ContentValidate.Recipe(J("""{"type":"minecraft:crafting_shaped","pattern":["a"],"key":{"a":["minecraft:oak_log","#minecraft:planks"]},"result":{"id":"minecraft:stick","count":4}}"""), "1.21.2"));
        // 认不出的模组配方类型:只查 type 写法,并在结果里说明
        Assert.Contains("只检查了 type", ContentValidate.Recipe(J("""{"type":"create:mixing","ingredients":[]}"""), "1.20.1"));
    }

    [Theory]
    [InlineData("""{"pattern":["#"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "type")]
    [InlineData("""{"type":"Minecraft:Crafting_Shaped","pattern":["#"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "resource location")]
    [InlineData("""{"type":"minecraft:crafting_shaped","pattern":["#X"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "'X'")]
    [InlineData("""{"type":"minecraft:crafting_shaped","pattern":["#"],"key":{"#":{"item":"minecraft:dirt"},"Y":{"item":"minecraft:stone"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "没用到")]
    [InlineData("""{"type":"minecraft:crafting_shaped","pattern":["##","#"],"key":{"#":{"item":"minecraft:dirt"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "长度必须相同")]
    [InlineData("""{"type":"minecraft:crafting_shaped","pattern":["#"],"key":{"#":{"item":"minecraft:Dirt"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "key.#.item")]
    [InlineData("""{"type":"minecraft:crafting_shaped","pattern":["#"],"key":{"#":{"item":"a:b:c"}},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "key.#.item")]
    [InlineData(ShapedOld, "1.21.4", "字符串")]
    [InlineData(ShapedMid, "1.20.1", "item")]
    [InlineData(ShapedOld, "1.20.6", "id")]
    [InlineData("""{"type":"minecraft:crafting_shapeless","ingredients":[],"result":{"item":"minecraft:diamond"}}""", "1.20.1", "ingredients")]
    [InlineData("""{"type":"minecraft:crafting_shapeless","ingredients":[{"item":"minecraft:dirt"}],"result":{"item":"minecraft:diamond","count":0}}""", "1.20.1", "result.count")]
    [InlineData("""{"type":"minecraft:smelting","ingredient":{"item":"minecraft:dirt"},"result":{"item":"minecraft:diamond"}}""", "1.20.1", "字符串")]
    [InlineData("""{"type":"minecraft:smelting","ingredient":"minecraft:dirt","result":{"id":"minecraft:diamond"}}""", "26.3", "cookingtime")]
    [InlineData("""["not","an","object"]""", "1.20.1", "对象")]
    public void Recipe_NegativeCases_AreRejectedWithTheField(string json, string mc, string expect)
        => Throws<ValueError>(() => ContentValidate.Recipe(J(json), mc), expect);

    // ───────────────────────────── JSON 校验:战利品表 / 进度 / 标签 / 函数 ─────────────────────────────

    [Fact]
    public void LootTable_PositiveAndNegative()
    {
        ContentValidate.LootTable(J("""{"type":"minecraft:block","pools":[{"rolls":1,"entries":[{"type":"minecraft:item","name":"minecraft:diamond"}]}]}"""), "1.20.1");
        ContentValidate.LootTable(J("""{"pools":[{"rolls":{"type":"minecraft:uniform","min":1,"max":3},"entries":[{"type":"minecraft:empty"}]}]}"""), "1.21");
        Throws<ValueError>(() => ContentValidate.LootTable(J("""{"pools":[{"entries":[]}]}"""), "1.20.1"), "rolls");
        Throws<ValueError>(() => ContentValidate.LootTable(J("""{"pools":[{"rolls":1}]}"""), "1.20.1"), "entries");
        Throws<ValueError>(() => ContentValidate.LootTable(J("""{"pools":[{"rolls":1,"entries":[{"type":"minecraft:item"}]}]}"""), "1.20.1"), "name");
        Throws<ValueError>(() => ContentValidate.LootTable(J("""{"pools":[{"rolls":1,"entries":[{"type":"item","name":"DIAMOND"}]}]}"""), "1.20.1"), "name");
    }

    [Fact]
    public void Advancement_PositiveAndNegative()
    {
        const string good = """{"parent":"minecraft:story/root","display":{"icon":{"item":"minecraft:diamond"},"title":"挖到钻石","description":{"text":"恭喜"},"frame":"goal"},"criteria":{"got":{"trigger":"minecraft:inventory_changed"}},"requirements":[["got"]]}""";
        ContentValidate.Advancement(J(good), "1.20.1");
        ContentValidate.Advancement(J(good.Replace("\"item\":", "\"id\":")), "1.21");
        Throws<ValueError>(() => ContentValidate.Advancement(J(good), "1.21"), "display.icon.id");
        Throws<ValueError>(() => ContentValidate.Advancement(J("""{"criteria":{}}"""), "1.20.1"), "criteria");
        Throws<ValueError>(() => ContentValidate.Advancement(J("""{"criteria":{"a":{}}}"""), "1.20.1"), "trigger");
        Throws<ValueError>(() => ContentValidate.Advancement(J(good.Replace("[[\"got\"]]", "[[\"nope\"]]")), "1.20.1"), "nope");
        Throws<ValueError>(() => ContentValidate.Advancement(J(good.Replace("\"goal\"", "\"boss\"")), "1.20.1"), "frame");
        Throws<ValueError>(() => ContentValidate.Advancement(J("""{"display":{"icon":{"item":"minecraft:diamond"}},"criteria":{"a":{"trigger":"minecraft:tick"}}}"""), "1.20.1"), "title");
    }

    [Fact]
    public void Tag_PositiveAndNegative()
    {
        ContentValidate.Tag(J("""{"replace":false,"values":["minecraft:stone","#minecraft:logs",{"id":"othermod:thing","required":false}]}"""), "1.20.1");
        Throws<ValueError>(() => ContentValidate.Tag(J("""{"replace":false}"""), "1.20.1"), "values");
        Throws<ValueError>(() => ContentValidate.Tag(J("""{"values":["Minecraft:Stone"]}"""), "1.20.1"), "values[0]");
        Throws<ValueError>(() => ContentValidate.Tag(J("""{"values":["minecraft:stone"],"replace":"yes"}"""), "1.20.1"), "replace");
        Throws<ValueError>(() => ContentValidate.Tag(J("""{"values":[{"id":"minecraft:stone","required":"no"}]}"""), "1.20.1"), "required");
    }

    [Fact]
    public void Function_BasicSyntax()
    {
        Assert.Equal("say hi\ngive @s minecraft:diamond\n", ContentValidate.Function("say hi\r\ngive @s minecraft:diamond\r\n\r\n", "1.20.1"));
        Assert.Equal("# 只有注释也算一行命令前的说明\nsay ok\n", ContentValidate.Function("# 只有注释也算一行命令前的说明\nsay ok", "1.19.2"));
        Throws<ValueError>(() => ContentValidate.Function("say hi\n/give @s minecraft:diamond", "1.20.1"), "第 2 行以 / 开头");
        Throws<ValueError>(() => ContentValidate.Function("Say hi", "1.20.1"), "小写");
        Throws<ValueError>(() => ContentValidate.Function("$say $(msg)", "1.20.1"), "宏行");
        Assert.Equal("$say $(msg)\n", ContentValidate.Function("$say $(msg)", "1.20.2"));
        Throws<ValueError>(() => ContentValidate.Function("   \n  ", "1.20.1"), "不能为空");
        Throws<ValueError>(() => ContentValidate.Function(PyDict.Of("a", 1L), "1.20.1"), "不是 JSON");
    }

    [Fact]
    public void ResourceLocation_FormatRules()
    {
        Assert.True(ContentValidate.IsResourceLocation("minecraft:stone"));
        Assert.True(ContentValidate.IsResourceLocation("stone"));
        Assert.True(ContentValidate.IsResourceLocation("my_mod:block/ore.v2"));
        Assert.False(ContentValidate.IsResourceLocation("Minecraft:stone"));
        Assert.False(ContentValidate.IsResourceLocation("minecraft:Stone"));
        Assert.False(ContentValidate.IsResourceLocation("a:b:c"));
        Assert.False(ContentValidate.IsResourceLocation("minecraft:"));
        Assert.False(ContentValidate.IsResourceLocation("minecraft:a//b"));
        Assert.False(ContentValidate.IsResourceLocation("minecraft:../x"));
        Assert.False(ContentValidate.IsResourceLocation("mine craft:x"));
    }

    // ───────────────────────────── 资源包:JSON / PNG ─────────────────────────────

    [Fact]
    public void ResourcePackJson_PositiveAndNegative()
    {
        ContentValidate.Lang(J("""{"item.minecraft.diamond":"闪亮的石头"}"""));
        Throws<ValueError>(() => ContentValidate.Lang(J("""{"item.minecraft.diamond":1}""")), "字符串");
        Throws<ValueError>(() => ContentValidate.Lang(J("""["a"]""")), "对象");
        Assert.True(ContentValidate.IsLangCode("zh_cn"));
        Assert.False(ContentValidate.IsLangCode("zh_CN"));

        ContentValidate.Model(J("""{"parent":"minecraft:block/cube_all","textures":{"all":"demo:block/ore"}}"""));
        ContentValidate.Model(J("""{"textures":{"0":"demo:block/x","particle":"#0"},"elements":[{"from":[0,0,0],"to":[16,8,16],"faces":{"up":{"texture":"#0"}}}]}"""));
        Throws<ValueError>(() => ContentValidate.Model(J("""{"display":{}}""")), "parent");
        Throws<ValueError>(() => ContentValidate.Model(J("""{"parent":"minecraft:block/cube_all","textures":{"all":"Demo:Block/Ore"}}""")), "textures.all");
        Throws<ValueError>(() => ContentValidate.Model(J("""{"elements":[{"from":[0,0,0],"to":[16,40,16]}]}""")), "elements[0].to");
        Throws<ValueError>(() => ContentValidate.Model(J("""{"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{"top":{"texture":"#0"}}}]}""")), "faces.top");

        ContentValidate.Blockstate(J("""{"variants":{"":{"model":"demo:block/ore"}}}"""));
        ContentValidate.Blockstate(J("""{"variants":{"facing=north":[{"model":"demo:block/a","y":90},{"model":"demo:block/b"}]}}"""));
        ContentValidate.Blockstate(J("""{"multipart":[{"when":{"up":"true"},"apply":{"model":"demo:block/post"}}]}"""));
        Throws<ValueError>(() => ContentValidate.Blockstate(J("""{"variants":{"":{"model":"demo:block/a"}},"multipart":[]}""")), "有且只有一个");
        Throws<ValueError>(() => ContentValidate.Blockstate(J("""{"variants":{"":{}}}""")), "model");
        Throws<ValueError>(() => ContentValidate.Blockstate(J("""{"variants":{"":{"model":"demo:block/a","y":45}}}""")), "旋转");
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(8, 8)]
    [InlineData(1, 1)]
    [InlineData(512, 512)]
    [InlineData(16, 48)]
    [InlineData(48, 48)]
    [InlineData(32, 64)]
    public void Png_AcceptsPowerOfTwoOrMultipleOf16(int w, int h)
        => Assert.Equal((w, h), ContentValidate.Png(Png(w, h)));

    [Theory]
    [InlineData(24, 24, "既不是 2 的幂")]
    [InlineData(16, 20, "既不是 2 的幂")]
    [InlineData(0, 16, "尺寸异常")]
    public void Png_RejectsOddSizes(int w, int h, string expect)
        => Throws<ValueError>(() => ContentValidate.Png(Png(w, h)), expect);

    [Fact]
    public void Png_RejectsNonPngBytes()
    {
        Throws<ValueError>(() => ContentValidate.Png(Png(16, 16, goodSig: false)), "不是 PNG");
        Throws<ValueError>(() => ContentValidate.Png(Encoding.UTF8.GetBytes("hello")), "不是 PNG");
        byte[] wrongChunk = Png(16, 16);
        wrongChunk[12] = (byte)'I';
        wrongChunk[13] = (byte)'D';
        wrongChunk[14] = (byte)'A';
        wrongChunk[15] = (byte)'T';
        Throws<ValueError>(() => ContentValidate.Png(wrongChunk), "IHDR");
    }

    [Fact]
    public async Task CreateResourcepack_WritesLangModelBlockstateTexture_AndMergesLang()
    {
        Instance inst = NewInstance("rp");
        string tmp = Path.Combine(Path.GetTempPath(), "tipcl-ct-png-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string png = Path.Combine(tmp, "ore.png");
            File.WriteAllBytes(png, Png(16, 16));
            ToolRunner runner = Runner(inst);
            PyDict args = PyDict.Of("name", "MyRP", "namespace", "demo", "mc_version", "1.20.1", "files", new List<object>
            {
                File1("lang", "zh_cn", """{"block.demo.ore":"矿石"}"""),
                File1("model", "block/ore", """{"parent":"minecraft:block/cube_all","textures":{"all":"demo:block/ore"}}"""),
                File1("blockstate", "ore", """{"variants":{"":{"model":"demo:block/ore"}}}"""),
                PyDict.Of("type", "texture", "path", "block/ore", "source", png),
            });
            (ToolCall call, string text) = await Run(runner, "create_resourcepack", args);
            Assert.True(call.status == ToolCallStatus.COMPLETED, text);
            string root = Path.Combine(Root(inst), "resourcepacks", "MyRP");
            Assert.Equal(15L, Py.D(Json(File.ReadAllText(Path.Combine(root, "pack.mcmeta"))).Get("pack")).Get("pack_format"));
            Assert.True(File.Exists(Path.Combine(root, "assets", "demo", "models", "block", "ore.json")));
            Assert.True(File.Exists(Path.Combine(root, "assets", "demo", "blockstates", "ore.json")));
            Assert.Equal(Png(16, 16), File.ReadAllBytes(Path.Combine(root, "assets", "demo", "textures", "block", "ore.png")));

            // update + merge:新键并进去,老键保留
            PyDict merge = PyDict.Of("name", "MyRP", "namespace", "demo", "mc_version", "1.20.1", "update", true, "files", new List<object>
            {
                File1("lang", "zh_cn", """{"item.demo.gem":"宝石"}""", "merge", true),
            });
            (ToolCall merged, string mergedText) = await Run(runner, "create_resourcepack", merge);
            Assert.True(merged.status == ToolCallStatus.COMPLETED, mergedText);
            PyDict lang = Json(File.ReadAllText(Path.Combine(root, "assets", "demo", "lang", "zh_cn.json")));
            Assert.Equal("矿石", lang.Get("block.demo.ore"));
            Assert.Equal("宝石", lang.Get("item.demo.gem"));

            // 贴图:假 PNG、带 content、相对路径都拒
            string fake = Path.Combine(tmp, "fake.png");
            File.WriteAllText(fake, "not a png");
            foreach ((PyDict file, string expect) in new[]
                     {
                         (PyDict.Of("type", "texture", "path", "block/x", "source", fake), "不是 PNG"),
                         (PyDict.Of("type", "texture", "path", "block/x", "content", "iVBOR..."), "不生成图片"),
                         (PyDict.Of("type", "texture", "path", "block/x", "source", "ore.png"), "绝对路径"),
                         (PyDict.Of("type", "texture", "path", "block/x", "source", Path.Combine(tmp, "x.jpg")), ".png"),
                     })
            {
                (ToolCall bad, string badText) = await Run(runner, "create_resourcepack",
                    PyDict.Of("name", "Bad", "mc_version", "1.20.1", "files", new List<object> { file }));
                Assert.Equal(ToolCallStatus.FAILED, bad.status);
                Assert.Contains(expect, Msg(badText));
            }
            Assert.False(Directory.Exists(Path.Combine(Root(inst), "resourcepacks", "Bad")));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }

    // ───────────────────────────── KubeJS ─────────────────────────────

    /// <summary>常量表里每条规则都至少有一个样例命中(规则加了却没有样例会在这里红)。</summary>
    private static readonly Dictionary<string, string> DangerSamples = new()
    {
        ["require(...) 加载模块"] = "const fs = require('fs')",
        ["eval 动态求值"] = "eval('1+1')",
        ["Function 构造器动态求值"] = "let f = new Function('return 1')",
        ["importPackage / importClass / JavaImporter"] = "importPackage(java.util)",
        ["JavaAdapter"] = "new JavaAdapter(Runnable, {})",
        ["Packages.* 直接访问 Java 包"] = "Packages.java.util.List",
        ["java.lang.Runtime(执行外部命令)"] = "Java.loadClass('java.lang.Runtime').getRuntime().exec('calc')",
        ["ProcessBuilder / Process(起进程)"] = "const PB = Java.loadClass(\"java.lang.ProcessBuilder\")",
        ["java.lang.reflect / invoke(反射)"] = "Java.loadClass('java.lang.reflect.Method')",
        ["ClassLoader(加载任意类)"] = "const L = Java.loadClass('java.net.URLClassLoader')",
        ["Class.forName(反射加载类)"] = "x.getClass().forName('a.B')",
        ["setAccessible(绕过访问控制)"] = "field.setAccessible(true)",
        ["java.lang.System(退出进程 / 读环境变量)"] = "Java.loadClass('java.lang.System').exit(0)",
        ["java.lang.Thread(后台线程)"] = "Java.loadClass('java.lang.Thread')",
        ["java.net(网络)"] = "Java.loadClass('java.net.URL')",
        ["java.nio.file / channels(文件系统)"] = "Java.loadClass('java.nio.file.Files')",
        ["java.io(文件读写)"] = "Java.loadClass('java.io.File')",
        ["javax.script(脚本引擎)"] = "Java.loadClass('javax.script.ScriptEngineManager')",
        ["sun.* / jdk.internal / com.sun.*(JDK 内部类)"] = "Java.loadClass('sun.misc.Unsafe')",
        ["Java.loadClass 参数不是字符串常量(没法确认加载的是什么)"] = "Java.loadClass(name)",
    };

    [Fact]
    public void KubeJs_EveryDeniedRule_HasASampleThatHits()
    {
        foreach ((string rule, _) in ContentValidate.KubeJsDenied)
        {
            Assert.True(DangerSamples.TryGetValue(rule, out string? sample), "规则没有测试样例: " + rule);
            Assert.Contains(ContentValidate.KubeJsViolations(sample!), h => h.Contains(rule, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Java.loadClass(\"java.n\" + \"et.URL\")")]
    [InlineData("Java.loadClass(\n  'java.io.File'\n)")]
    [InlineData("Java . loadClass ( cls )")]
    [InlineData("\\u0065val('x')")]
    [InlineData("Java.loadClass('java.\\u006eet.URL')")]
    [InlineData("// 注释里写 require('fs') 也算\nServerEvents.recipes(e => {})")]
    [InlineData("const R = java.lang.Runtime")]
    public void KubeJs_ObfuscatedOrIndirectUsages_AreCaught(string script)
        => Assert.NotEmpty(ContentValidate.KubeJsViolations(script));

    [Theory]
    [InlineData("ServerEvents.recipes(event => {\n  event.remove({ output: 'minecraft:stick' })\n  event.shaped('minecraft:diamond', ['##', '##'], { '#': 'minecraft:dirt' })\n})")]
    [InlineData("const Items = Java.loadClass('net.minecraft.world.item.Items')\nconsole.log('evaluation done')")]
    [InlineData("StartupEvents.registry('item', e => { e.create('demo_gem').displayName('\\u00a7b宝石') })")]
    [InlineData("ItemEvents.tooltip(e => { e.add('minecraft:diamond', 'processed') })")]
    public void KubeJs_OrdinaryScripts_Pass(string script)
        => Assert.Empty(ContentValidate.KubeJsViolations(script));

    [Fact]
    public async Task WriteKubeJs_RequiresKubeJsInstalled_AndWritesOnlyWhitelistedScripts()
    {
        Instance inst = NewInstance("kjs");
        ToolRunner runner = Runner(inst);
        const string script = "ServerEvents.recipes(event => {\n  event.remove({ output: 'minecraft:stick' })\n})";
        PyDict args = PyDict.Of("script_type", "server", "name", "no_sticks", "content", script);

        // 没装 KubeJS:拒绝;只放一个 id 不是 kubejs 的附属模组 jar 也不算装了
        FakeJar(Path.Combine(Root(inst), "mods"), "kubejs-addon-1.0.jar", "fabric.mod.json", "{\"id\":\"kubejs_addon\"}");
        Assert.Equal("", ContentTools.FindKubeJs(Path.Combine(Root(inst), "mods")));
        (ToolCall none, string noneText) = await Run(runner, "write_kubejs_script", args);
        Assert.Equal(ToolCallStatus.FAILED, none.status);
        Assert.Contains("没装 KubeJS", Msg(noneText));

        InstallKubeJs(inst);
        Assert.StartsWith("kubejs-fabric", ContentTools.FindKubeJs(Path.Combine(Root(inst), "mods")));
        (ToolCall ok, string okText) = await Run(runner, "write_kubejs_script", args);
        Assert.True(ok.status == ToolCallStatus.COMPLETED, okText);
        string target = Path.Combine(Root(inst), "kubejs", "server_scripts", "no_sticks.js");
        Assert.Equal(script + "\n", ReadNorm(target));

        // 已存在不带 update:拒;危险内容:拒且原文件不动;文件名 / 目录越界:拒
        Assert.Contains("update=true", Msg((await Run(runner, "write_kubejs_script", args)).Text));
        foreach ((PyDict bad, string expect) in new[]
                 {
                     (PyDict.Of("script_type", "server", "name", "no_sticks", "update", true, "content", "Java.loadClass('java.lang.Runtime').getRuntime().exec('rm -rf /')"), "不允许的用法"),
                     (PyDict.Of("script_type", "server", "name", "../../evil", "content", script), "name 只能用"),
                     (PyDict.Of("script_type", "server", "name", "a.b", "content", script), "name 只能用"),
                     (PyDict.Of("script_type", "data", "name", "x", "content", script), "script_type"),
                     (PyDict.Of("script_type", "server", "name", "big", "content", new string('a', ContentTools.MAX_SCRIPT_BYTES + 1)), "64 KB"),
                 })
        {
            (ToolCall call, string text) = await Run(runner, "write_kubejs_script", bad);
            Assert.Equal(ToolCallStatus.FAILED, call.status);
            Assert.Contains(expect, Msg(text));
        }
        Assert.Equal(script + "\n", ReadNorm(target));
        Assert.False(File.Exists(Path.Combine(Root(inst), "evil.js")));
        Assert.Equal(new[] { "no_sticks.js" }, Directory.GetFiles(Path.GetDirectoryName(target)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteKubeJs_AsksEveryTime_EvenIfTheCardReturnsAlways()
    {
        Instance inst = NewInstance("kjs-ask");
        InstallKubeJs(inst);
        int asked = 0;
        ToolRunner runner = Runner(inst, confirm: (_, _, _, _) =>
        {
            asked++;
            return Task.FromResult((true, true)); // 界面就算回了「本对话都允许」
        });
        for (int i = 0; i < 2; i++)
        {
            (ToolCall call, string text) = await Run(runner, "write_kubejs_script",
                PyDict.Of("script_type", "startup", "name", "s" + i, "content", "StartupEvents.init(e => {})"));
            Assert.True(call.status == ToolCallStatus.COMPLETED, text);
        }
        Assert.Equal(2, asked);
        Assert.False(runner.IsAllowedAlways("write_kubejs_script"));

        // 对照:普通写工具回 always 后第二次不再问
        NewWorld(inst);
        int dpAsked = 0;
        ToolRunner dp = Runner(inst, confirm: (_, _, _, _) =>
        {
            dpAsked++;
            return Task.FromResult((true, true));
        });
        for (int i = 0; i < 2; i++)
        {
            await Run(dp, "create_datapack", DatapackArgs("World1", "p" + i, "1.20.1", File1("function", "f", "say " + i)));
        }
        Assert.Equal(1, dpAsked);
    }

    // ───────────────────────────── 路径穿越 ─────────────────────────────

    [Fact]
    public async Task PathTraversal_IsRejectedEverywhere()
    {
        Instance inst = NewInstance("trav");
        NewWorld(inst);
        ToolRunner runner = Runner(inst);
        string recipe = ShapedOld;
        foreach ((PyDict args, string expect) in new[]
                 {
                     (DatapackArgs("World1", "../evil", "1.20.1", File1("recipe", "r", recipe)), "name"),
                     (DatapackArgs("World1", "..", "1.20.1", File1("recipe", "r", recipe)), "name"),
                     (DatapackArgs("../World1", "p", "1.20.1", File1("recipe", "r", recipe)), "world"),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "../../../escape", recipe)), ".."),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "/abs/x", recipe)), "相对路径"),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "a\\b", recipe)), "相对路径"),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "C:/x", recipe)), "相对路径"),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "r", recipe, "namespace", "../x")), "namespace"),
                     (DatapackArgs("World1", "p", "1.20.1", File1("recipe", "Big", recipe)), "小写"),
                     (DatapackArgs("World1", "con", "1.20.1", File1("recipe", "r", recipe)), "保留名"),
                     (DatapackArgs("Nope", "p", "1.20.1", File1("recipe", "r", recipe)), "存档不存在"),
                 })
        {
            (ToolCall call, string text) = await Run(runner, "create_datapack", args);
            Assert.Equal(ToolCallStatus.FAILED, call.status);
            Assert.Contains(expect, Msg(text));
        }
        (ToolCall rp, string rpText) = await Run(runner, "create_resourcepack", PyDict.Of("name", "R", "mc_version", "1.20.1",
            "files", new List<object> { File1("model", "../../../../x", "{\"parent\":\"minecraft:block/cube\"}") }));
        Assert.Equal(ToolCallStatus.FAILED, rp.status);
        Assert.Contains("..", Msg(rpText));
        (ToolCall lang, string langText) = await Run(runner, "create_resourcepack", PyDict.Of("name", "R", "mc_version", "1.20.1",
            "files", new List<object> { File1("lang", "../zh_cn", "{}") }));
        Assert.Equal(ToolCallStatus.FAILED, lang.status);
        Assert.Contains("语言代码", Msg(langText));
        Assert.False(Directory.Exists(Path.Combine(Root(inst), "saves", "World1", "datapacks")));
        Assert.False(Directory.Exists(Path.Combine(Root(inst), "resourcepacks", "R")));
    }

    private static bool TryLinkDir(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception)
        {
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using Process? p = Process.Start(psi);
                p?.WaitForExit(15000);
                return Directory.Exists(link);
            }
            catch (Exception)
            {
            }
        }
        return false;
    }

    [Fact]
    public async Task LinkedDatapacksDir_PointingOutside_IsRejected()
    {
        Instance inst = NewInstance("link");
        string world = NewWorld(inst);
        string outside = Path.Combine(Path.GetTempPath(), "tipcl-ct-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            if (!TryLinkDir(Path.Combine(world, "datapacks"), outside))
            {
                return; // 本机建不了链接:这条防线由 AiLogsFsTests 的同类用例另行覆盖
            }
            (ToolCall call, string text) = await Run(Runner(inst), "create_datapack", DatapackArgs("World1", "p", "1.20.1", File1("function", "f", "say hi")));
            Assert.Equal(ToolCallStatus.FAILED, call.status);
            Assert.Contains("越出", Msg(text));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(world, "datapacks")); } catch (Exception) { }
            try { Directory.Delete(outside, true); } catch (Exception) { }
        }
    }

    // ───────────────────────────── 确认链:预览 / 拒绝 / plan 档 ─────────────────────────────

    [Fact]
    public async Task ConfirmCard_GetsDiffPreview_RefusalWritesNothing_PlanModeNeverAsks()
    {
        Instance inst = NewInstance("confirm");
        NewWorld(inst);
        var previews = new List<List<string>>();
        var labels = new List<string>();
        bool allow = false;
        ToolRunner runner = Runner(inst, confirm: (_, label, _, preview) =>
        {
            labels.Add(label);
            previews.Add(Preview.Flatten(preview));
            return Task.FromResult((allow, false));
        });
        string fn = Path.Combine(Root(inst), "saves", "World1", "datapacks", "pv", "data", "demo", "functions", "f.mcfunction");
        PyDict args = DatapackArgs("World1", "pv", "1.20.1", File1("function", "f", "say old"));

        (ToolCall refused, _) = await Run(runner, "create_datapack", args);
        Assert.Equal(ToolCallStatus.DENIED, refused.status);
        Assert.False(Directory.Exists(Path.Combine(Root(inst), "saves", "World1", "datapacks", "pv")));
        Assert.Equal("生成数据包 pv", labels[0]);
        Assert.Contains("（新建文件）", previews[0]);
        Assert.Contains("+say old", previews[0]);
        Assert.Contains(previews[0], l => l.Contains("15"));
        Assert.True(AiUiText.LooksLikeDiff(previews[0]));

        allow = true;
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "create_datapack", args)).Call.status);
        Assert.Equal("say old\n", ReadNorm(fn));

        // 已存在、不带 update:卡片上就说会被拒;带 update:diff 是改前改后
        await Run(runner, "create_datapack", args);
        Assert.Contains(previews[^1], l => l.Contains("update=true"));
        PyDict upd = DatapackArgs("World1", "pv", "1.20.1", File1("function", "f", "say new"));
        upd["update"] = true;
        (ToolCall updated, string updatedText) = await Run(runner, "create_datapack", upd);
        Assert.True(updated.status == ToolCallStatus.COMPLETED, updatedText);
        Assert.Contains("-say old", previews[^1]);
        Assert.Contains("+say new", previews[^1]);
        Assert.DoesNotContain("+++ b/pack.mcmeta", previews[^1]);   // pack.mcmeta 没变且没传 description:不在改动里
        Assert.Equal("say new\n", ReadNorm(fn));

        // 「只看不动」档:直接拒,卡都不弹
        int before = labels.Count;
        ToolRunner plan = Runner(inst, "plan", confirm: (_, label, _, _) =>
        {
            labels.Add(label);
            return Task.FromResult((true, false));
        });
        upd["files"] = new List<object> { File1("function", "f", "say plan") };
        (ToolCall planCall, _) = await Run(plan, "create_datapack", upd);
        Assert.Equal(ToolCallStatus.DENIED, planCall.status);
        Assert.Equal(before, labels.Count);
        Assert.Equal("say new\n", ReadNorm(fn));
    }

    [Fact]
    public async Task UnchangedContent_WritesNothing()
    {
        Instance inst = NewInstance("same");
        NewWorld(inst);
        ToolRunner runner = Runner(inst);
        PyDict args = DatapackArgs("World1", "same", "1.21", File1("recipe", "r", ShapedMid.Replace("{\"item\":\"minecraft:dirt\"}", "{\"item\":\"minecraft:dirt\"}")));
        args["files"] = new List<object> { File1("function", "f", "say x") };
        await Run(runner, "create_datapack", args);
        args["update"] = true;
        (ToolCall again, string text) = await Run(runner, "create_datapack", args);
        Assert.True(again.status == ToolCallStatus.COMPLETED, text);
        Assert.Empty(Py.ListOr(Json(text).Get("written")));
        Assert.Empty(Tools.AffectedPaths("create_datapack", args, inst.name));
    }

    // ───────────────────────────── 检查点 ─────────────────────────────

    [Fact]
    public async Task AffectedPaths_FeedCheckpoint_SoRollbackRemovesNewAndRestoresEdited()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("ckpt");
        NewWorld(inst);
        InstallKubeJs(inst);
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        string pack = Path.Combine(Root(inst), "saves", "World1", "datapacks", "ck");
        string fn = Path.Combine(pack, "data", "demo", "function", "f.mcfunction");

        // 第一轮:新建数据包 + 新建脚本
        PyDict create = DatapackArgs("World1", "ck", "1.21", File1("function", "f", "say v1"));
        PyDict js = PyDict.Of("script_type", "server", "name", "a", "content", "ServerEvents.tick(e => {})");
        List<string> paths = Tools.AffectedPaths("create_datapack", create, inst.name).Concat(Tools.AffectedPaths("write_kubejs_script", js, inst.name)).ToList();
        Assert.Equal(3, paths.Count); // pack.mcmeta + f.mcfunction + a.js
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t1", paths).Get("ok")));
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "create_datapack", create)).Call.status);
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "write_kubejs_script", js)).Call.status);

        // 第二轮:改函数
        PyDict edit = DatapackArgs("World1", "ck", "1.21", File1("function", "f", "say v2"));
        edit["update"] = true;
        List<string> editPaths = Tools.AffectedPaths("create_datapack", edit, inst.name);
        Assert.Equal(AiFsTools.RealPath(fn), Assert.Single(editPaths));
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, "t2", editPaths).Get("ok")));
        Assert.Equal(ToolCallStatus.COMPLETED, (await Run(runner, "create_datapack", edit)).Call.status);
        Assert.Equal("say v2\n", ReadNorm(fn));

        PyDict r2 = Checkpoint.Rollback(chat, "t2");
        Assert.True(Py.Truthy(r2.Get("ok")), PyJson.Dumps(r2));
        Assert.Equal("say v1\n", ReadNorm(fn));

        PyDict r1 = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(r1.Get("ok")), PyJson.Dumps(r1));
        Assert.False(File.Exists(fn));
        Assert.False(File.Exists(Path.Combine(pack, "pack.mcmeta")));
        Assert.False(File.Exists(Path.Combine(Root(inst), "kubejs", "server_scripts", "a.js")));
    }

    // ───────────────────────────── validate_content ─────────────────────────────

    [Fact]
    public async Task ValidateContent_FindsCategorizedErrorsInLogs()
    {
        Instance inst = NewInstance("logs");
        string logs = Path.Combine(Root(inst), "logs");
        Directory.CreateDirectory(Path.Combine(logs, "kubejs"));
        File.WriteAllText(Path.Combine(logs, "latest.log"), string.Join("\n", new[]
        {
            "[12:00:00] [main/INFO]: Loading Minecraft 1.20.1",
            "[12:00:01] [Worker-Main-2/ERROR]: Parsing error loading recipe demo:dirt_to_diamond",
            "com.google.gson.JsonSyntaxException: Unknown item 'minecraft:dirtt'",
            "\tat net.minecraft.world.item.crafting.ShapedRecipe.itemFromJson(ShapedRecipe.java:1)",
            "[12:00:01] [Worker-Main-2/ERROR]: Parsing error loading recipe demo:dirt_to_diamond",
            "[12:00:02] [Server thread/ERROR]: Failed to load function demo:init",
            "[12:00:03] [Render thread/WARN]: Unable to load model: 'demo:block/ore#' referenced from: demo:ore#: java.io.FileNotFoundException: demo:models/block/ore.json",
            "[12:00:03] [Render thread/WARN]: Missing textures in model demo:ore#:",
            "[12:00:04] [main/ERROR]: KubeJS: Error loading script server_scripts/a.js",
            "[12:00:05] [main/INFO]: Everything else is fine",
        }));
        File.WriteAllText(Path.Combine(logs, "kubejs", "server.log"), "[12:00:06] [INFO ] Loaded script a.js\n[12:00:06] [ERR  ] Error in 'ServerEvents.recipes': ReferenceError: \"evnt\" is not defined.\n");
        ToolRunner runner = Runner(inst);

        PyDict all = Json((await Run(runner, "validate_content", PyDict.Of())).Text);
        Assert.True(Py.Truthy(all.Get("ok")));
        List<PyDict> issues = Py.ListOr(all.Get("issues")).Cast<PyDict>().ToList();
        PyDict counts = Py.D(all.Get("counts"));
        Assert.Equal(2L, counts.Get("datapack"));
        Assert.Equal(2L, counts.Get("resourcepack"));
        Assert.Equal(2L, counts.Get("kubejs"));
        PyDict recipe = issues.First(i => Py.S(i.Get("text")).Contains("Parsing error loading recipe"));
        Assert.Equal(2L, recipe.Get("repeat"));
        Assert.Contains("JsonSyntaxException", Py.S(Py.ListOr(recipe.Get("detail")).FirstOrDefault() ?? ""));
        Assert.Equal(2L, recipe.Get("line_no"));
        Assert.Contains("/reload", Py.S(all.Get("tip")));
        Assert.Contains("F3+T", Py.S(all.Get("tip")));
        Assert.DoesNotContain(issues, i => Py.S(i.Get("text")).Contains("Everything else"));

        PyDict onlyRp = Json((await Run(runner, "validate_content", PyDict.Of("kind", "resourcepack"))).Text);
        Assert.All(Py.ListOr(onlyRp.Get("issues")).Cast<PyDict>(), i => Assert.Equal("resourcepack", i.Get("category")));
        PyDict filtered = Json((await Run(runner, "validate_content", PyDict.Of("filter", "demo:init"))).Text);
        Assert.Equal("demo:init", Py.S(Py.ListOr(filtered.Get("issues")).Cast<PyDict>().Single().Get("text")).Split(' ').Last());

        Instance empty = NewInstance("nolog");
        PyDict none = Json((await Run(Runner(empty), "validate_content", PyDict.Of())).Text);
        Assert.Empty(Py.ListOr(none.Get("issues")));
        Assert.Contains("latest.log", Py.S(none.Get("message")));
    }

    [Fact]
    public void Classify_KnownMessages()
    {
        Assert.Equal("datapack", ContentTools.Classify("[x/ERROR]: Couldn't parse data file demo:a from demo:loot_tables/a.json"));
        Assert.Equal("datapack", ContentTools.Classify("[x/ERROR]: Couldn't load tag demo:shiny as it is missing following references: minecraft:nope"));
        Assert.Equal("resourcepack", ContentTools.Classify("[x/WARN]: Using missing texture, unable to load demo:textures/block/ore.png"));
        Assert.Equal("resourcepack", ContentTools.Classify("[x/WARN]: Exception loading blockstate definition: 'demo:blockstates/ore.json'"));
        Assert.Equal("resourcepack", ContentTools.Classify("[x/WARN]: Failed to load translations for zh_cn from pack file/MyRP"));
        Assert.Equal("kubejs", ContentTools.Classify("[x/ERROR]: [KubeJS] Script error in startup_scripts/a.js"));
        Assert.Equal("", ContentTools.Classify("[x/INFO]: Loaded 7 recipes"));
        Assert.Equal("", ContentTools.Classify("[x/INFO]: [KubeJS] Loaded 3 scripts with 0 errors"));
    }

    [Fact]
    public void SelfCheck_AllPass()
    {
        var failed = new List<string>();
        ContentTools.SelfCheck((name, ok) =>
        {
            if (!ok)
            {
                failed.Add(name);
            }
        });
        Assert.Empty(failed);
    }
}

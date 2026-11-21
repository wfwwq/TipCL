// AI3-content:内容制作包的遗留补齐 ——
//   1. create_datapack / create_resourcepack / write_kubejs_script 新建的目录在检查点撤回后不再留空壳
//      (只删本次新建、撤回后已经空了的目录;原来就有的、里面还有文件的都不动);
//   2. 引用的物品 / 方块 ID 命名空间既不是 minecraft 也不是已装模组时给 warnings(不拒绝);
//   3. KubeJS 静态检查加固:字符串拼接构造类名、\u / \x / 八进制转义、模板字符串、方括号动态属性访问、constructor 链、Function 别名,
//      每条正反例都有。
// 全部在 TestIsolation 指好的临时 TIPCL_HOME 里跑,检查点落在 AiServiceDirs 的临时目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using TipCL.Core.Launcher.Ai;
using Xunit;

namespace TipCL.Tests.Core;

public class AiContent3Tests : IDisposable
{
    private sealed class NullApi : ToolRunner.IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => Task.FromResult<T?>(default);
    }

    private readonly List<string> _cleanup = new();

    public AiContent3Tests() => ContentIds.ClearCache();

    public void Dispose()
    {
        ContentIds.ClearCache();
        foreach (string p in _cleanup)
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }
            catch (Exception)
            {
            }
        }
    }

    private Instance NewInstance(string tag)
    {
        Instance inst = Instances.CreateUniqueInstance("c3-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        _cleanup.Add(PPath.Abs(inst.path));
        return inst;
    }

    private static string Root(Instance inst) => PPath.Abs(inst.path);

    private static string NewWorld(Instance inst, string name = "World1", bool withDatapacksDir = true)
    {
        string dir = Path.Combine(Root(inst), "saves", name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "level.dat"), new byte[] { 1, 2, 3 });
        if (withDatapacksDir)
        {
            Directory.CreateDirectory(Path.Combine(dir, "datapacks"));
        }
        return dir;
    }

    private static ToolRunner Runner(Instance inst)
        => new(new NullApi())
        {
            PermissionMode = "default",
            Instance = inst.name,
            Confirm = (_, _, _, _) => Task.FromResult((true, false)),
        };

    private static async Task<(ToolCall Call, string Text)> Run(ToolRunner runner, string tool, PyDict args)
    {
        var call = new ToolCall("c-" + Guid.NewGuid().ToString("N").Substring(0, 6), tool, args);
        string text = await runner.RunAsync(call);
        return (call, text);
    }

    private static PyDict Json(string text) => (PyDict)PyJson.Loads(text);

    private static PyDict File1(string type, string path, string content, params object[] extra)
    {
        PyDict d = PyDict.Of("type", type, "path", path, "content", content);
        for (int i = 0; i + 1 < extra.Length; i += 2)
        {
            d[(string)extra[i]] = extra[i + 1];
        }
        return d;
    }

    private static PyDict Datapack(string world, string name, string mc, params PyDict[] files)
        => PyDict.Of("world", world, "name", name, "namespace", "demo", "mc_version", mc, "files", files.Cast<object>().ToList());

    private static void FabricJar(Instance inst, string file, string modId)
    {
        string dir = Path.Combine(Root(inst), "mods");
        Directory.CreateDirectory(dir);
        using ZipArchive zip = ZipFile.Open(Path.Combine(dir, file), ZipArchiveMode.Create);
        using var w = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open(), new UTF8Encoding(false));
        w.Write("{\"schemaVersion\":1,\"id\":\"" + modId + "\",\"version\":\"1.0\"}");
    }

    /// <summary>照 AiAgent.SnapshotForTool 的顺序做检查点:先记新目录名单,再做内容快照,然后执行。</summary>
    private static async Task<string> RunWithCheckpoint(ToolRunner runner, string chat, string turn, string tool, PyDict args, string instance)
    {
        ContentTools.SnapshotNewDirs(chat, turn, tool, args, instance);
        Assert.True(Py.Truthy(Checkpoint.Snapshot(chat, turn, Tools.AffectedPaths(tool, args, instance)).Get("ok")));
        (ToolCall call, string text) = await Run(runner, tool, args);
        Assert.True(call.status == ToolCallStatus.COMPLETED, text);
        return text;
    }

    // ───────────────────────────── 1. 撤回清理空目录 ─────────────────────────────

    [Fact]
    public async Task Rollback_NewDatapack_LeavesNoEmptyDirs_ButKeepsExistingOnes()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("rb");
        string world = NewWorld(inst);
        string datapacks = Path.Combine(world, "datapacks");
        Directory.CreateDirectory(Path.Combine(datapacks, "already_here"));   // 原来就有的(空)目录:不能删
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        PyDict args = Datapack("World1", "fresh", "1.21.4",
            File1("function", "a/b/c", "say hi"),
            File1("recipe", "r", "{\"type\":\"minecraft:crafting_shapeless\",\"ingredients\":[\"minecraft:dirt\"],\"result\":{\"id\":\"minecraft:diamond\"}}"));
        await RunWithCheckpoint(runner, chat, "t1", "create_datapack", args, inst.name);
        Assert.True(File.Exists(Path.Combine(datapacks, "fresh", "data", "demo", "function", "a", "b", "c.mcfunction")));

        PyDict r = Checkpoint.Rollback(chat, "t1");
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        Assert.False(Directory.Exists(Path.Combine(datapacks, "fresh")));          // 新建的包整个没了,不留 data/demo/... 空壳
        Assert.True(Directory.Exists(datapacks));                                 // datapacks 原来就有:留着
        Assert.True(Directory.Exists(Path.Combine(datapacks, "already_here")));   // 别的目录不碰
        Assert.True(File.Exists(Path.Combine(world, "level.dat")));
    }

    [Fact]
    public async Task Rollback_DeletesNewlyCreatedParentDir_WhenItDidNotExistBefore()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("rbp");
        string world = NewWorld(inst, withDatapacksDir: false);
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        await RunWithCheckpoint(runner, chat, "t1", "create_datapack", Datapack("World1", "p", "1.20.1", File1("function", "f", "say x")), inst.name);
        Assert.True(Directory.Exists(Path.Combine(world, "datapacks", "p")));
        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(Directory.Exists(Path.Combine(world, "datapacks")));   // 这一步才建出来的 datapacks/ 也一起清掉
        Assert.True(Directory.Exists(world));                               // 存档本身不动
    }

    [Fact]
    public async Task Rollback_UpdateExistingPack_RemovesOnlyTheNewNamespaceDir()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("rbu");
        string world = NewWorld(inst);
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        string pack = Path.Combine(world, "datapacks", "p");
        (ToolCall first, string firstText) = await Run(runner, "create_datapack", Datapack("World1", "p", "1.21", File1("function", "f", "say 1")));
        Assert.True(first.status == ToolCallStatus.COMPLETED, firstText);

        PyDict update = Datapack("World1", "p", "1.21", File1("function", "g", "say 2", "namespace", "other"));
        update["update"] = true;
        await RunWithCheckpoint(runner, chat, "t2", "create_datapack", update, inst.name);
        Assert.True(File.Exists(Path.Combine(pack, "data", "other", "function", "g.mcfunction")));

        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t2").Get("ok")));
        Assert.False(Directory.Exists(Path.Combine(pack, "data", "other")));                    // 这一步新加的命名空间目录清掉
        Assert.True(File.Exists(Path.Combine(pack, "data", "demo", "function", "f.mcfunction"))); // 原来的包原样
        Assert.True(File.Exists(Path.Combine(pack, "pack.mcmeta")));
    }

    [Fact]
    public async Task Rollback_KeepsNewDir_IfUserPutAFileThereAfterwards()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("rbk");
        string world = NewWorld(inst);
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        string pack = Path.Combine(world, "datapacks", "p");
        await RunWithCheckpoint(runner, chat, "t1", "create_datapack", Datapack("World1", "p", "1.21", File1("function", "f", "say 1")), inst.name);
        File.WriteAllText(Path.Combine(pack, "README.txt"), "user notes");   // 撤回前用户自己往新目录里放了文件

        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(File.Exists(Path.Combine(pack, "pack.mcmeta")));                // 本次写的文件照样撤掉
        Assert.True(File.Exists(Path.Combine(pack, "README.txt")));                  // 用户的文件不动,所以目录也留着
        Assert.False(Directory.Exists(Path.Combine(pack, "data")));                  // 但已经空了的新子目录照样清掉
    }

    [Fact]
    public async Task Rollback_NewResourcepackAndKubeJsScript_LeaveNoEmptyDirs()
    {
        using var dirs = new AiServiceDirs();
        Instance inst = NewInstance("rbr");
        FabricJar(inst, "kubejs-fabric-2001.6.5-build.16.jar", "kubejs");
        ToolRunner runner = Runner(inst);
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        string packs = Path.Combine(Root(inst), "resourcepacks");
        bool packsExisted = Directory.Exists(packs);
        bool kubeExisted = Directory.Exists(Path.Combine(Root(inst), "kubejs"));
        PyDict rp = PyDict.Of("name", "rp", "mc_version", "1.20.1",
            "files", new List<object> { File1("lang", "zh_cn", "{\"item.minecraft.diamond\":\"钻石\"}") });
        await RunWithCheckpoint(runner, chat, "t1", "create_resourcepack", rp, inst.name);
        PyDict js = PyDict.Of("script_type", "server", "name", "a", "content", "ServerEvents.tick(e => {})");
        await RunWithCheckpoint(runner, chat, "t1", "write_kubejs_script", js, inst.name);
        Assert.True(File.Exists(Path.Combine(packs, "rp", "assets", "minecraft", "lang", "zh_cn.json")));

        Assert.True(Py.Truthy(Checkpoint.Rollback(chat, "t1").Get("ok")));
        Assert.False(Directory.Exists(Path.Combine(packs, "rp")));
        Assert.Equal(packsExisted, Directory.Exists(packs));
        Assert.False(Directory.Exists(Path.Combine(Root(inst), "kubejs", "server_scripts")));
        Assert.Equal(kubeExisted, Directory.Exists(Path.Combine(Root(inst), "kubejs")));
        Assert.True(Directory.Exists(Path.Combine(Root(inst), "mods")));
    }

    [Fact]
    public void SnapshotNewDirs_OnlyNamedFreshEmptyDirsAreRemoved()
    {
        using var dirs = new AiServiceDirs();
        string parent = Path.Combine(dirs.Root, "parent");
        Directory.CreateDirectory(Path.Combine(parent, "old"));
        string chat = "chat-" + Guid.NewGuid().ToString("N");
        // old 已存在:自动去掉,不记;new_a / new_b 记下
        PyDict snap = Checkpoint.SnapshotNewDirs(chat, "t", parent, new[] { "old", "new_a", "new_b", "../escape" });
        Assert.True(Py.Truthy(snap.Get("ok")));
        Assert.Equal(2L, snap.Get("names"));
        Directory.CreateDirectory(Path.Combine(parent, "new_a", "x", "y"));          // 只有空子目录 → 删
        Directory.CreateDirectory(Path.Combine(parent, "new_b"));
        File.WriteAllText(Path.Combine(parent, "new_b", "keep.txt"), "1");          // 有文件 → 不删
        Directory.CreateDirectory(Path.Combine(parent, "unnamed"));                 // 没点名(比如游戏自己建的)→ 不删
        PyDict r = Checkpoint.Rollback(chat, "t");
        Assert.True(Py.Truthy(r.Get("ok")), PyJson.Dumps(r));
        Assert.False(Directory.Exists(Path.Combine(parent, "new_a")));
        Assert.True(File.Exists(Path.Combine(parent, "new_b", "keep.txt")));
        Assert.True(Directory.Exists(Path.Combine(parent, "unnamed")));
        Assert.True(Directory.Exists(Path.Combine(parent, "old")));
        // 全都已存在时不写流水
        Assert.Equal(-1L, Checkpoint.SnapshotNewDirs(chat, "t2", parent, new[] { "old" }).Get("seq"));
    }

    [Fact]
    public void NewDirWatches_PointAtTheOutermostDirThatWillBeCreated()
    {
        Instance inst = NewInstance("watch");
        string world = NewWorld(inst);
        List<(string Parent, string Child)> w = ContentTools.NewDirWatches("create_datapack",
            Datapack("World1", "pk", "1.21", File1("function", "f", "say x")), inst.name);
        (string parent, string child) = Assert.Single(w);
        Assert.Equal(AiFsTools.RealPath(Path.Combine(world, "datapacks")), AiFsTools.RealPath(parent));
        Assert.Equal("pk", child);
        // 校验不过的调用不记(执行时也会被拒)
        Assert.Empty(ContentTools.NewDirWatches("create_datapack", Datapack("World1", "pk", "1.99", File1("function", "f", "say x")), inst.name));
        Assert.Empty(ContentTools.NewDirWatches("validate_content", new PyDict(), inst.name));
    }

    // ───────────────────────────── 2. ID 命名空间提醒 ─────────────────────────────

    private const string CreateRecipe = """{"type":"minecraft:crafting_shapeless","ingredients":["create:andesite_alloy","#c:ingots/iron","#demo:my_tag"],"result":{"id":"create:wrench"}}""";

    [Fact]
    public async Task Datapack_UnknownNamespace_WarnsButStillWrites()
    {
        Instance inst = NewInstance("idw");
        string world = NewWorld(inst);
        ToolRunner runner = Runner(inst);
        PyDict args = Datapack("World1", "ids", "1.21.4",
            File1("recipe", "wrench", CreateRecipe),
            File1("loot_table", "blocks/x", """{"pools":[{"rolls":1,"entries":[{"type":"minecraft:alternatives","children":[{"type":"minecraft:item","name":"mekanism:steel_ingot"},{"type":"minecraft:item","name":"minecraft:iron_ingot"}]}]}]}"""),
            File1("advancement", "a", """{"criteria":{"x":{"trigger":"minecraft:tick"}},"display":{"icon":{"id":"botania:twig_wand"},"title":"t","description":"d"}}"""),
            File1("tag", "t", """{"values":["minecraft:stone","thermal:copper_block","#c:storage_blocks"]}""", "tag_type", "block"));
        (ToolCall call, string text) = await Run(runner, "create_datapack", args);
        Assert.True(call.status == ToolCallStatus.COMPLETED, text);   // 不拒绝
        Assert.True(File.Exists(Path.Combine(world, "datapacks", "ids", "data", "demo", "recipe", "wrench.json")));
        List<string> warnings = Py.ListOr(Json(text).Get("warnings")).Select(x => Py.S(x)).ToList();
        foreach (string ns in new[] { "create", "mekanism", "botania", "thermal" })
        {
            Assert.Contains(warnings, w => w.Contains("命名空间 " + ns + " "));
        }
        Assert.Contains(warnings, w => w.Contains("create:wrench") && w.Contains("create:andesite_alloy"));   // 同一命名空间合并成一条,列出引用
        Assert.DoesNotContain(warnings, w => w.Contains("命名空间 minecraft ") || w.Contains("命名空间 c ") || w.Contains("命名空间 demo "));
        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public async Task Datapack_InstalledModIds_AreNotWarned_DisabledJarsDoNotCount()
    {
        Instance inst = NewInstance("idok");
        NewWorld(inst);
        FabricJar(inst, "create-fabric-0.5.1.jar", "create");
        FabricJar(inst, "botania-1.0.jar.disabled", "botania");   // 禁用的不算装了
        ToolRunner runner = Runner(inst);
        PyDict args = Datapack("World1", "ok", "1.21.4",
            File1("recipe", "wrench", CreateRecipe),
            File1("advancement", "a", """{"criteria":{"x":{"trigger":"minecraft:tick"}},"display":{"icon":{"id":"botania:twig_wand"},"title":"t","description":"d"}}"""));
        (ToolCall call, string text) = await Run(runner, "create_datapack", args);
        Assert.True(call.status == ToolCallStatus.COMPLETED, text);
        List<string> warnings = Py.ListOr(Json(text).Get("warnings")).Select(x => Py.S(x)).ToList();
        Assert.Contains("命名空间 botania ", Assert.Single(warnings));
        Assert.Contains("create", ContentIds.InstalledModIds(Path.Combine(Root(inst), "mods")));
        Assert.DoesNotContain("botania", ContentIds.InstalledModIds(Path.Combine(Root(inst), "mods")));

        // 全部有来头时结果里不带 warnings
        PyDict clean = Datapack("World1", "clean", "1.21.4", File1("recipe", "r", CreateRecipe));
        (ToolCall c2, string t2) = await Run(runner, "create_datapack", clean);
        Assert.True(c2.status == ToolCallStatus.COMPLETED, t2);
        Assert.Null(Json(t2).Get("warnings"));
    }

    [Fact]
    public async Task Datapack_LegacyRecipeFormat_AndModdedRecipeType_AreChecked()
    {
        Instance inst = NewInstance("idleg");
        NewWorld(inst);
        ToolRunner runner = Runner(inst);
        PyDict args = Datapack("World1", "leg", "1.20.1",
            File1("recipe", "shaped", """{"type":"minecraft:crafting_shaped","pattern":["#"],"key":{"#":{"item":"ae2:certus_quartz_crystal"}},"result":{"item":"minecraft:diamond"}}"""),
            File1("recipe", "mix", """{"type":"farmersdelight:cooking","ingredients":[{"item":"minecraft:carrot"}],"result":{"item":"minecraft:bread"}}"""));
        (ToolCall call, string text) = await Run(runner, "create_datapack", args);
        Assert.True(call.status == ToolCallStatus.COMPLETED, text);
        List<string> warnings = Py.ListOr(Json(text).Get("warnings")).Select(x => Py.S(x)).ToList();
        Assert.Contains(warnings, w => w.Contains("命名空间 ae2 ") && w.Contains("物品"));
        Assert.Contains(warnings, w => w.Contains("命名空间 farmersdelight ") && w.Contains("配方类型"));
    }

    [Fact]
    public async Task Resourcepack_ModelAndBlockstateRefs_OwnNamespaceIsFine()
    {
        Instance inst = NewInstance("idrp");
        ToolRunner runner = Runner(inst);
        PyDict args = PyDict.Of("name", "rp", "mc_version", "1.20.1", "namespace", "mypack", "files", new List<object>
        {
            File1("model", "block/a", """{"parent":"minecraft:block/cube_all","textures":{"all":"mypack:block/a"}}"""),
            File1("model", "block/b", """{"parent":"othermod:block/base","textures":{"all":"#all","side":"texmod:block/side"}}"""),
            File1("blockstate", "a", """{"variants":{"":{"model":"mypack:block/a"}}}"""),
            File1("blockstate", "stone", """{"variants":{"":{"model":"minecraft:block/stone"}}}""", "namespace", "minecraft"),
        });
        (ToolCall call, string text) = await Run(runner, "create_resourcepack", args);
        Assert.True(call.status == ToolCallStatus.COMPLETED, text);
        List<string> warnings = Py.ListOr(Json(text).Get("warnings")).Select(x => Py.S(x)).ToList();
        Assert.Contains(warnings, w => w.Contains("命名空间 othermod ") && w.Contains("模型"));
        Assert.Contains(warnings, w => w.Contains("命名空间 texmod ") && w.Contains("贴图"));
        Assert.Contains(warnings, w => w.Contains("命名空间 mypack ") && w.Contains("方块 mypack:a"));   // 方块 ID 不能靠资源包自己定义
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void Preview_ListsSuspiciousNamespaces()
    {
        Instance inst = NewInstance("idpv");
        NewWorld(inst);
        PyDict preview = ContentPreview.ChangePreview("create_datapack", Datapack("World1", "pv", "1.21.4", File1("recipe", "r", CreateRecipe)), inst.name)!;
        List<string> lines = Py.ListOr(preview.Get("lines")).Select(x => Py.S(x)).ToList();
        Assert.Contains(lines, l => l.Contains("create") && l.Contains(I18nCore.Tr("提醒：这些命名空间既不是 minecraft 也不是已装模组，可能拼错了或模组没装（不影响写入）：{list}").Split('{')[0]));
    }

    [Fact]
    public void ContentIds_Rules()
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        var own = new HashSet<string>(StringComparer.Ordinal) { "mypack" };
        Assert.Equal("minecraft", ContentIds.NamespaceOf("stone"));
        Assert.Equal("c", ContentIds.NamespaceOf("#c:ingots"));
        Assert.True(ContentIds.Known("item", "minecraft", none, own));
        Assert.False(ContentIds.Known("item", "mypack", none, own));     // 物品不能靠包自己的命名空间
        Assert.True(ContentIds.Known("tag", "mypack", none, own));       // 标签可以
        Assert.True(ContentIds.Known("tag", "forge", none, own));
        Assert.True(ContentIds.Known("texture", "mypack", none, own));
        Assert.False(ContentIds.Known("texture", "other", none, own));
        Assert.True(ContentIds.Known("item", "create", new HashSet<string> { "create" }, own));
        // 超过上限时折成一句
        var many = Enumerable.Range(0, ContentIds.MAX_WARNINGS + 3).Select(i => new ContentIds.Ref("ns" + i + ":x", "item", "f")).ToList();
        List<string> w = ContentIds.Warnings(many, none, own);
        Assert.Equal(ContentIds.MAX_WARNINGS + 1, w.Count);
        Assert.Contains("3", w[^1]);
    }

    // ───────────────────────────── 3. KubeJS 静态检查加固 ─────────────────────────────

    [Theory]
    // 字符串拼接构造类名
    [InlineData("let cls = 'java.ne' + suffix", "字符串拼接构造 Java 类名")]
    [InlineData("const p = prefix + \".lang.Runtime\"", "字符串拼接构造 Java 类名")]
    [InlineData("const n = 'ja' + 'va.n' + 'et.Socket'", "java.net")]                  // 常量拼接折叠后再查
    [InlineData("const n = 'java.lang.Run' +\n  'time'", "java.lang.Runtime")]         // 跨行拼接
    // 转义
    [InlineData("const k = '\\x65val'", "转义")]
    [InlineData("const k = '\\x65val'", "eval")]
    [InlineData("const k = '\\u0072equire'", "转义")]
    [InlineData("const k = '\\u{6a}ava.io.File'", "转义")]
    [InlineData("const k = '\\145val'", "eval")]                                      // 八进制
    // 模板字符串
    [InlineData("const s = `java.${'io'}.File`", "模板字符串")]
    [InlineData("String.raw`x`", "模板字符串")]
    // 方括号动态属性访问
    [InlineData("Java['loadClass']('java.util.List')", "方括号")]
    [InlineData("const f = this['ev' + 'al']", "方括号")]
    [InlineData("global[name]()", "方括号")]
    [InlineData("const L = Java\n['loadClass']", "方括号")]                            // ASI 跨行
    [InlineData("foo()\n['x'].forEach(f)", "方括号")]
    // constructor 链
    [InlineData("''.constructor.constructor('return this')()", "constructor")]
    [InlineData("const p = obj.__proto__", "constructor")]
    [InlineData("Object.getPrototypeOf(x)", "constructor")]
    // Function 的各种拿法
    [InlineData("const F = Function; F('x')", "Function")]
    [InlineData("Function.prototype.call", "Function")]
    [InlineData("[].map.call(xs, Function)", "Function")]
    [InlineData("return Function", "Function")]
    public void KubeJs_HardenedRules_CatchBypasses(string script, string expectRule)
    {
        List<string> hits = ContentValidate.KubeJsViolations(script);
        Assert.Contains(hits, h => h.Contains(expectRule, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("const list = ['a', 'b']\nlist.forEach(x => console.log(x))")]
    [InlineData("// Function to tweak recipes\nServerEvents.recipes(e => { e.remove({ id: 'minecraft:stick' }) })")]
    [InlineData("ServerEvents.recipes(e => {\n  e.shaped('minecraft:diamond', [\n    'AB',\n    'BA'\n  ], { A: 'minecraft:dirt', B: 'minecraft:stone' })\n})")]
    [InlineData("function pick(xs) { return ['a', 'b'] }")]
    [InlineData("const key = 'item.mymod.' + name\nItemEvents.tooltip(e => e.add(key, Text.translate(key)))")]
    [InlineData("e.add('minecraft:diamond', '\\u00a7b闪亮的\\u00a7r')")]                 // 颜色符这类非 ASCII 转义照常放行
    [InlineData("const colors = { red: 1 }\nconsole.log(colors.red)")]
    [InlineData("const s = \"it's fine\"\nconsole.log(s + ' ok')")]
    [InlineData("for (let i = 0; i < xs.length; i++) { console.log(xs[i]) }")]
    public void KubeJs_HardenedRules_DoNotFlagOrdinaryScripts(string script)
        => Assert.Empty(ContentValidate.KubeJsViolations(script));

    [Fact]
    public void KubeJs_LineNumbersStayRight_AfterUnescapeAndFold()
    {
        string script = "const a = 'x' +\n  'y'\nconsole.log(a)\nconst k = '\\x65val'";
        List<string> hits = ContentValidate.KubeJsViolations(script);
        Assert.Contains(hits, h => h.StartsWith("第 4 行", StringComparison.Ordinal) && h.Contains("eval"));
        Assert.Equal("'xy'\n\nb", ContentValidate.FoldConstantConcat("'x' +\n 'y'\nb"));
        Assert.Equal("eval A §", ContentValidate.UnescapeUnicode("\\x65val \\101 \\u00a7"));
    }

    [Fact]
    public async Task WriteKubeJs_BypassAttempt_IsRejected_AndStillHighRiskEveryTime()
    {
        Instance inst = NewInstance("kjs");
        FabricJar(inst, "kubejs-fabric-2001.6.5-build.16.jar", "kubejs");
        int asked = 0;
        var runner = new ToolRunner(new NullApi())
        {
            PermissionMode = "default",
            Instance = inst.name,
            Confirm = (_, _, _, _) => { asked++; return Task.FromResult((true, true)); },
        };
        PyDict bad = PyDict.Of("script_type", "server", "name", "evil", "content", "const L = Java['loadClass']\nL('java.n' + 'et.URL')");
        (ToolCall call, string text) = await Run(runner, "write_kubejs_script", bad);
        Assert.Equal(ToolCallStatus.FAILED, call.status);
        Assert.False(File.Exists(Path.Combine(Root(inst), "kubejs", "server_scripts", "evil.js")));
        Assert.Equal("high", ToolMeta.Get("write_kubejs_script")!.risk);
        Assert.False(ToolRunner.AllowsAlways("write_kubejs_script"));
        PyDict ok = PyDict.Of("script_type", "server", "name", "fine", "content", "ServerEvents.tick(e => {})");
        await Run(runner, "write_kubejs_script", ok);
        ok["update"] = true;
        ok["content"] = "ServerEvents.tick(e => { })";
        await Run(runner, "write_kubejs_script", ok);
        Assert.True(asked >= 2);   // 卡片回 always,下一次照样问
    }
}

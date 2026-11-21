// AI3-stability:改进程级全局状态的测试类一律串行。
//
// 背景:Checkpoint.CheckpointsDir / Artifacts.ArtifactsDir / Store.SessionsDir / PageLayoutStore.DirOverride / Config.CONFIG
// 这些都是进程级静态量。xunit 默认「不同测试类并行」,两个类同时改同一个静态量会互相踩:A 的快照写进 A 目录,
// 同一时刻 B 把根目录换成 B 的,A 回滚时去 B 里找流水 —— 偶发红,而且红在无辜的用例上(progress/AI2-stability.md §5 第 1 条)。
//
// 做法:
//   1. 本文件定义串行集合 GlobalStateCollection(DisableParallelization = true:集合里的类彼此串行,
//      并且整个集合在所有可并行的测试跑完之后才跑,不会与任何别的类同时执行)。
//   2. GlobalStateGuardTests 扫测试程序集的 IL:凡是(直接或经测试程序集里的辅助方法间接)
//        * 给产品程序集(TipCL.Core / TipCL.App)的静态字段赋值、调静态属性的 setter;
//        * 调 Utils.SetRoot、LauncherConfig.Set / Update / Save / Load、Config.Reset、Environment.SetEnvironmentVariable;
//        * 改 Config.CONFIG.data(取了 data 又调 PyDict 的写方法);
//      的测试类,必须挂在某个 DisableParallelization 的集合上(本集合,或已有的「TipCL 真机交互回归」「AI2 DNS 注入」)。
//      新加的测试类漏挂会直接红,并列出类名与第一处改动点。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace TipCL.Tests;

/// <summary>改进程级全局目录 / 静态配置的测试类挂这个集合(串行,且不与任何并行类同时跑)。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateCollection
{
    public const string Name = "全局目录与静态配置";
}

public class GlobalStateGuardTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    /// <summary>显式算「改全局」的方法(类型名::方法名)。</summary>
    private static readonly HashSet<string> MutatorMethods = new(StringComparer.Ordinal)
    {
        "Utils::SetRoot",
        "LauncherConfig::Set",
        "LauncherConfig::Update",
        "LauncherConfig::Save",
        "LauncherConfig::Load",
        "Config::Reset",
        "Environment::SetEnvironmentVariable",
        // 产品里自己临时换全局目录的入口(AI 自检会把 Checkpoint / Artifacts / Trace / Store / Hooks 的目录指到临时目录再换回来)
        "AiSelfTest::VerifyAsync",
        "AiSelfTestCli::Run",
    };

    private static readonly HashSet<string> PyDictWriters = new(StringComparer.Ordinal) { "set_Item", "Remove", "Clear", "Add", "Pop", "SetDefault", "Update" };

    private static bool IsProduct(Assembly a)
    {
        string n = a.GetName().Name ?? "";
        return n.StartsWith("TipCL.", StringComparison.Ordinal) && !n.StartsWith("TipCL.Tests", StringComparison.Ordinal);
    }

    private static IEnumerable<(OpCode Op, int Token)> Walk(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            short v = il[i];
            if (v == 0xFE && i + 1 < il.Length)
            {
                v = unchecked((short)(0xFE00 | il[i + 1]));
                i += 2;
            }
            else
            {
                i += 1;
            }
            if (!OpCodesByValue.TryGetValue(v, out OpCode op))
            {
                yield break;
            }
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            int token = size == 4 && op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType
                ? BitConverter.ToInt32(il, i)
                : 0;
            yield return (op, token);
            i += size;
        }
    }

    private static IEnumerable<MethodBase> MethodsOf(Type t)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (MethodInfo m in t.GetMethods(all))
        {
            yield return m;
        }
        foreach (ConstructorInfo c in t.GetConstructors(all))
        {
            yield return c;
        }
    }

    /// <summary>直接改全局的方法 → 第一处改动点的说明;测试程序集里方法之间的调用边。</summary>
    private static (Dictionary<MethodBase, string> Direct, Dictionary<MethodBase, List<MethodBase>> Calls) Analyze(Assembly asm)
    {
        var direct = new Dictionary<MethodBase, string>();
        var calls = new Dictionary<MethodBase, List<MethodBase>>();
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
        }
        foreach (Type t in types)
        {
            foreach (MethodBase m in MethodsOf(t))
            {
                byte[]? il;
                try
                {
                    il = m.GetMethodBody()?.GetILAsByteArray();
                }
                catch (Exception)
                {
                    il = null;
                }
                if (il == null)
                {
                    continue;
                }
                Type[]? typeArgs = t.IsGenericType ? t.GetGenericArguments() : null;
                Type[]? methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
                var edges = new List<MethodBase>();
                bool touchesConfigData = false;
                bool writesPyDict = false;
                string? hit = null;
                foreach ((OpCode op, int token) in Walk(il))
                {
                    if (token == 0)
                    {
                        continue;
                    }
                    MemberInfo? member;
                    try
                    {
                        member = m.Module.ResolveMember(token, typeArgs, methodArgs);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (member is FieldInfo f)
                    {
                        if (op == OpCodes.Stsfld && f.IsStatic && !f.IsLiteral && f.DeclaringType != null && IsProduct(f.DeclaringType.Assembly))
                        {
                            hit ??= "stsfld " + f.DeclaringType.Name + "." + f.Name;
                        }
                        if (f.Name == "data" && f.DeclaringType?.Name == "LauncherConfig")
                        {
                            touchesConfigData = true;
                        }
                        continue;
                    }
                    if (member is not MethodBase callee || callee.DeclaringType == null)
                    {
                        continue;
                    }
                    string key = callee.DeclaringType.Name + "::" + callee.Name;
                    bool product = IsProduct(callee.DeclaringType.Assembly);
                    if (product && callee.IsStatic && callee.IsSpecialName && callee.Name.StartsWith("set_", StringComparison.Ordinal))
                    {
                        hit ??= "set " + callee.DeclaringType.Name + "." + callee.Name.Substring(4);
                    }
                    else if (MutatorMethods.Contains(key))
                    {
                        hit ??= "call " + key;
                    }
                    if (callee.DeclaringType.Name == "PyDict" && PyDictWriters.Contains(callee.Name))
                    {
                        writesPyDict = true;
                    }
                    if (callee.DeclaringType.Assembly == asm)
                    {
                        edges.Add(callee is MethodInfo mi && mi.IsGenericMethod && !mi.IsGenericMethodDefinition ? mi.GetGenericMethodDefinition() : callee);
                    }
                }
                if (hit == null && touchesConfigData && writesPyDict)
                {
                    hit = "write Config.CONFIG.data";
                }
                if (hit != null)
                {
                    direct[m] = t.FullName + "." + m.Name + ": " + hit;
                }
                calls[m] = edges;
                // async / 迭代器方法的方法体在编译器生成的状态机里:补一条「存根 → MoveNext」的边
                foreach (StateMachineAttribute sm in m.GetCustomAttributes<StateMachineAttribute>())
                {
                    MethodInfo? move = sm.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (move != null)
                    {
                        edges.Add(move);
                    }
                }
            }
        }
        return (direct, calls);
    }

    /// <summary>测试类(有 [Fact]/[Theory])→ 它(含嵌套类型、闭包、状态机、测试程序集里的基类)用到的全部方法。</summary>
    private static IEnumerable<MethodBase> Closure(Type cls)
    {
        var seen = new HashSet<Type>();
        var stack = new Stack<Type>();
        for (Type? b = cls; b != null && b.Assembly == cls.Assembly; b = b.BaseType)
        {
            stack.Push(b);
        }
        while (stack.Count > 0)
        {
            Type t = stack.Pop();
            if (!seen.Add(t))
            {
                continue;
            }
            foreach (MethodBase m in MethodsOf(t))
            {
                yield return m;
            }
            foreach (Type n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                stack.Push(n);
            }
        }
    }

    private static bool IsTestClass(Type t)
        => t.IsClass && !t.IsAbstract && t.DeclaringType == null
           && t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
               .Any(m => m.GetCustomAttributes().Any(a => a is FactAttribute));

    /// <summary>所有改全局状态的测试类 → 第一处改动点(直接或间接)。</summary>
    internal static Dictionary<Type, string> MutatingTestClasses()
    {
        Assembly asm = typeof(GlobalStateGuardTests).Assembly;
        (Dictionary<MethodBase, string> direct, Dictionary<MethodBase, List<MethodBase>> calls) = Analyze(asm);
        // 不动点:调用了「会改全局」的测试程序集方法,自己也算
        var why = new Dictionary<MethodBase, string>(direct);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((MethodBase m, List<MethodBase> edges) in calls)
            {
                if (why.ContainsKey(m))
                {
                    continue;
                }
                MethodBase? via = edges.FirstOrDefault(e => why.ContainsKey(e));
                if (via != null)
                {
                    why[m] = why[via];
                    changed = true;
                }
            }
        }
        var result = new Dictionary<Type, string>();
        foreach (Type t in asm.GetTypes().Where(IsTestClass))
        {
            MethodBase? first = Closure(t).FirstOrDefault(m => why.ContainsKey(m));
            if (first != null)
            {
                result[t] = why[first];
            }
        }
        return result;
    }

    /// <summary>不并行的集合名(读 [CollectionDefinition(..., DisableParallelization = true)])。</summary>
    internal static HashSet<string> SerialCollections()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type t in typeof(GlobalStateGuardTests).Assembly.GetTypes())
        {
            foreach (CustomAttributeData a in t.GetCustomAttributesData().Where(a => a.AttributeType == typeof(CollectionDefinitionAttribute)))
            {
                bool serial = a.NamedArguments.Any(n => n.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization) && n.TypedValue.Value is true);
                if (serial && a.ConstructorArguments.Count > 0 && a.ConstructorArguments[0].Value is string name)
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static string? CollectionOf(Type t)
        => t.GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(CollectionAttribute) && a.ConstructorArguments.Count > 0)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .FirstOrDefault();

    [Fact]
    public void ClassesThatMutateGlobalState_AreInASerialCollection()
    {
        HashSet<string> serial = SerialCollections();
        Assert.Contains(GlobalStateCollection.Name, serial);
        Dictionary<Type, string> mutating = MutatingTestClasses();
        // 自检:已知会改全局目录的类一定要被扫出来(否则就是扫描本身失灵了)
        Assert.Contains(mutating.Keys, t => t.Name == "CheckpointTests");
        Assert.Contains(mutating.Keys, t => t.Name == "PageLayoutTests");
        List<string> offenders = mutating
            .Where(kv => CollectionOf(kv.Key) is not { } c || !serial.Contains(c))
            .Select(kv => kv.Key.FullName + "  ←  " + kv.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        // 诊断:设了 TIPCL_GLOBAL_STATE_DUMP 就把全部「改全局」的类与所在集合写到那个文件(测试输出太长时会被截断)
        string? dump = Environment.GetEnvironmentVariable("TIPCL_GLOBAL_STATE_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            System.IO.File.WriteAllLines(dump, mutating
                .Select(kv => kv.Key.FullName + "\t" + (CollectionOf(kv.Key) ?? "-") + "\t" + kv.Value)
                .OrderBy(s => s, StringComparer.Ordinal));
        }
        Assert.True(offenders.Count == 0,
            "下列测试类改了进程级全局状态,却没挂在串行集合上(请加 [Collection(GlobalStateCollection.Name)]):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void SerialCollection_ActuallyDisablesParallelization()
    {
        Assert.Contains("TipCL 真机交互回归", SerialCollections());
        Assert.Contains(GlobalStateCollection.Name, SerialCollections());
        // 本类只读反射,不改全局:自己不在扫描结果里
        Assert.DoesNotContain(MutatingTestClasses().Keys, t => t == typeof(GlobalStateGuardTests));
    }
}

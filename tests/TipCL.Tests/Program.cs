using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// 进程内测试执行器 —— 本机 DSH 沙箱里 <c>dotnet test</c> 的替代验证通道(见 DECISIONS.md D-05)。
///
/// 背景:本沙箱把命令跑在 Low 完整性级别,并且禁止跨进程拿句柄
/// (实测:子进程对父进程/兄弟进程设 Process.EnableRaisingEvents 一律 Access is denied)。
/// VSTest 的 testhost 一启动就做这件事,所以必然以 Win32Exception(5) 挂掉,
/// <c>dotnet test</c> 在本沙箱不可用 —— 与工程配置无关。
///
/// 这个执行器不跨进程,直接在本进程里反射执行 [Fact] / [Theory]:
/// 断言仍然是 xunit 自己的 Assert(抛 Xunit.Sdk.XunitException),不是另写一套断言。
/// 支持:[Fact]、[Theory] + [InlineData]/[MemberData]/[ClassData]、Skip、async(Task);
/// 以及测试类实现的 IDisposable / IAsyncLifetime(跑完无论成败都清理,和 xunit 一致 —— A6 补)。
/// 不认识的形态记为 UNSUPPORTED 并让退出码非 0 —— 绝不静默跳过任何用例。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.Out.WriteLine("in-proc test runner (fallback for this sandbox; `dotnet test` needs a real machine)");

        Assembly assembly = typeof(Program).Assembly;
        var failures = new List<string>();
        int passed = 0;
        int failed = 0;
        int skipped = 0;
        int unsupported = 0;

        foreach (Type type in assembly.GetTypes().Where(IsCandidateTestClass).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                               .Where(m => m.GetCustomAttributes<FactAttribute>(true).Any() ||
                                                           m.GetCustomAttributes<TheoryAttribute>(true).Any())
                                               // 按声明顺序(MetadataToken)执行,和 xunit 一致 —— A6 之前这里按方法名排序。
                                               // 原因:搬来的 Windows 测试里有顺序敏感的用例(例如 AiFixChatStoreTests 三条用例
                                               // 共用缓存里的 Utils.ROOT,又各自 new TestHome(),方法名的字母序会把
                                               // SetActiveOnUnknownChatIsSilentAndKeepsActive 排到最后,读到前一条用例留下的 chat)。
                                               // xunit 走声明顺序,所以两边必须一致,否则两条通道的结论不可比。
                                               .OrderBy(m => m.MetadataToken))
            {
                string display = type.Name + "." + method.Name;

                // 注意:TheoryAttribute 继承自 FactAttribute,所以要把 Theory 从 Fact 里剔出去。
                var theories = method.GetCustomAttributes<TheoryAttribute>(true).ToList();
                var facts = method.GetCustomAttributes<FactAttribute>(true)
                                  .Where(a => a is not TheoryAttribute)
                                  .ToList();

                string? skip = theories.Count > 0 ? theories[0].Skip : facts[0].Skip;
                if (!string.IsNullOrEmpty(skip))
                {
                    skipped++;
                    Console.Out.WriteLine("[SKIP] " + display + " : " + skip);
                    continue;
                }

                bool isTheory = theories.Count > 0;
                List<object?[]> rows;
                if (isTheory)
                {
                    rows = EnumerateRows(method).ToList();
                    if (rows.Count == 0)
                    {
                        unsupported++;
                        Console.Out.WriteLine("[UNSUPPORTED] " + display + " : [Theory] 没解析出任何数据行(可能用了本执行器不认识的 DataAttribute)");
                        continue;
                    }
                }
                else
                {
                    rows = new List<object?[]> { Array.Empty<object?>() };
                }

                foreach (object?[] row in rows)
                {
                    string caseName = display + (isTheory ? "(" + Describe(row) + ")" : string.Empty);
                    string? error = Execute(type, method, row);
                    if (error is null)
                    {
                        passed++;
                        Console.Out.WriteLine("[PASS] " + caseName);
                    }
                    else
                    {
                        failed++;
                        failures.Add(caseName + Environment.NewLine + Indent(error));
                        Console.Out.WriteLine("[FAIL] " + caseName);
                    }
                }
            }
        }

        Console.Out.WriteLine();
        foreach (string failure in failures)
        {
            Console.Out.WriteLine(failure);
            Console.Out.WriteLine();
        }

        Console.Out.WriteLine(
            "总计 " + (passed + failed + skipped + unsupported) + " 个用例,通过 " + passed +
            ",失败 " + failed + "，跳过 " + skipped + ",不支持 " + unsupported);

        if (failed > 0 || unsupported > 0 || passed == 0)
        {
            Console.Error.WriteLine("in-proc 测试未全绿");
            return 1;
        }

        Console.Out.WriteLine("in-proc 测试全绿");
        return 0;
    }

    private static bool IsCandidateTestClass(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !type.IsPublic || type.IsNested)
        {
            return false;
        }

        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                   .Any(m => m.GetCustomAttributes<FactAttribute>(true).Any() ||
                             m.GetCustomAttributes<TheoryAttribute>(true).Any());
    }

    private static string? Execute(Type type, MethodInfo method, object?[] row)
    {
        object? instance = null;
        string? error = null;

        try
        {
            instance = Activator.CreateInstance(type);

            // xunit 语义:实现了 IAsyncLifetime 的测试类,构造之后先 InitializeAsync,跑完再 DisposeAsync。
            if (instance is IAsyncLifetime lifetime)
            {
                lifetime.InitializeAsync().GetAwaiter().GetResult();
            }

            object? result = method.Invoke(instance, row);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            error = tie.InnerException.GetType().FullName + ": " + tie.InnerException.Message;
        }
        catch (Exception ex)
        {
            error = ex.GetType().FullName + ": " + ex.Message;
        }

        // 无论成功失败都要清理实例(xunit 的规则:IAsyncLifetime 优先于 IDisposable)。
        string? disposeError = DisposeInstance(instance);
        return error ?? disposeError;
    }

    private static string? DisposeInstance(object? instance)
    {
        if (instance is null)
        {
            return null;
        }

        try
        {
            if (instance is IAsyncLifetime lifetime)
            {
                lifetime.DisposeAsync().GetAwaiter().GetResult();
            }
            else if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return null;
        }
        catch (Exception ex)
        {
            return "Dispose 失败 " + ex.GetType().FullName + ": " + ex.Message;
        }
    }

    // 鸭子类型读取 Theory 数据:任何带 GetData(MethodInfo) 的特性(xunit 的 InlineData /
    // MemberData / ClassData 都满足)都按它的返回的每个数据行展开。
    private static IEnumerable<object?[]> EnumerateRows(MethodInfo method)
    {
        foreach (object attribute in method.GetCustomAttributes(inherit: true))
        {
            MethodInfo? getData = attribute.GetType().GetMethod("GetData", new[] { typeof(MethodInfo) });
            if (getData is null)
            {
                continue;
            }

            object? produced = getData.Invoke(attribute, new object?[] { method });
            if (produced is not IEnumerable items)
            {
                continue;
            }

            foreach (object? item in items)
            {
                if (item is object?[] row)
                {
                    yield return row;
                    continue;
                }

                if (item is null)
                {
                    yield return new object?[] { null };
                    continue;
                }

                MethodInfo? rowGetData = item.GetType().GetMethod("GetData", Type.EmptyTypes);
                if (rowGetData?.Invoke(item, null) is IEnumerable<object?> values)
                {
                    yield return values.ToArray();
                    continue;
                }

                yield return new object?[] { item };
            }
        }
    }

    private static string Describe(object?[] row)
    {
        return string.Join(", ", row.Select(v => v is null ? "null" : v.ToString()));
    }

    private static string Indent(string text)
    {
        return "    " + text.Replace(Environment.NewLine, Environment.NewLine + "    ");
    }
}

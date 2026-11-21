// AI-logs-fs:ToolRunner 的分部 —— 日志 / 文件 / 打开文件夹几样工具的分发入口,以及「执行器到底认不认这个工具名」的探针。
//
// 分发:主 switch(ToolRunner.cs)里只加了一组 case 指到 RunLogsFs,实现都在 AiFsTools。
//
// 探针(DispatchesAsync):Linux 的 schema、ToolMeta.ALL、ToolRunner 分发是三份手写清单,以前只校验了前两份互相对得上,
// 第三份(switch)没人校验 —— 「提示里让模型调 get_latest_log,switch 里却没有」就是这样漏掉的。
// switch 没法反射,所以用一次**不会产生副作用的真调用**来判:
//   · 后端门面一调就抛 ProbeStop;
//   · 参数表里每个 schema 参数都塞一个 ProbeValue,它一被转成字符串就抛 ProbeStop;
//   · 子代理回调同样一调就抛。
// 于是任何一个真分发到的 case,在动到机器之前就会因为读参数 / 调后端而抛出;只有落到 default 分支的才会返回
// unknown_tool。结论:返回 unknown_tool = 执行器不认;其余(抛异常或正常返回)= 认。
// 这判断完全跟着真实 switch 走,别的包新加的工具不需要在这里登记。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

public sealed partial class ToolRunner
{
    /// <summary>get_latest_log / get_crash_report / fs_* / open_folder 的执行(实现见 AiFsTools)。</summary>
    private object RunLogsFs(string name, PyDict args)
    {
        string inst = InstName(args);
        return name switch
        {
            "get_latest_log" => AiFsTools.LogExcerpt(args, inst, "latest"),
            "get_crash_report" => AiFsTools.LogExcerpt(args, inst, "crash"),
            "fs_list" => AiFsTools.FsList(args, inst),
            "fs_read" => AiFsTools.FsRead(args, inst),
            "fs_write" => AiFsTools.FsWrite(args, inst),
            "open_folder" => AiFsTools.OpenFolder(args, inst),
            _ => ToolErr(name, "unknown_tool", "没有这个工具: " + name, ""),
        };
    }

    /// <summary>
    /// 执行器的分发 switch 是否认这个工具名(见文件头的探针说明)。只看分发,不看权限:
    /// 直接走 ExecuteAsync,跳过 RunAsync 里的元信息 / 权限两道门(那两样另有校验)。
    /// </summary>
    public static async Task<bool> DispatchesAsync(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        var probe = new ToolRunner(new ProbeBackend())
        {
            PermissionMode = "plan",
            Instance = "",
            Subagent = (_, _) => throw new ProbeStop(),
        };
        var args = new PyDict();
        foreach (string param in SchemaParams(name))
        {
            // instance 不塞:它只决定「落在哪个实例」,让它走执行器的默认值,别把探针值变成实例名
            if (param != "instance")
            {
                args[param] = new ProbeValue();
            }
        }
        try
        {
            object? result = await probe.ExecuteAsync(new ToolCall("probe", name, args), CancellationToken.None).ConfigureAwait(false);
            return !(result is string text && IsUnknownToolPayload(text));
        }
        catch (Exception)
        {
            // ProbeStop 或别的异常:都说明进了某个 case(default 分支只会安静地返回 unknown_tool)
            return true;
        }
    }

    private static bool IsUnknownToolPayload(string text)
    {
        try
        {
            return PyJson.Loads(text) is PyDict d && Py.S(d.Get("error")) == "unknown_tool" && d.Get("ok") is false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>某个工具 schema 里声明的参数名(没有这个工具就是空)。</summary>
    private static IEnumerable<string> SchemaParams(string name)
    {
        foreach (object schema in Tools.Schemas)
        {
            if (Tools.SchemaName(schema) != name)
            {
                continue;
            }
            PyDict function = Py.D(Py.D(schema)?.Get("function")) ?? new PyDict();
            PyDict parameters = Py.D(function.Get("parameters")) ?? new PyDict();
            PyDict properties = Py.D(parameters.Get("properties")) ?? new PyDict();
            foreach (KeyValuePair<string, object?> kv in properties)
            {
                yield return kv.Key;
            }
        }
    }

    /// <summary>探针专用:表示「已经进了某个 case,到此为止」。</summary>
    private sealed class ProbeStop : Exception
    {
        public ProbeStop() : base("probe") { }
    }

    /// <summary>探针参数值:一被当字符串用就抛 ProbeStop(在任何副作用之前拦住真执行)。</summary>
    private sealed class ProbeValue
    {
        public override string ToString() => throw new ProbeStop();
    }

    /// <summary>探针后端:任何调用都抛 ProbeStop(不联网、不碰磁盘)。</summary>
    private sealed class ProbeBackend : IBackend
    {
        public Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default)
            => throw new ProbeStop();
    }
}

// M4-H（AI 服务层）：会话用量统计（照 Windows 版 Launcher/Ai/Usage.cs 搬运）。
//
// 按对话 id 累计 input/output/requests：provider 给了真值就记真值（real_requests），
// 没给就把本地估算记进 input_estimated。AI 页顶部的「上下文占用环」读的就是这里。
using System.Collections.Generic;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>按对话累计的 token 用量。</summary>
public static class Usage
{
    private static readonly object Lock = new();

    private static readonly Dictionary<string, PyDict> Data = new();

    private static PyDict Blank()
        => PyDict.Of("input", 0L, "output", 0L, "input_estimated", 0L, "requests", 0L, "real_requests", 0L);

    private static void Inc(PyDict row, string k, long v) => row[k] = Py.Int(row[k]) + v;

    /// <summary>记一次请求（<paramref name="usage"/> 为空或没有 token 字段时按估算记）。</summary>
    public static void Add(string chatId, PyDict? usage, long estimatedInput = 0, long estimatedOutput = 0)
    {
        string id = string.IsNullOrEmpty(chatId) ? "active" : chatId;
        lock (Lock)
        {
            if (!Data.TryGetValue(id, out PyDict? row) || row == null)
            {
                row = Data[id] = Blank();
            }
            Inc(row, "requests", 1);
            usage ??= new PyDict();
            long input = Tokens.UsageInt(usage, "prompt_tokens", "input_tokens");
            long output = Tokens.UsageInt(usage, "completion_tokens", "output_tokens");
            if (input > 0 || output > 0)
            {
                Inc(row, "real_requests", 1);
                Inc(row, "input", input);
                Inc(row, "output", output);
            }
            else
            {
                Inc(row, "input_estimated", estimatedInput);
                Inc(row, "output", estimatedOutput);
            }
        }
    }

    /// <summary>读一份拷贝（source 说明这些数来自真值还是估算）。</summary>
    public static PyDict Get(string chatId)
    {
        lock (Lock)
        {
            string id = string.IsNullOrEmpty(chatId) ? "active" : chatId;
            if (!Data.TryGetValue(id, out PyDict? row) || row.Count == 0)
            {
                PyDict blank = Blank();
                blank["source"] = "estimate";
                return blank;
            }
            PyDict copy = row.Copy();
            copy["source"] = Py.Truthy(row["real_requests"]) ? "provider_usage" : "estimate";
            return copy;
        }
    }

    /// <summary>清掉某个对话的累计（新建/清空对话时用）。</summary>
    public static void Reset(string chatId)
    {
        lock (Lock)
        {
            Data.Remove(string.IsNullOrEmpty(chatId) ? "active" : chatId);
        }
    }
}

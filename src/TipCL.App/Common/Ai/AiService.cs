// M3-C1 AI 运行时门面：流式对话走 Core.Launcher.Ai.Client（OpenAI 兼容端点），
// 接入模式 / 地址 / 令牌 / 模型全部读 Core 的 Config.CONFIG（config.json 持久化），
// 不在此层保存任何凭据副本。供页面 / 插件宿主的 AI 能力调用。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core;
using TipCL.Core.Launcher;
using CoreAi = TipCL.Core.Launcher.Ai;
using PyDict = TipCL.Core.PyDict;

namespace TipCL.App.Common.Ai;

/// <summary>
/// AI 流式对话门面（最小落地）。底层 CoreAi.Client.ChatStream / ChatOnce；
/// 凭据与端点读 Config.CONFIG 的 ai_mode / ai_base_url / ai_api_key / ai_model。
/// </summary>
public static class AiService
{
    /// <summary>当前设置（Config.CONFIG 的引用，只读使用）。</summary>
    public static PyDict Settings => Config.CONFIG.data;

    public static string Get(string key)
        => Py.Str(Config.CONFIG.data.Get(key));

    /// <summary>解析当前端点（builtin / custom），供「测试连接」前的校验与提示。</summary>
    public static PyDict ResolveEndpoint() => CoreAi.Client.ResolveEndpoint(Settings);

    public static string TestConnection() => CoreAi.Client.TestConnection(Settings);

    public static List<object> ListModels() => CoreAi.Client.ListModels(Settings);

    public static long MaxOutputTokens() => CoreAi.Client.MaxOutputTokens(Settings);

    /// <summary>
    /// 单轮非流式对话。messages 元素为 PyDict（role/content），返回 choices[0].message 的结构化结果。
    /// </summary>
    public static PyDict ChatOnce(List<object> messages, double temperature = 0.3, CoreAi.HttpCancel? cancel = null)
        => CoreAi.Client.ChatOnce(Settings, messages, null!, temperature, cancel!);

    /// <summary>
    /// 一次流式对话：后台线程拉 SSE，增量文本经 onDelta 回调（含 &lt;think&gt;/&lt;/think&gt; 包裹的推理段），
    /// 正常结束回调 onDone（finish_reason 可能未知则空串），异常回调 onError（中文文案，来自 AIClientError）。
    /// 返回 IDisposable：Dispose 等价于中途停止。
    /// </summary>
    public static IDisposable StreamChat(List<object> messages, Action<string>? onDelta, Action<string>? onError, Action<string>? onDone, double temperature = 0.3)
    {
        var cancel = new CoreAi.HttpCancel();
        CancellationTokenSource done = new CancellationTokenSource();
        Task.Run(delegate
        {
            try
            {
                string finish = "";
                foreach (PyDict ev in CoreAi.Client.ChatStream(Settings, messages, null!, temperature, cancel))
                {
                    string type = Py.S(ev.Get("type"));
                    if (type == "delta")
                    {
                        string text = Py.S(ev.Get("text"));
                        if (text.Length > 0) onDelta?.Invoke(text);
                    }
                    else if (type == "error")
                    {
                        onError?.Invoke(Py.S(ev.Get("message")));
                        return;
                    }
                    else if (type == "done")
                    {
                        finish = Py.S(ev.Get("finish_reason"));
                    }
                    else if (type == "tool_calls")
                    {
                        // 最小门面不执行插件工具：把工具调用当错误上报，避免静默丢内容
                        onError?.Invoke("模型请求调用工具，当前 AI 门面未启用工具执行。");
                        return;
                    }
                }
                onDone?.Invoke(finish);
            }
            catch (CoreAi.AIClientError e)
            {
                if (!cancel.Cancelled()) onError?.Invoke(e.Message);
            }
            catch (CoreAi.AiRequestException e)
            {
                if (!cancel.Cancelled()) onError?.Invoke(e.Message);
            }
            catch (Exception e)
            {
                if (!cancel.Cancelled()) onError?.Invoke("AI 对话异常：" + Py.ExcStr(e));
            }
            finally
            {
                done.Cancel();
            }
        });
        return new ChatRun(cancel);
    }

    private sealed class ChatRun : IDisposable
    {
        private readonly CoreAi.HttpCancel _cancel;
        public ChatRun(CoreAi.HttpCancel cancel) { _cancel = cancel; }
        public void Dispose()
        {
            _cancel.Abort();
        }
    }
}

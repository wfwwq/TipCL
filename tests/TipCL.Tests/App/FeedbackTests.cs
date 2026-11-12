using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 反馈链路回归:必须「真的发得出去、且不被服务器拒」。
/// 用一个本机 TCP sink 顶替 ingest 服务器(不联网、不污染线上),断言请求形状与服务器要求一致:
///   · 路径 /api/v1/feedback
///   · 头 X-PyMCL-Client = PyMCL/&lt;版本&gt;(当初修 403 就是补这个客户端标识)
///   · User-Agent 也是 PyMCL/&lt;版本&gt;(与 Android/桌面端一致)
///   · Content-Type: application/json + 必需字段(device_id/category/title/body/app_version)
///   · category 归并到服务器认得的值域 {bug,feature,crash,other}
///   · sysinfo 为「key: value」多行文本(服务器/Android 端约定)
/// 另锁:没同意诊断数据上传前**不发送**;服务器拒绝时把状态码原样抛出(UI 要能显示)。
/// </summary>
public class FeedbackTests : IDisposable
{
    private readonly IApi _api = InProcessApi.Instance;
    private readonly RequestSink _sink = new();
    private readonly string _urlBefore;
    private readonly object? _consentBefore;

    public FeedbackTests()
    {
        _urlBefore = Py.S(Config.CONFIG.Get("feedback_url", ""));
        _consentBefore = Config.CONFIG.Get("feedback_consent");
        Config.CONFIG.Set("feedback_url", _sink.BaseUrl);
    }

    public void Dispose()
    {
        Config.CONFIG.Set("feedback_url", _urlBefore);
        if (_consentBefore == null)
        {
            var c = Config.CONFIG;
            c.Set("feedback_consent", false);
        }
        else
        {
            Config.CONFIG.Set("feedback_consent", _consentBefore);
        }
        _sink.Dispose();
    }

    [Fact]
    public void Submit_SendsRequest_ShapedAsTheServerRequires() => TestApp.RunAsync(async () =>
    {
        Config.CONFIG.Set("feedback_consent", true);
        var result = await _api.CallAsync<PyDict>("feedback_submit", new PyDict
        {
            ["category"] = "mod",          // 桌面端特有分类 → 应归并为 other
            ["body"] = "第一次反馈:下载页偶发卡顿\n第二行细节",
            ["include_sysinfo"] = true,
        });
        Assert.True(_sink.Seen.Wait(TimeSpan.FromSeconds(25)), "服务器没收到请求");

        Assert.Equal("POST", _sink.Method);
        Assert.Equal("/api/v1/feedback", _sink.Path);
        Assert.Equal("PyMCL/" + AppInfo.APP_VERSION, _sink.Headers["X-PyMCL-Client"]);
        Assert.Equal("PyMCL/" + AppInfo.APP_VERSION, _sink.Headers["User-Agent"]);
        Assert.StartsWith("application/json", _sink.Headers["Content-Type"]);

        var payload = PyJson.Loads(_sink.Body!);
        var d = Py.D(payload);
        Assert.Equal("other", Py.S(d.Get("category")));                       // 归并后的线上分类
        Assert.False(string.IsNullOrEmpty(Py.S(d.Get("device_id"))), "缺设备标识");
        Assert.Equal(AppInfo.APP_VERSION, Py.S(d.Get("app_version")));
        Assert.Contains("第一次反馈", Py.S(d.Get("title")));                    // 标题缺省取正文首行
        Assert.Contains("第二行细节", Py.S(d.Get("body")));
        Assert.True(d.Get("contact") != null, "缺 contact 字段(可为空串)");
        string sysinfo = Py.S(d.Get("sysinfo"));
        Assert.Contains("\n", sysinfo);                                       // 多行文本
        Assert.Contains(": ", sysinfo);                                       // key: value

        Assert.NotNull(result);
        Assert.Equal("fb-test", Py.S(result!["id"]));
    });

    [Fact]
    public void Submit_WithoutConsent_DoesNotSend_ThenConsentAllowsIt() => TestApp.RunAsync(async () =>
    {
        Config.CONFIG.Set("feedback_consent", false);
        Assert.False(Py.Truthy(await _api.CallAsync<object>("feedback_has_consent") ?? false));

        await Assert.ThrowsAsync<FeedbackError>(() => _api.CallAsync<PyDict>("feedback_submit", new PyDict
        {
            ["category"] = "bug",
            ["body"] = "未同意时不该发出",
            ["include_sysinfo"] = false,
        }));
        Assert.False(_sink.Seen.Wait(TimeSpan.FromMilliseconds(300)), "未同意却发出去了");

        // 同意门放行后应能发出
        await _api.CallAsync<object>("feedback_consent", new PyDict { ["allow"] = true });
        Assert.True(Py.Truthy(await _api.CallAsync<object>("feedback_has_consent") ?? false));
        await _api.CallAsync<PyDict>("feedback_submit", new PyDict
        {
            ["category"] = "feature",
            ["body"] = "同意之后再发一次",
            ["include_sysinfo"] = false,
        });
        Assert.True(_sink.Seen.Wait(TimeSpan.FromSeconds(25)), "同意后仍未发出");
        Assert.Equal("feature", Py.S(Py.D(PyJson.Loads(_sink.Body!)).Get("category")));
    });

    [Fact]
    public void Submit_AgainstIngestLikeSink_PassesIdentityAndPayloadGates() => TestApp.RunAsync(async () =>
    {
        // sink 复刻生产 ingest 的两条规则(已用真实服务器探测确认):
        //   缺 X-PyMCL-Client → 403「缺少客户端标识」;标题与内容都空 → 400「请填写标题或内容」;否则 200 + id
        _sink.EnforceIngestRules = true;
        Config.CONFIG.Set("feedback_consent", true);

        var result = await _api.CallAsync<PyDict>("feedback_submit", new PyDict
        {
            ["category"] = "bug",
            ["body"] = "整链路上线自检:标识门 + 载荷门 + 返回 id",
            ["include_sysinfo"] = false,
        });

        Assert.True(_sink.Seen.Wait(TimeSpan.FromSeconds(20)), "服务器没收到请求");
        Assert.Equal(200, _sink.LastStatus);                       // 两道门都过了
        Assert.NotNull(result);
        Assert.Equal("fb-ingest", Py.S(result!["id"]));
    });

    [Fact]
    public void Submit_ServerRejection_SurfacesStatusCode() => TestApp.RunAsync(async () =>
    {
        Config.CONFIG.Set("feedback_consent", true);
        _sink.StatusCode = 403;
        _sink.ResponseJson = "{\"ok\":false,\"error\":\"missing client identity\"}";

        var ex = await Assert.ThrowsAsync<FeedbackError>(() => _api.CallAsync<PyDict>("feedback_submit", new PyDict
        {
            ["category"] = "bug",
            ["body"] = "会被服务器拒绝",
            ["include_sysinfo"] = false,
        }));
        Assert.Contains("403", ex.Message);
    });
}

/// <summary>极简本机 HTTP sink(只收一个请求):TcpListener 实现,免 Windows 的 HttpListener URL ACL。</summary>
internal sealed class RequestSink : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public string BaseUrl { get; }

    public string? Method { get; private set; }

    public string? Path { get; private set; }

    public string? Body { get; private set; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int StatusCode { get; set; } = 200;

    public string ResponseJson { get; set; } = "{\"ok\":true,\"id\":\"fb-test\"}";

    /// <summary>打开后按生产 ingest 的规则回话(403 / 400 / 200)。</summary>
    public bool EnforceIngestRules { get; set; }

    /// <summary>最近一次真正发出的状态码(受 EnforceIngestRules 影响)。</summary>
    public int LastStatus { get; private set; }

    public ManualResetEventSlim Seen { get; } = new(false);

    public RequestSink()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _ = Task.Run(AcceptOnce);
    }

    private async Task AcceptOnce()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            using var stream = client.GetStream();
            var buffer = new List<byte>();
            var chunk = new byte[4096];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = await stream.ReadAsync(chunk, _cts.Token);
                if (n <= 0) break;
                buffer.AddRange(chunk[..n]);
                headerEnd = FindHeaderEnd(buffer);
            }
            string head = Encoding.UTF8.GetString(buffer.ToArray());
            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            Method = requestLine[0];
            Path = requestLine.Length > 1 ? requestLine[1] : "";
            foreach (string line in lines[1..])
            {
                int i = line.IndexOf(':');
                if (i > 0)
                    Headers[line[..i].Trim()] = line[(i + 1)..].Trim();
            }

            int contentLength = Headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
            while (buffer.Count < headerEnd + contentLength)
            {
                int n = await stream.ReadAsync(chunk, _cts.Token);
                if (n <= 0) break;
                buffer.AddRange(chunk[..n]);
            }
            Body = Encoding.UTF8.GetString(buffer.ToArray(), headerEnd, Math.Max(0, buffer.Count - headerEnd));

            int status = StatusCode;
            string json = ResponseJson;
            if (EnforceIngestRules)
            {
                var d = Py.D(PyJson.Loads(Body ?? "{}"));
                if (!Headers.ContainsKey("X-PyMCL-Client"))
                    (status, json) = (403, "{\"ok\":false,\"error\":\"缺少客户端标识\"}");
                else if (Py.S(d.Get("title")).Length == 0 && Py.S(d.Get("body")).Length == 0)
                    (status, json) = (400, "{\"ok\":false,\"error\":\"请填写标题或内容\"}");
                else
                    (status, json) = (200, "{\"ok\":true,\"id\":\"fb-ingest\"}");
            }
            LastStatus = status;

            byte[] body = Encoding.UTF8.GetBytes(json);
            string resp = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\n" +
                          "Content-Type: application/json\r\n" +
                          $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(resp), _cts.Token);
            await stream.WriteAsync(body, _cts.Token);
            await stream.FlushAsync(_cts.Token);
            Seen.Set();
        }
        catch (Exception)
        {
            // 取消/断开都算正常收尾
        }
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 3; i < bytes.Count; i++)
            if (bytes[i - 3] == 13 && bytes[i - 2] == 10 && bytes[i - 1] == 13 && bytes[i] == 10)
                return i + 1;
        return -1;
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (Exception) { }
        try { _listener.Stop(); } catch (Exception) { }
        Seen.Dispose();
        _cts.Dispose();
    }
}

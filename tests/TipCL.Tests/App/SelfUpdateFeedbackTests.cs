// M4-H R22 收尾(task-21):把「更新失败必须在界面上说出来」这条修复钉进 dotnet test。
//
// 为什么必须补:R22(`ed419c2`)新增了 `Common/SelfUpdate.cs`(51 行),但那次提交的 7 个文件里
// **没有一个在 `tests/`** —— 也就是说"失败要有界面反馈"这套关键行为此前只靠真机脚本
// (`scratch/update-e2e/`、`scratch/update-e2e2/`)兜着,没有红灯。谁哪天把那条 toast 拆了,CI 不会响。
//
// 本文件在进程内把整条链路跑一遍(无头 UI 会话 + 本机假包服务器),覆盖三件事:
//   1 下载/校验失败 → 任务以失败结束 → 界面收到「更新失败 + 原因」(= `SelfUpdate` 的存在理由,真机 T1/T4);
//   2 同一个失败必须落在**任务行**上(任务页那张卡片显示的正是 `row.Status`,真机 T4/t4-30-tasks-page.png);
//   3 正常路径(真 sha)不被这两处改动弄坏:包落到 cache、`update_staged` 照发(真机 T3/T5)。
//   4 格式非法的占位值(仓库里那份清单的现值)当场被拒:连包都不去拉、也不产生任何任务(真机 T2)。
//
// 真机证据见 `scratch/update-e2e2/REPORT.md`;本文件是它的机器可复跑版。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

public class SelfUpdateFeedbackTests
{
    private const string PlaceholderSha = "REPLACE-WITH-THE-REAL-SHA256";

    /// <summary>假清单(注入点:`Updater.JsonFetcher`,与 News/MdView 同一套测试替身做法)。</summary>
    private static PyDict Manifest(string version, string url, string sha) =>
        PyDict.Of("version", version, "notes", "task-21 单元测试", "url", url, "sha256", sha);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Sha256OfFile(string path) => Sha256(File.ReadAllBytes(path));

    /// <summary>边等边推渲染 pass(照 DlgCloseTests:无头会话里不推计时器 / 不跑队列,Post 出去的续体不会动)。</summary>
    private static async Task<bool> PumpAsync(Func<bool> ready, int timeoutMs = 30000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            if (ready()) return true;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return ready();
    }

    /// <summary>订阅事件流(事件可能来自线程池,列表加锁)。</summary>
    private static (List<ApiEvent> Events, Action<ApiEvent> Handler) Subscribe()
    {
        var events = new List<ApiEvent>();
        var gate = new object();
        void Handler(ApiEvent ev) { lock (gate) events.Add(ev); }
        InProcessApi.Instance.BridgeEvent += Handler;
        return (events, Handler);
    }

    // ───────────────────────── 1)+2) 失败路径 ─────────────────────────

    /// <summary>
    /// 清单 sha256 **格式合法但值错**(64 个 0)—— 真机 T1/T4 用的就是这个形状:
    /// 它会一路走到下载才失败。修复前界面上什么都看不到(真机对照:失败前后三张截图逐字节相同),
    /// 修复后必须收到「更新失败」+ 原因,而且失败要落在任务行上。
    /// </summary>
    [Fact]
    public void StartSelfUpdate_WhenTheShaDoesNotMatch_ReportsTheFailureOnScreenAndOnTheTaskRow()
    {
        TestApp.RunAsync(async () =>
        {
            using var server = new LocalPackageServer(Encoding.UTF8.GetBytes("NOT-THE-PACKAGE"));
            (List<ApiEvent> events, Action<ApiEvent> handler) = Subscribe();
            var toasts = new List<(string Title, string Body, ToastKind Kind)>();
            try
            {
                Updater.JsonFetcher = _ => Manifest("99.0.1", server.Url("/pkg.AppImage"), new string('0', 64));

                await SelfUpdate.StartAsync(InProcessApi.Instance, (t, b, k) => toasts.Add((t, b, k)));

                Assert.True(
                    await PumpAsync(() => toasts.Count > 0),
                    "下载/校验失败之后界面上什么都没出现 —— 这正是 R22 修掉的那个静默失败");

                Assert.Equal("更新失败", toasts[0].Title);
                Assert.Equal(ToastKind.Error, toasts[0].Kind);
                Assert.Contains("sha256 校验失败", toasts[0].Body);
                Assert.Contains("pkg.AppImage", toasts[0].Body);          // 原因里要有到底是哪个地址失败的

                // 事件流:`finished` 且 Success=false(SelfUpdate 盯的就是这一条)
                Assert.True(
                    await PumpAsync(() => events.Any(e => e.Event == "finished")),
                    "任务结束了但事件流里没有 finished");
                ApiEvent fin = events.Last(e => e.Event == "finished");
                Assert.False(fin.Success);
                Assert.Contains("sha256 校验失败", fin.Message);

                // 任务行:任务页那张卡片显示的 row.Status 就是这句原因
                TaskRow? row = TaskStore.Rows.LastOrDefault(r => r.Title == "更新启动器");
                Assert.NotNull(row);
                Assert.True(row!.Finished, "任务已经失败了,行上却还标着未完成");
                Assert.False(row.Success);
                Assert.Contains("sha256 校验失败", row.Status);

                // 失败不该留下那个(坏)包。注意版本号与别的用例**特意错开**(缓存文件名里带版本号),
                // 否则这条断言会被另一个用例写进 cache 的同名包误判。
                string cacheDir = Path.Combine(Utils.ROOT, "cache");
                string cache = Path.Combine(cacheDir, "TipCL-99.0.1.bin");
                string listing = Directory.Exists(cacheDir)
                    ? string.Join(", ", Directory.GetFiles(cacheDir).Select(Path.GetFileName))
                    : "(cache 目录不存在)";
                Assert.False(File.Exists(cache), $"sha256 校验失败,包却留在缓存里了:{cache};目录内容 = {listing}");
            }
            finally
            {
                InProcessApi.Instance.BridgeEvent -= handler;
                Updater.JsonFetcher = null;
                TaskStore.ClearFinished();
            }
        });
    }

    // ───────────────────────── 3) 正常路径 ─────────────────────────

    /// <summary>
    /// 真 sha256:下载校验通过 → 包落到 cache、`update_staged` 事件照发(外壳据此弹「更新已下载」),
    /// 而 `SelfUpdate` **保持安静**(成功不重复打扰,由 `update_staged` 那条路负责)。
    /// </summary>
    [Fact]
    public void StartSelfUpdate_WithTheRealSha_StagesThePackageAndStaysQuiet()
    {
        TestApp.RunAsync(async () =>
        {
            byte[] pkg = Encoding.UTF8.GetBytes("TipCL-fake-package-bytes-" + new string('x', 4096));
            string sha = Sha256(pkg);
            using var server = new LocalPackageServer(pkg);
            (List<ApiEvent> events, Action<ApiEvent> handler) = Subscribe();
            var toasts = new List<(string Title, string Body, ToastKind Kind)>();
            try
            {
                Updater.JsonFetcher = _ => Manifest("99.0.2", server.Url("/pkg.AppImage"), sha);

                await SelfUpdate.StartAsync(InProcessApi.Instance, (t, b, k) => toasts.Add((t, b, k)));

                Assert.True(
                    await PumpAsync(() => events.Any(e => e.Event == "update_staged")),
                    "下载校验都通过了,却没发 update_staged(正常路径被弄坏了)");

                ApiEvent staged = events.First(e => e.Event == "update_staged");
                Assert.Equal("99.0.2", staged.Message);
                Assert.True(File.Exists(staged.Text), $"更新包没落到缓存:{staged.Text}");
                Assert.Equal(sha, Sha256OfFile(staged.Text));                 // 落盘的确实是那个包

                Assert.True(
                    await PumpAsync(() => TaskStore.Rows.Any(r => r.Title == "更新启动器" && r.Finished)),
                    "任务没结束");
                TaskRow row = TaskStore.Rows.Last(r => r.Title == "更新启动器");
                Assert.True(row.Success, $"正常路径被标成失败:{row.Status}");

                Assert.Empty(toasts);                                        // 成功保持安静
            }
            finally
            {
                InProcessApi.Instance.BridgeEvent -= handler;
                Updater.JsonFetcher = null;
                TaskStore.ClearFinished();
            }
        });
    }

    // ───────────────────────── 4) 格式非法占位值 ─────────────────────────

    /// <summary>
    /// 仓库里那份清单的现值(`REPLACE-WITH-THE-REAL-SHA256`,R22 特意写成格式非法)必须**当场被拒**:
    /// 不弹「发现新版本」、不产生任何任务、连包都不去拉。真机版是 T2(截图里是「更新被拒绝」toast)。
    /// </summary>
    [Fact]
    public void CheckUpdate_WithTheCommittedPlaceholder_RefusesUpFront_WithoutTouchingTheNetwork()
    {
        TestApp.RunAsync(async () =>
        {
            using var server = new LocalPackageServer(Encoding.UTF8.GetBytes("x"));
            try
            {
                Updater.JsonFetcher = _ => Manifest("99.0.0", server.Url("/pkg.AppImage"), PlaceholderSha);

                PyDict? res = await InProcessApi.Instance.CallAsync<PyDict>("check_update");

                Assert.NotNull(res);
                Assert.False(Py.Truthy(res!["has_update"]));                  // 不会去弹「发现新版本」
                Assert.False(Py.Truthy(res["ok"]));                           // "被拒绝"而不是"已是最新"
                Assert.Contains("SHA-256", Py.S(res["message"]));
                Assert.Equal(0, server.Requests);                             // 当场拒绝:连包都不去拉
                Assert.DoesNotContain(TaskStore.Rows, r => r.Title == "更新启动器");
            }
            finally
            {
                Updater.JsonFetcher = null;
                TaskStore.ClearFinished();
            }
        });
    }

    /// <summary>
    /// 本机假包服务器:极简 HTTP,总是 `200` + `Content-Length` + 正文。
    /// 对 `Range: bytes=0-0`(下载器的分片探测)也回 200 而不是 206 —— 与真机 rig 里那个
    /// `python3 -m http.server` 行为一致(它同样忽略 Range),下载器据此回落到单连接。
    /// </summary>
    private sealed class LocalPackageServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly CancellationTokenSource _cts = new();
        private int _requests;

        public string BaseUrl { get; }

        public int Requests => Volatile.Read(ref _requests);

        public LocalPackageServer(byte[] body)
        {
            _body = body;
            _listener = new TcpListener(IPAddress.Loopback, 0);   // 端口 0 = 系统给个空闲端口,避免撞别的用例
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Url(string path) => BaseUrl + path;

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
                catch (Exception) { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var buffer = new List<byte>();
                    var chunk = new byte[4096];
                    int headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        int n = await stream.ReadAsync(chunk, _cts.Token).ConfigureAwait(false);
                        if (n <= 0) return;
                        buffer.AddRange(chunk[..n]);
                        headerEnd = FindHeaderEnd(buffer);
                    }

                    Interlocked.Increment(ref _requests);

                    string head =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/octet-stream\r\n" +
                        $"Content-Length: {_body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head), _cts.Token).ConfigureAwait(false);
                    await stream.WriteAsync(_body, _cts.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // 客户端主动断开 / 取消都算正常收尾
            }
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (int i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == 13 && bytes[i - 2] == 10 && bytes[i - 1] == 13 && bytes[i] == 10)
                {
                    return i + 1;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch (Exception) { }
            try { _listener.Stop(); } catch (Exception) { }
            _cts.Dispose();
        }
    }
}

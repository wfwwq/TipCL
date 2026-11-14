// task-22 条 2:手机同步的**协议级**端到端(不需要真手机,也不需要盒子 UI)。
//
// 背景:linux 侧原来的 `App/PhoneSyncTests.cs` 开篇自己就写着「真机同步要一台连着手机的环境(接线/同网),
// 这里覆盖不需要手机的那部分」—— 也就是说「配对 → 对比 → 真的把一个存档传过去 / 拉回来」这条链
// 在自动化里**从来没有跑过**,只靠真机手点。本文件用**假手机服务端**把它补上:
//
//   * 协议面照 `src/TipCL.Core/Launcher/Sync/PhoneClient.cs` 的 5 个端点实现
//     (`/api/ping`、`/api/worlds`、`/api/worlds/download`、`/api/worlds/upload`、`/api/worlds/delete`),
//     每个请求都要带 `X-PyMCL-Token`(手机上的 6 位配对码),否则 401;
//   * 服务端用 `TcpListener` 手写 HTTP —— 循 windos 侧 PhoneLinkTests 与仓库既有
//     `FeedbackTests` / `AiRetryTests` 的先例(免掉 Windows 上 HttpListener 要 URL ACL 的坑);
//   * 全程不碰网络,端口用 0 让系统分配。
//
// 覆盖:①配对(对的码成功 / 错的码给「配对码不对」)②对比(只在电脑 / 只在手机)
//      ③一键同步**两个方向都真的动了字节** ④传完两边判定回到「一致」
//      ⑤上传撞名 409、删除不存在 404 的中文文案。
//
// 剩下**仍然只能真机验**的那部分(如实登记,别把这文件当"真机同步已验"):手机端真正的 Android
// 同步 App、局域网扫描 `ScanLan`、USB(adb forward)通道、以及真机 UI 里那套面板的观感。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests.Core;

public class PhoneLinkProtocolTests
{
    /// <summary>假手机上的世界根 id:DefaultRoot 会优先挑 "global"。</summary>
    private const string RootId = "global";

    [Fact]
    public void Pair_WithTheRightCode_Handshakes_AndWithAWrongCode_SaysTheCodeIsWrong()
    {
        using var phone = new FakePhone();
        PhoneLink.Disconnect();
        try
        {
            PhoneClient.Info info = PhoneLink.Connect(phone.Address, FakePhone.PairingCode);

            Assert.Equal("FakePhone-9", info.Device);
            Assert.Equal(1, info.Protocol);
            Assert.Equal(27531, info.Port);
            Assert.NotNull(PhoneLink.Current);
            Assert.Equal("FakePhone-9", PhoneLink.LastDevice);
            // 握手确实是打给 /api/ping 的(而不是靠本地假装的)
            Assert.Contains("GET /api/ping", phone.Requests);

            // 错的配对码:401 并且给出人话
            PhoneClient.ApiException ex =
                Assert.Throws<PhoneClient.ApiException>(() => new PhoneClient(phone.Address, "000000").Ping());
            Assert.Equal(401, ex.Status);
            Assert.Contains("配对码不对", ex.Message);
        }
        finally
        {
            PhoneLink.Disconnect();
        }
    }

    [Fact]
    public void Compare_ShowsLocalOnlyAndPhoneOnlyRows()
    {
        using var phone = new FakePhone();
        PhoneLink.Disconnect();
        try
        {
            SaveSync.Target t = MakeLocalWorld("LocalWorldP");
            PhoneLink.Connect(phone.Address, FakePhone.PairingCode);

            Assert.Equal(RootId, PhoneLink.DefaultRoot(t, PhoneLink.Roots()));

            List<PhoneLink.Row> rows = PhoneLink.Compare(t, RootId);

            PhoneLink.Row local = rows.Single(r => r.Name == "LocalWorldP");
            Assert.Equal(SyncDecision.OnlyLocal, local.Decision);
            Assert.True(local.HasLocal);
            Assert.False(local.HasRemote);
            Assert.False(string.IsNullOrEmpty(local.LocalHash));

            PhoneLink.Row remote = rows.Single(r => r.Name == phone.PhoneWorld);
            Assert.Equal(SyncDecision.OnlyRemote, remote.Decision);
            Assert.True(remote.HasRemote);
            Assert.False(remote.HasLocal);
            Assert.True(remote.Remote.SizeBytes > 0);
        }
        finally
        {
            PhoneLink.Disconnect();
        }
    }

    [Fact]
    public void SyncAll_PushesTheLocalWorldAndPullsThePhoneWorld()
    {
        using var phone = new FakePhone();
        PhoneLink.Disconnect();
        try
        {
            SaveSync.Target t = MakeLocalWorld("LocalWorldP");
            PhoneLink.Connect(phone.Address, FakePhone.PairingCode);

            List<string> done = PhoneLink.SyncAll(t, RootId, _ => SyncDecision.Same, _ => { });

            // 两个方向都执行了(失败会被 SyncAll 记成「失败:名字(原因)」)
            // 注意:产品文案里那个冒号是**全角 U+FF1A**(`PhoneLink.cs:414/419`),不是 ASCII 的 ':' ——
            // 工具里渲染出来一模一样,别靠肉眼比(用 \uFF1A 写死,免得以后再被咬一次)。
            Assert.Contains("→ 手机\uFF1A" + "LocalWorldP", done);
            Assert.Contains("→ 电脑\uFF1A" + phone.PhoneWorld, done);
            Assert.DoesNotContain(done, d => d.StartsWith("失败:", StringComparison.Ordinal));

            // ③-a 手机侧真的收到了电脑那个世界:把上传的 zip 解出来看内容
            byte[]? pushed = phone.Uploaded("LocalWorldP");
            Assert.NotNull(pushed);
            using (var ms = new MemoryStream(pushed!))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                ZipArchiveEntry? entry = zip.GetEntry("LocalWorldP/marker.txt");
                Assert.NotNull(entry);
                using var sr = new StreamReader(entry!.Open());
                Assert.Equal("FROM-DESKTOP", sr.ReadToEnd());
            }

            // ③-b 电脑侧真的落了手机那个世界
            string pulled = Path.Combine(t.SavesDir, phone.PhoneWorld, "marker.txt");
            Assert.True(File.Exists(pulled), "手机上的世界没有被拉下来:" + pulled);
            Assert.Equal("FROM-PHONE", File.ReadAllText(pulled));
            Assert.True(File.Exists(Path.Combine(t.SavesDir, phone.PhoneWorld, "level.dat")));

            // ④ 传完这两个世界两边都该判「一致」(基准已记下)
            List<PhoneLink.Row> after = PhoneLink.Compare(t, RootId);
            Assert.Equal(SyncDecision.Same, after.Single(r => r.Name == "LocalWorldP").Decision);
            Assert.Equal(SyncDecision.Same, after.Single(r => r.Name == phone.PhoneWorld).Decision);
        }
        finally
        {
            PhoneLink.Disconnect();
        }
    }

    [Fact]
    public void Upload_OnAnExistingWorldWithoutReplace_IsRefused_AndDeleteMissesSaySo()
    {
        using var phone = new FakePhone();
        PhoneLink.Disconnect();
        try
        {
            PhoneClient c = new PhoneClient(phone.Address, FakePhone.PairingCode);

            // 第一次传:新世界,replace=0 可以
            string zip = WriteZip(phone.PhoneWorld, "FROM-DESKTOP");
            try
            {
                Assert.Equal(phone.PhoneWorld, c.Upload(RootId, phone.PhoneWorld, zip, replace: false));
                // 第二次:同名且 replace=0 → 409
                PhoneClient.ApiException exists =
                    Assert.Throws<PhoneClient.ApiException>(() => c.Upload(RootId, phone.PhoneWorld, zip, replace: false));
                Assert.Equal(409, exists.Status);
                Assert.Contains("手机上已有同名世界", exists.Message);
                // replace=1 就能覆盖
                Assert.Equal(phone.PhoneWorld, c.Upload(RootId, phone.PhoneWorld, zip, replace: true));
            }
            finally
            {
                File.Delete(zip);
            }

            // 删一个手机上没有的世界 → 404 + 中文文案
            PhoneClient.ApiException miss =
                Assert.Throws<PhoneClient.ApiException>(() => c.Delete(RootId, "NotOnPhoneP"));
            Assert.Equal(404, miss.Status);
            Assert.Contains("手机上没有这个世界", miss.Message);
        }
        finally
        {
            PhoneLink.Disconnect();
        }
    }

    // ───────────────────────── 夹具 ─────────────────────────

    /// <summary>在隔离 TIPCL_HOME 的根实例存档目录下造一个本地世界,返回它的 Target。</summary>
    private static SaveSync.Target MakeLocalWorld(string name)
    {
        List<SaveSync.Target> targets = SaveSync.Targets();
        Assert.NotEmpty(targets);
        SaveSync.Target t = targets[0];
        string dir = Path.Combine(t.SavesDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "level.dat"), "LOCAL-LEVEL-DAT");
        File.WriteAllText(Path.Combine(dir, "marker.txt"), "FROM-DESKTOP");
        return t;
    }

    private static string WriteZip(string world, string marker)
    {
        string path = Path.Combine(Path.GetTempPath(), "tipcl-phone-tests-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, FakePhone.WorldZip(world, marker));
        return path;
    }

    /// <summary>
    /// 假手机:手写 HTTP/1.1 的极小服务端,实现 PhoneClient 要的那 5 个端点。
    /// 每个请求都记进 <see cref="Requests"/>(断言"真的打过来了"),上传的字节留在内存里(断言"传的是什么")。
    /// </summary>
    private sealed class FakePhone : IDisposable
    {
        public const string PairingCode = "123456";

        /// <summary>手机端给所有世界的修改时间。固定值 ⇒ 三方判定可复现(不然会撞上「差 2 秒算一样」那条规则)。</summary>
        private const long FixedMtime = 1_760_000_000_000L;

        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private volatile bool _stopped;
        private readonly object _gate = new();
        private readonly List<string> _requests = new();
        private readonly Dictionary<string, byte[]> _uploads = new();

        public FakePhone(string phoneWorld = "PhoneWorldP")
        {
            PhoneWorld = phoneWorld;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public string PhoneWorld { get; }

        public int Port { get; }

        public string Address => "127.0.0.1:" + Port;

        public IReadOnlyList<string> Requests
        {
            get { lock (_gate) { return _requests.ToList(); } }
        }

        public byte[]? Uploaded(string name)
        {
            lock (_gate) { return _uploads.TryGetValue(name, out byte[]? b) ? b : null; }
        }

        public static byte[] WorldZip(string world, string marker)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                Put(zip, world + "/level.dat", Encoding.UTF8.GetBytes("PHONE-LEVEL-DAT"));
                Put(zip, world + "/marker.txt", Encoding.UTF8.GetBytes(marker));
                Put(zip, world + "/region/r.0.0.mca", new byte[4096]);
            }
            return ms.ToArray();
        }

        private static void Put(ZipArchive zip, string name, byte[] data)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using Stream s = e.Open();
            s.Write(data, 0, data.Length);
        }

        public void Dispose()
        {
            _stopped = true;
            try { _listener.Stop(); } catch (Exception) { }
        }

        private void Loop()
        {
            while (!_stopped)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch (Exception) { return; }
                try
                {
                    using (c) { Handle(c); }
                }
                catch (Exception)
                {
                    // 单个请求出错不该拖垮整个假手机
                }
            }
        }

        private void Handle(TcpClient c)
        {
            NetworkStream s = c.GetStream();
            string head = ReadHead(s);
            if (head.Length == 0) return;
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] request = lines[0].Split(' ');
            string method = request[0];
            string target = request.Length > 1 ? request[1] : "/";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines.Skip(1))
            {
                int i = line.IndexOf(':');
                if (i > 0) headers[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            int len = headers.TryGetValue("Content-Length", out string? hcl) && hcl != null && int.TryParse(hcl, out int n) ? n : 0;
            byte[] body = ReadBody(s, len);

            int q = target.IndexOf('?');
            string path = q < 0 ? target : target.Substring(0, q);
            Dictionary<string, string> query = ParseQuery(target);

            lock (_gate) { _requests.Add(method + " " + target); }

            if (!headers.TryGetValue(PhoneClient.TokenHeader, out string? token) || token != PairingCode)
            {
                Send(s, 401, "{\"error\":\"bad token\"}", "application/json");
                return;
            }

            switch (method + " " + path)
            {
                case "GET /api/ping":
                    Send(s, 200, "{\"app\":\"TipCL Sync (fake)\",\"protocol\":1,\"device\":\"FakePhone-9\","
                        + "\"android\":\"14\",\"port\":27531,\"deviceTimeMs\":" + FixedMtime + "}", "application/json");
                    return;
                case "GET /api/worlds":
                    Send(s, 200, WorldsJson(), "application/json");
                    return;
                case "GET /api/worlds/download":
                {
                    string name = query.TryGetValue("name", out string? qn) && qn != null ? qn : PhoneWorld;
                    byte[]? data = WorldBytes(name);
                    if (data == null) { Send(s, 404, "{\"error\":\"no such world\"}", "application/json"); return; }
                    Send(s, 200, data, "application/zip");
                    return;
                }
                case "POST /api/worlds/upload":
                {
                    string name = query.TryGetValue("name", out string? qn) && qn != null ? qn : PhoneWorld;
                    bool replace = query.TryGetValue("replace", out string? qr) && qr == "1";
                    lock (_gate)
                    {
                        if (_uploads.ContainsKey(name) && !replace)
                        {
                            Send(s, 409, "{\"error\":\"exists\"}", "application/json");
                            return;
                        }
                        _uploads[name] = body;
                    }
                    Send(s, 200, "{\"name\":\"" + name + "\"}", "application/json");
                    return;
                }
                case "POST /api/worlds/delete":
                {
                    string name = query.TryGetValue("name", out string? qn) && qn != null ? qn : PhoneWorld;
                    lock (_gate)
                    {
                        if (!_uploads.Remove(name))
                        {
                            Send(s, 404, "{\"error\":\"no such world\"}", "application/json");
                            return;
                        }
                    }
                    Send(s, 200, "{\"ok\":true}", "application/json");
                    return;
                }
                default:
                    Send(s, 404, "{\"error\":\"no such endpoint\"}", "application/json");
                    return;
            }
        }

        /// <summary>手机上的世界:一个固定的 + 所有被上传上来的(upload 后要能被 ListWorlds 看到,否则基准记不住)。</summary>
        private byte[]? WorldBytes(string name)
        {
            if (name == PhoneWorld) return WorldZip(PhoneWorld, "FROM-PHONE");
            lock (_gate) { return _uploads.TryGetValue(name, out byte[]? b) ? b : null; }
        }

        private string WorldsJson()
        {
            var names = new List<string> { PhoneWorld };
            lock (_gate)
            {
                foreach (string n in _uploads.Keys.OrderBy(x => x, StringComparer.Ordinal)) names.Add(n);
            }
            var sb = new StringBuilder();
            sb.Append("{\"root\":{\"id\":\"").Append(RootId).Append("\",\"label\":\"手机存储\",\"exists\":true},");
            sb.Append("\"roots\":[{\"id\":\"global\",\"label\":\"手机存储\",\"exists\":true},");
            sb.Append("{\"id\":\"backup\",\"label\":\"手机备份\",\"exists\":true}],\"worlds\":[");
            for (int i = 0; i < names.Count; i++)
            {
                byte[] data = WorldBytes(names[i]) ?? Array.Empty<byte>();
                if (i > 0) sb.Append(',');
                sb.Append("{\"dirName\":\"").Append(names[i]).Append("\",\"sizeBytes\":").Append(data.Length)
                  .Append(",\"fileCount\":3,\"lastModified\":").Append(FixedMtime).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string ReadHead(NetworkStream s)
        {
            var buf = new MemoryStream();
            var one = new byte[1];
            while (buf.Length < 65536)
            {
                if (s.Read(one, 0, 1) <= 0) break;
                buf.WriteByte(one[0]);
                int l = (int)buf.Length;
                if (l >= 4)
                {
                    byte[] b = buf.GetBuffer();
                    if (b[l - 4] == 13 && b[l - 3] == 10 && b[l - 2] == 13 && b[l - 1] == 10) break;
                }
            }
            return buf.Length == 0 ? "" : Encoding.ASCII.GetString(buf.ToArray());
        }

        private static byte[] ReadBody(NetworkStream s, int len)
        {
            byte[] body = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = s.Read(body, read, len - read);
                if (n <= 0) break;
                read += n;
            }
            return body;
        }

        private static void Send(NetworkStream s, int code, string text, string ctype)
        {
            Send(s, code, Encoding.UTF8.GetBytes(text), ctype);
        }

        private static void Send(NetworkStream s, int code, byte[] body, string ctype)
        {
            string reason = code switch { 200 => "OK", 401 => "Unauthorized", 404 => "Not Found", 409 => "Conflict", _ => "OK" };
            byte[] head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + code + " " + reason + "\r\n" +
                "Content-Type: " + ctype + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Connection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            if (body.Length > 0) s.Write(body, 0, body.Length);
            s.Flush();
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            int q = target.IndexOf('?');
            if (q < 0) return d;
            foreach (string kv in target.Substring(q + 1).Split('&'))
            {
                if (kv.Length == 0) continue;
                int i = kv.IndexOf('=');
                string k = i < 0 ? kv : kv.Substring(0, i);
                string v = i < 0 ? "" : Uri.UnescapeDataString(kv.Substring(i + 1));
                d[k] = v;
            }
            return d;
        }
    }
}

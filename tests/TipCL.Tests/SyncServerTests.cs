using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>在本机回环上跑一遍完整流程：配对 → 列表 → 下载 → 冲突 → 上传。</summary>
public class SyncServerTests
{
	[Fact]
	public void IsLan()
	{
		Assert.True(SyncServer.IsLan(IPAddress.Parse("192.168.1.8")));
		Assert.True(SyncServer.IsLan(IPAddress.Parse("10.0.0.2")));
		Assert.True(SyncServer.IsLan(IPAddress.Parse("172.20.1.1")));
		Assert.True(SyncServer.IsLan(IPAddress.Loopback));
		Assert.False(SyncServer.IsLan(IPAddress.Parse("8.8.8.8")));
		Assert.False(SyncServer.IsLan(IPAddress.Parse("172.40.1.1")));
	}

	[Fact]
	public void FullFlow()
	{
		using TestHome home = new TestHome();
		SyncServer.Start(port: 0);
		try
		{
			Assert.True(SyncServer.Running, SyncServer.LastError);
			string baseUrl = "http://127.0.0.1:" + SyncServer.Port + "/api/sync/v1";
			using HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false });

			JsonElement info = Json(http.GetAsync(baseUrl + "/info").Result, HttpStatusCode.OK);
			Assert.Equal("tipcl-sync", info.GetProperty("service").GetString());
			Assert.Equal(1, info.GetProperty("protocol").GetInt32());

			Assert.Equal(HttpStatusCode.Unauthorized, http.GetAsync(baseUrl + "/targets").Result.StatusCode);
			Assert.Equal(HttpStatusCode.Forbidden, Post(http, baseUrl + "/pair", "{\"code\":\"000000\",\"device_id\":\"d1\"}").StatusCode);

			string code = SyncServer.PairCode;
			JsonElement pair = Json(Post(http, baseUrl + "/pair", "{\"code\":\"" + code + "\",\"device_id\":\"d1\",\"device_name\":\"测试手机\"}"), HttpStatusCode.OK);
			string token = pair.GetProperty("token").GetString();
			Assert.NotEqual(code, SyncServer.PairCode);
			Assert.Single(SyncServer.Devices());
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

			JsonElement targets = Json(http.GetAsync(baseUrl + "/targets").Result, HttpStatusCode.OK).GetProperty("targets");
			Assert.True(targets.GetArrayLength() >= 1);
			string inst = targets[0].GetProperty("instance").GetString();
			SaveSync.Target t = SaveSync.FindTarget(inst, "");
			string worldDir = WorldFixture.Create(t.SavesDir, "同步测试");
			string q = "?instance=" + Uri.EscapeDataString(inst);

			JsonElement worlds = Json(http.GetAsync(baseUrl + "/worlds" + q).Result, HttpStatusCode.OK).GetProperty("worlds");
			JsonElement w = worlds.EnumerateArray().First(x => x.GetProperty("name").GetString() == "同步测试");
			string hash = w.GetProperty("hash").GetString();
			Assert.Equal(SaveSync.WorldHash(worldDir), hash);
			Assert.Equal("测试世界", w.GetProperty("level_name").GetString());

			string worldUrl = baseUrl + "/worlds/" + Uri.EscapeDataString("同步测试") + "/zip" + q;
			HttpResponseMessage dl = http.GetAsync(worldUrl).Result;
			Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
			Assert.Equal(hash, dl.Headers.GetValues("X-TipCL-World-Hash").Single());
			byte[] zip = dl.Content.ReadAsByteArrayAsync().Result;
			Assert.True(zip.Length > 100);

			// 手机端改了一下再传回来，但 base 填错 → 409
			string phoneDir = Path.Combine(home.Dir, "phone");
			Directory.CreateDirectory(phoneDir);
			string phoneWorld = WorldFixture.Create(phoneDir, "同步测试", "手机上玩过", 1729779999000L);
			string upZip = Path.Combine(home.Dir, "up.zip");
			using (FileStream fs = File.Create(upZip))
			{
				SaveSync.ExportZip(phoneWorld, fs);
			}
			byte[] up = File.ReadAllBytes(upZip);
			JsonElement conflict = Json(http.PutAsync(worldUrl + "&base=deadbeef", new ByteArrayContent(up)).Result, HttpStatusCode.Conflict);
			Assert.Equal("conflict", conflict.GetProperty("error").GetString());

			// base 正确 → 覆盖成功，旧的自动备份
			JsonElement done = Json(http.PutAsync(worldUrl + "&base=" + hash, new ByteArrayContent(up)).Result, HttpStatusCode.OK);
			Assert.Equal("手机上玩过", done.GetProperty("level_name").GetString());
			Assert.False(string.IsNullOrEmpty(done.GetProperty("backup").GetString()));
			Assert.Equal(SaveSync.WorldHash(worldDir), done.GetProperty("hash").GetString());
			Assert.Equal("手机上玩过", LevelDat.ReadFile(Path.Combine(worldDir, "level.dat")).LevelName);

			// 新世界：base 为空即可创建
			JsonElement created = Json(http.PutAsync(baseUrl + "/worlds/" + Uri.EscapeDataString("新世界") + "/zip" + q, new ByteArrayContent(up)).Result, HttpStatusCode.OK);
			Assert.Equal("新世界", created.GetProperty("name").GetString());

			// 不是存档的 zip → 400
			Assert.Equal(HttpStatusCode.BadRequest, http.PutAsync(baseUrl + "/worlds/x/zip" + q + "&force=1", new ByteArrayContent(Encoding.ASCII.GetBytes("nope"))).Result.StatusCode);
			Assert.Equal(HttpStatusCode.BadRequest, http.GetAsync(baseUrl + "/worlds/" + Uri.EscapeDataString(".hidden") + "/zip" + q).Result.StatusCode);

			// 取消配对后令牌失效
			Assert.Equal(HttpStatusCode.OK, http.DeleteAsync(baseUrl + "/pair").Result.StatusCode);
			Assert.Equal(HttpStatusCode.Unauthorized, http.GetAsync(baseUrl + "/targets").Result.StatusCode);
		}
		finally
		{
			SyncServer.Stop();
		}
	}

	[Fact]
	public void WrongCodesRotate()
	{
		using TestHome home = new TestHome();
		SyncServer.Start(port: 0);
		try
		{
			string code = SyncServer.PairCode;
			for (int i = 0; i < 5; i++)
			{
				Assert.Null(SyncServer.Pair("x", "d", "n"));
			}
			Assert.NotEqual(code, SyncServer.PairCode);
			Assert.Null(SyncServer.Pair(code, "d", "n"));
		}
		finally
		{
			SyncServer.Stop();
		}
	}

	private static HttpResponseMessage Post(HttpClient http, string url, string json)
	{
		return http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json")).Result;
	}

	private static JsonElement Json(HttpResponseMessage r, HttpStatusCode expected)
	{
		string body = r.Content.ReadAsStringAsync().Result;
		Assert.True(r.StatusCode == expected, (int)r.StatusCode + " " + body);
		return JsonDocument.Parse(body).RootElement.Clone();
	}
}

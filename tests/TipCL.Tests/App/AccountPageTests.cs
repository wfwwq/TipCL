using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TipCL.App.Common;
using TipCL.App.Pages;
using TipCL.Core;
using TipCL.Core.Launcher;
using Xunit;

namespace TipCL.Tests.App;

/// <summary>
/// 账号链路回归(2026-11-11 接线 get_account_rows / add_offline_account / set·remove_account /
/// skin_urls / get·set_account_skin + 三种登录的 start_* 任务入口)。
/// 登录本身联网,这里只锁「入口接线 + 离线账号与皮肤往返」。
/// </summary>
public class AccountPageTests
{
    private readonly IApi _api = InProcessApi.Instance;

    private static string UserName() => "c3-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    [Fact]
    public void OfflineAccount_AddListSetActiveRemove() => TestApp.RunAsync(async () =>
    {
        string name = UserName();
        try
        {
            await _api.CallAsync<object>("add_offline_account", new PyDict { ["username"] = name, ["skin"] = "steve" });

            var rows = await _api.CallAsync<List<object>>("get_account_rows");
            var mine = rows!.Find(r => Py.S(Py.D(r).Get("name")) == name);
            Assert.NotNull(mine);
            var d = Py.D(mine!);
            Assert.Equal("offline", Py.S(d.Get("type")));
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("uuid"))));
            Assert.False(string.IsNullOrEmpty(Py.S(d.Get("avatar"))), "离线账号也该有头像地址");

            await _api.CallAsync<object>("set_active_account", new PyDict { ["name"] = name });
            rows = await _api.CallAsync<List<object>>("get_account_rows");
            Assert.True(Py.Truthy(Py.D(rows!.Find(r => Py.S(Py.D(r).Get("name")) == name)!)["active"]));

            var urls = await _api.CallAsync<PyDict>("skin_urls", new PyDict { ["account_name"] = name });
            Assert.NotNull(urls);
            Assert.False(string.IsNullOrEmpty(Py.S(urls!["avatar"])));
        }
        finally
        {
            await _api.CallAsync<object>("remove_account", new PyDict { ["name"] = name });
        }
        var after = await _api.CallAsync<List<object>>("get_account_rows");
        Assert.DoesNotContain(after!, r => Py.S(Py.D(r).Get("name")) == name);
    });

    [Fact]
    public void AccountSkin_SetAndClear_RoundTrip() => TestApp.RunAsync(async () =>
    {
        string name = UserName();
        try
        {
            await _api.CallAsync<object>("add_offline_account", new PyDict { ["username"] = name, ["skin"] = "default" });

            var empty = await _api.CallAsync<PyDict>("get_account_skin", new PyDict { ["name"] = name });
            Assert.Equal("", Py.S(empty!["data_url"]));

            // 64×64 半透明 PNG(Skin.ValidateSkin 只认尺寸合法)
            await _api.CallAsync<object>("set_account_skin", new PyDict
            {
                ["name"] = name,
                ["data"] = "data:image/png;base64," + Convert.ToBase64String(TinySkinPng()),
                ["model"] = "slim",
            });

            var set = await _api.CallAsync<PyDict>("get_account_skin", new PyDict { ["name"] = name });
            Assert.False(string.IsNullOrEmpty(Py.S(set!["skin_file"])));
            Assert.True(Py.Truthy(set["slim"]));
            Assert.StartsWith("data:image/png;base64,", Py.S(set["data_url"]));

            // 正面视图合成:输出固定 128×256,脸(皮肤 8..16,8..16)落在输出 x 32..96 / y 0..64
            byte[] png = SkinPng.Decode(Py.S(set!["data_url"]))!;
            var front = SkinPng.FrontView(png, slim: true);
            Assert.NotNull(front);
            Assert.Equal(128, front!.PixelSize.Width);
            Assert.Equal(256, front.PixelSize.Height);
            var (r, g, bl, alpha) = PixelAt(front, 64, 32);
            Assert.Equal((200, 50, 30, 255), (r, g, bl, alpha));

            // 清空
            await _api.CallAsync<object>("set_account_skin", new PyDict { ["name"] = name });
            var cleared = await _api.CallAsync<PyDict>("get_account_skin", new PyDict { ["name"] = name });
            Assert.Equal("", Py.S(cleared!["data_url"]));
        }
        finally
        {
            await _api.CallAsync<object>("remove_account", new PyDict { ["name"] = name });
        }
    });

    [Fact]
    public void LoginEntries_ReturnTaskIds_AndBogusAuthlibFailsTheTask() => TestApp.RunAsync(async () =>
    {
        var presets = await _api.CallAsync<List<object>>("authlib_presets");
        Assert.NotEmpty(presets!);
        Assert.False(string.IsNullOrEmpty(Py.S(Py.D(presets![0]).Get("api"))));

        // 假地址:任务应当被创建并最终以失败收尾(不是同步抛异常)
        string id = await _api.CallAsync<string>("start_authlib_login", new PyDict
        {
            ["api"] = "http://127.0.0.1:1/api/yggdrasil",
            ["username"] = "nobody",
            ["password"] = "nope",
        }) ?? "";
        Assert.False(string.IsNullOrEmpty(id));

        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            var row = TaskStore.Get(id);
            if (row is { Finished: true })
            {
                Assert.False(row.Success);
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("登录任务未在 40s 内收尾");
    });

    /// <summary>64×64 皮肤 PNG:脸部正面(8..16, 8..16)涂成 RGB(200,50,30),其余透明。</summary>
    private static byte[] TinySkinPng()
    {
        var wb = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = wb.Lock())
        {
            var row = new byte[64 * 4];
            for (int y = 0; y < 64; y++)
            {
                Array.Clear(row);
                if (y >= 8 && y < 16)
                    for (int x = 8; x < 16; x++)
                    {
                        row[x * 4 + 0] = 30;   // B
                        row[x * 4 + 1] = 50;   // G
                        row[x * 4 + 2] = 200;  // R
                        row[x * 4 + 3] = 255;  // A
                    }
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
        }
        using var ms = new MemoryStream();
        wb.Save(ms);
        return ms.ToArray();
    }

    private static (int R, int G, int B, int A) PixelAt(Bitmap bmp, int x, int y)
    {
        using var copy = new WriteableBitmap(bmp.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = copy.Lock();
        bmp.CopyPixels(fb, AlphaFormat.Unpremul);
        var px = new byte[4];
        Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
        return (px[2], px[1], px[0], px[3]);
    }
}

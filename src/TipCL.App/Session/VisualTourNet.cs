// AI3-visual:巡检进程的出网闸门(配合 Core 的 HttpHandlerHook)。
// i18n:ignore-file —— 假源里的示例标题是巡检用的假数据,不是界面文案。
//
// 规则(巡检进程不联网):
//   * Modrinth 搜索(api.modrinth.com / mod.mcimirror.top/modrinth 的 /v2/search)→ 回一份固定的假结果,
//     让下载页的 Mod / 整合包 / 资源包 / 光影 / 数据包 分段有内容可比;
//   * 其余一切请求当场回 503(不重试也不等超时)—— 版本清单、新闻走 VisualFixture 预置的缓存
//     (Manifest / News 自己的「拉不到就用缓存」机制),CurseForge 世界分段如实落空态。
// 每个被挡 / 被假答的 URL 都记下来,写进 tour-result.json。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Core.Launcher;

namespace TipCL.App.Session;

internal static class TourNet
{
    /// <summary>(URL, 处置) 记录:FAKE = 假答、BLOCKED = 当场 503。</summary>
    public static readonly ConcurrentQueue<(string Url, string Action)> Log = new();

    /// <summary>装上闸门(必须早于任何 HttpClient 被建出来)。</summary>
    public static void Install()
    {
        HttpHandlerHook.Wrap = (inner, channel) => new Gate(inner);
    }

    internal static bool IsModrinthSearch(Uri? uri)
    {
        if (uri == null) return false;
        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath;
        bool modrinth = host == "api.modrinth.com" || (host == "mod.mcimirror.top" && path.StartsWith("/modrinth", StringComparison.Ordinal));
        return modrinth && path.EndsWith("/search", StringComparison.Ordinal);
    }

    /// <summary>从 facets 里认出要的是哪类项目(mod / modpack / resourcepack / shader / datapack)。</summary>
    internal static string ProjectType(Uri uri)
    {
        string q = Uri.UnescapeDataString(uri.Query);
        foreach (string t in new[] { "modpack", "resourcepack", "shader", "datapack", "mod" })
        {
            if (q.Contains("project_type:" + t, StringComparison.Ordinal)) return t;
        }
        return "mod";
    }

    private static readonly Dictionary<string, (string Slug, string Title, string Desc, string Author, long Downloads)[]> Samples = new()
    {
        ["mod"] = new[]
        {
            ("sodium", "Sodium", "Modern rendering engine and client-side optimization mod", "jellysquid3", 98_000_000L),
            ("fabric-api", "Fabric API", "Lightweight and modular API providing common hooks", "modmuss50", 120_000_000L),
            ("iris", "Iris Shaders", "A modern shader pack loader for Minecraft", "coderbot", 61_000_000L),
            ("lithium", "Lithium", "No-compromises game logic optimization mod", "jellysquid3", 45_000_000L),
            ("modmenu", "Mod Menu", "Adds a mod menu to view the list of mods you have installed", "Prospector", 52_000_000L),
            ("jei", "Just Enough Items", "View items and recipes", "mezz", 30_000_000L),
            ("xaeros-minimap", "Xaero's Minimap", "Displays the map of the world around you", "xaero96", 28_000_000L),
            ("appleskin", "AppleSkin", "Food and hunger information", "squeek502", 25_000_000L),
        },
        ["modpack"] = new[]
        {
            ("fabulously-optimized", "Fabulously Optimized", "Improve your graphics and performance with this simple modpack", "robotkoer", 9_000_000L),
            ("cobblemon-fabric", "Cobblemon Official Modpack", "The official modpack of Cobblemon", "cobblemon", 3_000_000L),
            ("better-mc-forge-bmc4", "Better MC [FORGE] - BMC4", "Minecraft but better", "SharkieTV", 2_500_000L),
            ("simply-optimized", "Simply Optimized", "The purpose of this modpack is to be a starting point", "Hexeption", 1_800_000L),
        },
        ["resourcepack"] = new[]
        {
            ("faithful-32x", "Faithful 32x", "The classic Minecraft look, in double resolution", "Faithful", 6_000_000L),
            ("fresh-animations", "Fresh Animations", "Breathe new life into Minecraft's mobs", "FreshLX", 5_200_000L),
            ("xalis-enhanced-vanilla", "Xali's Enhanced Vanilla", "Subtle improvements to vanilla textures", "xali", 1_200_000L),
        },
        ["shader"] = new[]
        {
            ("complementary-reimagined", "Complementary Shaders - Reimagined", "Vanilla-style shader with great performance", "EminGT", 11_000_000L),
            ("bsl-shaders", "BSL Shaders", "Bright, colourful and performant", "capttatsu", 9_500_000L),
            ("photon-shader", "Photon Shader", "A gameplay-focused shader with realistic lighting", "sixthsurge", 2_300_000L),
        },
        ["datapack"] = new[]
        {
            ("terralith", "Terralith", "Explore almost 100 new biomes", "Stardust Labs", 7_000_000L),
            ("incendium", "Incendium", "Nether expansion", "Stardust Labs", 2_000_000L),
            ("dungeons-and-taverns", "Dungeons and Taverns", "New structures and dungeons", "nova", 1_500_000L),
        },
    };

    /// <summary>按 Modrinth /v2/search 的形状造一份假结果。</summary>
    internal static string FakeSearchJson(string projectType)
    {
        if (!Samples.TryGetValue(projectType, out var rows)) rows = Samples["mod"];
        var hits = rows.Select(r => new Dictionary<string, object?>
        {
            ["project_id"] = "fake-" + r.Slug,
            ["slug"] = r.Slug,
            ["title"] = r.Title,
            ["description"] = r.Desc,
            ["author"] = r.Author,
            ["downloads"] = r.Downloads,
            ["follows"] = r.Downloads / 1000,
            ["icon_url"] = "",
            ["project_type"] = projectType,
            ["categories"] = new[] { "fabric", "optimization" },
            ["display_categories"] = new[] { "optimization" },
            ["versions"] = new[] { "1.20.1", "1.21.4", "26.3" },
            ["latest_version"] = "26.3",
            ["date_modified"] = "2026-11-01T00:00:00Z",
            ["date_created"] = "2021-01-01T00:00:00Z",
            ["client_side"] = "required",
            ["server_side"] = "optional",
        }).ToList();
        var payload = new Dictionary<string, object?>
        {
            ["hits"] = hits,
            ["offset"] = 0,
            ["limit"] = hits.Count,
            ["total_hits"] = hits.Count,
        };
        return JsonSerializer.Serialize(payload);
    }

    private sealed class Gate : DelegatingHandler
    {
        public Gate(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri? uri = request.RequestUri;
            string url = uri?.ToString() ?? "";
            if (IsModrinthSearch(uri))
            {
                Log.Enqueue((url, "FAKE"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(FakeSearchJson(ProjectType(uri!)), Encoding.UTF8, "application/json"),
                });
            }
            Log.Enqueue((url, "BLOCKED"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = new StringContent("visual tour: offline", Encoding.UTF8, "text/plain"),
            });
        }
    }
}

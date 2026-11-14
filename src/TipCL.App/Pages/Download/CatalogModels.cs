// 来源: Windows 版参考仓库 src/TipCL/Models/{CatalogKind,CatalogItem,CatalogFile,ModEntry,ModsTarget,SaveRow,ExportResult}.cs
// (M4-H P1 移植:合成一个文件、去掉 WPF 依赖;字段名与 JSON 名保持与 Windows 版一致,
//  经 InProcessApi.Coerce 从 Core 返回的 PyDict 反序列化落 POCO)。
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TipCL.App.Pages.Download;

/// <summary>目录类别(照 Windows Models/CatalogKind):六分段各自的方法名、类型标签、文件类型。</summary>
public sealed class CatalogKind
{
    public static readonly CatalogKind Mod = new()
    {
        Key = "mod",
        Title = "Mod",
        Icon = "Puzzle",
        SearchMethod = "search_mods",
        InstallMethod = "install_mod",
        InstalledMethod = "get_installed_mods",
        DeleteMethod = "delete_mod",
        Empty = "没有找到相关模组",
        LinkHint = "模组链接 / .jar 直链",
        LocalFilter = "*.jar",
        Types = new[] { "全部", "优化", "科技", "魔法", "冒险", "建筑", "工具" },
        FileKind = "mod",
        DefaultSource = "Modrinth",
    };

    public static readonly CatalogKind Modpack = new()
    {
        Key = "modpack",
        Title = "整合包",
        Icon = "Package",
        SearchMethod = "search_modpacks",
        InstallMethod = "install_modpack",
        InstalledMethod = "get_installed_modpacks",
        DeleteMethod = "delete_modpack",
        Empty = "没有找到相关整合包",
        LinkHint = "整合包链接 / .mrpack / .zip",
        LocalFilter = "*.mrpack;*.zip",
        IsModpack = true,
        Types = new[] { "全部", "生存", "空岛", "科技", "魔法", "冒险" },
        FileKind = "modpack",
        DefaultSource = "Modrinth",
    };

    public static readonly CatalogKind Datapack = new()
    {
        Key = "datapack",
        Title = "数据包",
        Icon = "List",
        SearchMethod = "search_datapacks",
        InstallMethod = "install_datapack",
        InstalledMethod = "get_installed_datapacks",
        DeleteMethod = "delete_datapack",
        Empty = "没有找到相关数据包",
        LinkHint = "数据包下载链接",
        LocalFilter = "*.zip",
        Types = new[] { "全部", "生存", "冒险", "装饰", "工具" },
        FileKind = "datapack",
    };

    public static readonly CatalogKind ResourcePack = new()
    {
        Key = "resourcepack",
        Title = "资源包",
        Icon = "Image",
        SearchMethod = "search_resourcepacks",
        InstallMethod = "install_resourcepack",
        InstalledMethod = "get_installed_resourcepacks",
        DeleteMethod = "delete_resourcepack",
        Empty = "没有找到相关资源包",
        LinkHint = "资源包下载链接",
        LocalFilter = "*.zip",
        Types = new[] { "全部", "16x", "32x", "64x", "写实", "现代风", "动态效果" },
        FileKind = "resourcepack",
    };

    public static readonly CatalogKind Shader = new()
    {
        Key = "shader",
        Title = "光影包",
        Icon = "Sun",
        SearchMethod = "search_shaders",
        InstallMethod = "install_shader",
        InstalledMethod = "get_installed_shaders",
        DeleteMethod = "delete_shader",
        Empty = "没有找到相关光影",
        LinkHint = "光影包下载链接",
        LocalFilter = "*.zip",
        Types = new[] { "全部", "写实", "卡通", "高性能", "光追" },
        FileKind = "shader",
    };

    public static readonly CatalogKind World = new()
    {
        Key = "world",
        Title = "世界",
        Icon = "World",
        SearchMethod = "search_worlds",
        InstallMethod = "install_world",
        InstalledMethod = "",
        DeleteMethod = "",
        Empty = "没有找到相关世界地图",
        LinkHint = "世界地图下载链接",
        LocalFilter = "*.zip",
        Types = new[] { "全部", "生存", "冒险", "创造", "跑酷" },
        FileKind = "world",
        DefaultSource = "CurseForge",
    };

    /// <summary>分段顺序与 Windows 版 DownloadPage.Kinds 一致(原版分段不在其中)。</summary>
    public static readonly CatalogKind[] All = { Mod, Modpack, Datapack, ResourcePack, Shader, World };

    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string SearchMethod { get; set; } = "";
    public string InstallMethod { get; set; } = "";
    public string InstalledMethod { get; set; } = "";
    public string DeleteMethod { get; set; } = "";
    public string Empty { get; set; } = "";
    public string LinkHint { get; set; } = "";
    public string LocalFilter { get; set; } = "";
    public bool IsModpack { get; set; }
    public string[] Types { get; set; } = Array.Empty<string>();
    public string FileKind { get; set; } = "mod";
    public string DefaultSource { get; set; } = "全部";
    public string Icon { get; set; } = "";
}

/// <summary>目录搜索结果行(照 Windows Models/CatalogItem)。</summary>
public sealed class CatalogItem
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public long Downloads { get; set; }
    public JsonElement? Id { get; set; }
    public string? Slug { get; set; }
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string>? Tags { get; set; }
    public string Updated { get; set; } = "";

    [JsonPropertyName("game_version")]
    public string GameVersion { get; set; } = "";

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; } = "";

    [JsonPropertyName("project_url")]
    public string ProjectUrl { get; set; } = "";

    [JsonIgnore]
    public object? IdValue
    {
        get
        {
            if (Id is not { } id) return null;
            return id.ValueKind switch
            {
                JsonValueKind.Number => id.TryGetInt64(out long v) ? v : id.GetDouble(),
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Undefined or JsonValueKind.Null => null,
                _ => id.ToString(),
            };
        }
    }

    /// <summary>收藏去重键(照 Windows CatalogPage.FavKey:source + slug/id/name)。</summary>
    public string FavKey => Source + "|" + (Slug is { Length: > 0 } s ? s : IdValue?.ToString() ?? Name);
}

/// <summary>项目版本文件(照 Windows Models/CatalogFile)。</summary>
public sealed class CatalogFile
{
    public JsonElement? Id { get; set; }
    public string Name { get; set; } = "";

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = "";

    public string Filename { get; set; } = "";

    [JsonPropertyName("game_versions")]
    public List<string>? GameVersions { get; set; }

    public List<string>? Loaders { get; set; }
    public string Date { get; set; } = "";
    public long Downloads { get; set; }

    [JsonPropertyName("release_type")]
    public string ReleaseType { get; set; } = "";

    public string Source { get; set; } = "";

    [JsonIgnore]
    public object? IdValue
    {
        get
        {
            if (Id is not { } id) return null;
            return id.ValueKind switch
            {
                JsonValueKind.Number => id.TryGetInt64(out long v) ? v : id.GetDouble(),
                JsonValueKind.String => id.GetString(),
                _ => null,
            };
        }
    }
}

/// <summary>已装模组行(照 Windows Models/ModEntry)。</summary>
public sealed class ModEntry
{
    public string Filename { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Version { get; set; } = "";
    public string Size { get; set; } = "";
}

/// <summary>已装版本的 mods 目标(照 Windows Models/ModsTarget)。</summary>
public sealed class ModsTarget
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    public override string ToString() => Label;
}

/// <summary>
/// 存档行(数据包「装进哪个存档」用)。
/// 形状按 **list_saves 的真实返回** 对齐(Saves.ListSaves):{name, path, icon, bytes, mtime},
/// 其中 mtime 是 epoch 秒的**数字**(Core 侧 LocalFs.MTimeInt 返回 long)—— 所以这里必须是数字类型:
/// 声明成 string 会让 Api.Coerce 在反序列化时抛 JsonException,整批行被判成转换失败,
/// 下拉拿到空列表后直接提前返回(真机现象:下载页「装进存档」永远打不开)。
/// 其余几个字段是参考版留下来的显示位,list_saves 不发它们,读到的就是默认值。
/// </summary>
public sealed class SaveRow
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Size { get; set; } = "";

    /// <summary>目录 mtime,epoch 秒。数字与数字字符串都能接住(JsonOpts 开了 AllowReadingFromString)。</summary>
    public long Mtime { get; set; }

    [JsonPropertyName("last_played")]
    public string LastPlayed { get; set; } = "";

    public string Mode { get; set; } = "";
    public string Seed { get; set; } = "";
}

/// <summary>目录页用到的数字格式化(照 Windows Services/Fmt.cs 的 Downloads,中文口径)。</summary>
public static class CatalogFmt
{
    public static string Downloads(long n)
    {
        if (n >= 10000)
            return n >= 100000000
                ? (n / 100000000.0).ToString("0.#") + " 亿"
                : (n / 10000.0).ToString("0.#") + " 万";
        return n > 0 ? n.ToString("0") : "—";
    }
}

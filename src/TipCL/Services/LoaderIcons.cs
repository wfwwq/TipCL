namespace TipCL.Services;

// 加载器官方图标：不嵌入 exe（体积红线），运行时经 Thumbs（后端 ensure_thumb → cache_dir/thumbs）
// 下载并落盘缓存；未下载 / 离线 / 失败时 ThumbTile 显示名称首字母方块。全程异步，异常在 Thumbs 内吞掉。
public static class LoaderIcons
{
	public static string? Url(string loader)
	{
		switch (loader.ToLowerInvariant())
		{
		case "fabric":
			return "https://github.com/FabricMC.png?size=64";
		case "forge":
			return "https://github.com/MinecraftForge.png?size=64";
		case "neoforge":
			return "https://github.com/neoforged.png?size=64";
		case "quilt":
			return "https://github.com/QuiltMC.png?size=64";
		case "":
		case "vanilla":
			return "https://minecraft.wiki/images/Grass_Block_JE7_BE6.png";
		default:
			return null;
		}
	}

	public static ThumbTile Tile(string loader, string name, double size = 36.0)
	{
		ThumbTile t = new ThumbTile(name, size, 9.0);
		t.SetUrl(Url(loader));
		return t;
	}
}

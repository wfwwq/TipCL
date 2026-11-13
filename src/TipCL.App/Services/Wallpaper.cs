using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using TipCL.Themes;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace TipCL.Services;

/// <summary>
/// 背景管线(壁纸):单图 / 文件夹轮播 / 模糊 / 遮罩,以及"壁纸激活时外壳各处该长什么样"
/// (= Windows <c>Services\Wallpaper.cs</c>,公开面与语义逐条对齐)。
///
/// 保持一致的部分:<c>Error</c> / <c>Active</c> / <c>ChromeInvalidated</c> / <c>Attach</c> /
/// <c>SetWindowActive</c> / <c>ReloadAsync</c> / <c>SetSource</c> / <c>SetEffects</c> /
/// <c>OnThemeChanged</c> / <c>Shutdown</c>;单图解码宽度 1920;模糊夹 0-40、遮罩夹 0-80;
/// 轮播间隔夹 1-1440 分钟;文件夹扫描按 <see cref="FileKinds.IsWallpaper"/> 过滤 + 序号忽略大小写;
/// <c>ApplyChrome</c> 那套"有壁纸 → 玻璃标题栏 / 根边框兜底不透明 / 页面底纹透明 / 遮罩出现"的状态机,
/// 以及"从来没有过壁纸时不要动 XAML 默认"(<c>if (!on &amp;&amp; !_chromeOn) return;</c>)。
///
/// mac 侧差异(逐条记在 PROGRESS.md 的 C2c2 差异表):
/// <list type="number">
/// <item><b>视频壁纸本版不支持</b>:Windows 用 WPF <c>MediaElement</c> 静音循环播 mp4;Avalonia 11
///   没有内建播放器,要支持得引 LibVLC 之类的新依赖(超出本包授权)。所以把播放器做成注入缝
///   <see cref="VideoHost"/>,默认 null → 记 <c>Error</c> 并保持 <c>Active = false</c>(绝不假装放上了);</item>
/// <item><b>内置预设图</b>:任务书说"预设已内嵌在 Core 的 <c>Resources\wallpapers</c>",实测
///   **Core 里没有**(Core 只嵌了 locales / ai / plugin-host),所以按 Windows 的做法把
///   <c>Resources\wallpapers\*.jpg</c> 嵌进 App(AvaloniaResource),并用 <see cref="PresetUri"/> /
///   <see cref="ReleasePreset"/> 两个口子替掉页面的 <c>pack://</c> 拼串;</item>
/// <item><b>不再直接调 Backdrop / SideFrost</b>(它们属 C2c3 的视觉件,本包的红线不许碰):
///   改为 <see cref="BackdropRefresh"/> / <see cref="SideFrostRefresh"/> 两个注入缝,
///   C2c3 落地时接上即可(现在为 null = 不调,不影响壁纸本身);</item>
/// <item><c>SetResourceReference</c> → <see cref="UiExt.Dyn{T}"/>;WPF 的 <c>Rectangle</c> →
///   Avalonia 的 <c>Avalonia.Controls.Shapes.Rectangle</c>;轮播计时器做成注入缝
///   <see cref="TimerFactory"/>,与 C2c1 的 Toast 计时器同一套纪律(没有时钟的环境里不挂计时器);</item>
/// <item><c>Attach</c> 会把上一次挂上去的两个图层从旧宿主里摘掉(mac 侧一个进程里会有多个外壳;
///   Windows 一辈子只有一个 MainWindow,不摘也不会出事)。</item>
/// </list>
/// </summary>
public static class Wallpaper
{
	/// <summary>文件夹轮播的播放表(Windows 的私有嵌套类,逐行等价)。</summary>
	private sealed class Playlist
	{
		private string _folder = "";

		private bool _shuffle;

		private List<string> _files = new List<string>();

		private string _current = "";

		private readonly Random _rng = new Random();

		public bool SetFolder(string folder, bool shuffle)
		{
			folder = (folder ?? "").Trim();
			_shuffle = shuffle;
			if (folder != _folder)
			{
				_folder = folder;
				_current = "";
			}

			Scan();
			return _files.Count > 0;
		}

		public string Current()
		{
			if (_current.Length <= 0 || !File.Exists(_current))
			{
				return Advance();
			}

			return _current;
		}

		public string Advance()
		{
			Scan();
			if (_files.Count == 0)
			{
				_current = "";
				return _current;
			}

			if (_files.Count == 1)
			{
				_current = _files[0];
				return _current;
			}

			if (_shuffle)
			{
				string picked;
				do
				{
					picked = _files[_rng.Next(_files.Count)];
				}
				while (picked == _current);

				_current = picked;
				return _current;
			}

			int index = _files.IndexOf(_current);
			_current = _files[((index >= 0) ? (index + 1) : 0) % _files.Count];
			return _current;
		}

		public int Count => _files.Count;

		private void Scan()
		{
			if (_folder.Length == 0 || !Directory.Exists(_folder))
			{
				_files = new List<string>();
				return;
			}

			try
			{
				_files = Directory.EnumerateFiles(_folder)
					.Where(FileKinds.IsWallpaper)
					.OrderBy((string p) => p, StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			catch
			{
				_files = new List<string>();
			}
		}
	}

	/// <summary>读设置的口子(注入缝):默认走后端 <c>get_setting</c>(与 Windows 的 6 次调用逐字对应)。</summary>
	internal interface ISettingsSource
	{
		Task<string> GetStringAsync(string key, string fallback);

		Task<bool> GetBoolAsync(string key, bool fallback);

		Task<int> GetIntAsync(string key, int fallback);
	}

	/// <summary>轮播计时器的注入缝(与 C2c1 的 <c>ToastTimer</c> 同一套:返回 null = 这次没起计时器)。</summary>
	internal delegate IDisposable? WallpaperTimer(TimeSpan delay, Action onFire);

	/// <summary>视频壁纸的播放后端(Windows 是 WPF 的 <c>MediaElement</c>;mac 侧默认为 null = 不支持)。</summary>
	internal interface IVideoHost
	{
		void Start(string path);

		void SetActive(bool active);

		void Stop();
	}

	private static Grid? _host;

	private static Border? _root;

	private static Border? _chromeBar;

	private static Border? _side;

	private static Rectangle? _wash;

	private static readonly Image _still = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private static readonly Rectangle _dim = new Rectangle();

	private static readonly Playlist _playlist = new Playlist();

	private static string _source = "";

	private static int _blur;

	private static int _dimPercent;

	private static bool _windowActive = true;

	private static bool _chromeOn;

	private static IDisposable? _rotation;

	private static IVideoHost? _video;

	private static readonly ISettingsSource Bridge = new BridgeSettingsSource();

	/// <summary>最近一次的错误原因(空串 = 没有)。Windows 同名成员。</summary>
	public static string Error { get; private set; } = "";

	/// <summary>有没有正在生效的背景(Windows 同名成员)。</summary>
	public static bool Active => !string.IsNullOrEmpty(_source);

	/// <summary>壁纸开关 / 轮换后的窗口级联动(Windows 同名成员;mac 侧由外壳决定接什么)。</summary>
	public static Action? ChromeInvalidated;

	/// <summary>设置来源的注入缝(null = 后端;<see cref="AppServices.Ready"/> 为假时整条管线不跑,与 Windows 同)。</summary>
	internal static ISettingsSource? Settings { get; set; }

	/// <summary>轮播计时器的注入缝(null = 真桌面用 <c>DispatcherTimer</c>)。</summary>
	internal static WallpaperTimer? TimerFactory { get; set; }

	/// <summary>视频播放后端的注入缝(null = 本版不支持视频壁纸,见类注释差异 1)。</summary>
	internal static IVideoHost? VideoHost { get; set; }

	/// <summary>DWM / 窗口背板的刷新口(C2c3 的 <c>Backdrop</c> 接进来;本包只负责喊一声)。</summary>
	internal static Action? BackdropRefresh { get; set; }

	/// <summary>侧栏霜化的刷新口(C2c3 的 <c>SideFrost</c> 接进来)。</summary>
	internal static Action? SideFrostRefresh { get; set; }

	/// <summary>当前生效的背景路径(空串 = 没有)。</summary>
	internal static string CurrentSource => _source;

	/// <summary>当前模糊半径(夹过 0-40)。</summary>
	internal static int BlurValue => _blur;

	/// <summary>当前遮罩浓度(夹过 0-80)。</summary>
	internal static int DimPercent => _dimPercent;

	/// <summary>轮播计时器还在不在(测试断言"没有残留计时器")。</summary>
	internal static bool Rotating => _rotation is not null;

	/// <summary>播放表里的文件数(测试断言"文件夹扫描到了几张")。</summary>
	internal static int PlaylistCount => _playlist.Count;

	/// <summary>单图层(测试断言挂进宿主了没有、图像位图对不对)。</summary>
	internal static Image StillLayer => _still;

	/// <summary>遮罩层(测试断言透明度与可见性)。</summary>
	internal static Rectangle DimLayer => _dim;

	/// <summary>视频层在不在(Windows 是 <c>_video != null</c>)。</summary>
	internal static bool VideoPlaying => _video is not null;

	// ---------------------------------------------------------------- 内置预设背景

	/// <summary>【Windows 源】<c>SettingsPage.cs:635-641</c> —— 四个内置预设(文件名 + 中文标签,顺序一字不改)。</summary>
	internal static readonly (string File, string Label)[] Presets =
	{
		("preset-morning", "晨雾"),
		("preset-dusk", "暮色"),
		("preset-night", "星夜"),
		("preset-sakura", "樱粉"),
	};

	/// <summary>【Windows 源】<c>SettingsPage.cs:643</c> 的 <c>pack://application:,,,/TipCL;component/Resources/wallpapers/</c>
	/// —— mac 侧换成 avares(嵌入资源在同一程序集的 <c>Resources\wallpapers</c> 下)。</summary>
	internal const string PresetUriPrefix = "avares://TipCL/Resources/wallpapers/";

	/// <summary>预设的嵌入资源 URI(未知名字也照样拼,与 Windows 拼串后取不到资源的结果一致)。</summary>
	internal static string PresetUri(string name)
	{
		return PresetUriPrefix + name + ".jpg";
	}

	/// <summary>【Windows 源】<c>SettingsPage.cs:664</c> —— 预设释放到数据目录的落点
	/// (<c>&lt;root&gt;/wallpapers/&lt;name&gt;.jpg</c>)。</summary>
	internal static string PresetPath(string name)
	{
		return Path.Combine(BridgeHost.FindRoot(), "wallpapers", name + ".jpg");
	}

	/// <summary>是不是内置预设的名字(Windows 侧就是一个四元组的 <c>foreach</c>,没有这个名字的查询)。</summary>
	internal static bool IsKnownPreset(string name)
	{
		return Presets.Any(((string File, string Label) p) => p.File == name);
	}

	/// <summary>
	/// 打开预设的嵌入资源流(= Windows <c>Application.GetResourceStream(...)</c>)。
	/// 名字不认识 / 资源缺失 → null(Windows 那边是抛异常,页面在 <c>catch</c> 里报「预设背景应用失败」)。
	/// </summary>
	internal static Stream? OpenPreset(string name)
	{
		try
		{
			return AssetLoader.Open(new Uri(PresetUri(name)));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// 把预设释放到数据目录(= Windows <c>SettingsPage</c> 里"建目录 + 有了就不覆盖 + 拷字节"那一段)。
	/// 返回目标路径;名字不认识 / 拷不动 → 空串(页面据此报错,不静默成功)。
	/// </summary>
	internal static string ReleasePreset(string name)
	{
		try
		{
			string destination = PresetPath(name);
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			if (File.Exists(destination))
			{
				return destination;
			}

			using Stream? source = OpenPreset(name);
			if (source is null)
			{
				return "";
			}

			using FileStream target = File.Create(destination);
			source.CopyTo(target);
			return destination;
		}
		catch
		{
			return "";
		}
	}

	// ---------------------------------------------------------------- 公开面(与 Windows 一致)

	/// <summary>把三个外壳图层交给管线(Windows 同名方法的参数顺序一致:<c>host/root/chromeBar/side/wash</c>)。</summary>
	public static void Attach(Grid host, Border root, Border chromeBar, Border? side, Rectangle? wash)
	{
		// mac 侧:上一次挂上去的那两个静态图层可能还在旧宿主里(Avalonia 不许一个控件有两个父)。
		(_still.Parent as Panel)?.Children.Remove(_still);
		(_dim.Parent as Panel)?.Children.Remove(_dim);

		_host = host;
		_root = root;
		_chromeBar = chromeBar;
		_side = side;
		_wash = wash;
		_still.IsVisible = false;
		_dim.IsVisible = false;
		_dim.IsHitTestVisible = false;
		_still.IsHitTestVisible = false;
		RenderOptions.SetBitmapInterpolationMode(_still, BitmapInterpolationMode.HighQuality);
		host.Children.Add(_still);
		host.Children.Add(_dim);
	}

	/// <summary>摘掉图层引用并停掉轮播(外壳关窗时;Windows 只 <c>Shutdown</c> 停计时器)。</summary>
	internal static void Detach()
	{
		StopRotation();
		(_still.Parent as Panel)?.Children.Remove(_still);
		(_dim.Parent as Panel)?.Children.Remove(_dim);
		_host = null;
		_root = null;
		_chromeBar = null;
		_side = null;
		_wash = null;
	}

	/// <summary>窗口激活与否(Windows 的 <c>SetWindowActive</c>;视频会跟着播 / 暂停)。</summary>
	public static void SetWindowActive(bool active)
	{
		_windowActive = active;
		SyncPlayback();
	}

	/// <summary>按设置重读一遍背景(Windows 的 <c>ReloadAsync</c>,6 个键一字不改)。</summary>
	public static async Task ReloadAsync()
	{
		if (_host is null)
		{
			return;
		}

		ISettingsSource? source = Settings ?? (AppServices.Ready ? Bridge : null);
		if (source is null)
		{
			// Windows: if (_host != null && AppServices.Ready) —— 后端没连上就什么都不做。
			return;
		}

		string image = (await source.GetStringAsync("ui_background", "")).Trim();
		string folder = (await source.GetStringAsync("ui_background_folder", "")).Trim();
		bool shuffle = await source.GetBoolAsync("ui_background_shuffle", false);
		int interval = await source.GetIntAsync("ui_background_interval", 10);
		int blur = await source.GetIntAsync("ui_background_blur", 0);
		int dim = await source.GetIntAsync("ui_background_dim", 0);

		string current;
		if (folder.Length > 0 && _playlist.SetFolder(folder, shuffle))
		{
			current = _playlist.Current();
			StartRotation(Clamp.Of(interval, 1, 1440));
		}
		else
		{
			StopRotation();
			current = (FileKinds.IsWallpaper(image) ? image : "");
		}

		SetEffects(blur, dim);
		SetSource(current);

		// WP-S12:壁纸状态可能翻转,窗口背板 / 边框随刷新(SetSource→ApplyChrome 也有一份)。
		BackdropRefresh?.Invoke();
		ChromeInvalidated?.Invoke();
	}

	/// <summary>换一张图 / 一段视频(Windows 的 <c>SetSource</c>)。</summary>
	public static void SetSource(string path)
	{
		path = (path ?? "").Trim();
		if (path == _source)
		{
			return;
		}

		_source = path;
		Error = "";
		StopVideo();
		_still.Source = null;
		_still.IsVisible = false;
		if (path.Length == 0)
		{
			ApplyChrome(on: false);
			return;
		}

		if (!File.Exists(path))
		{
			Error = I18n.L("文件不存在:") + path;
			_source = "";
			ApplyChrome(on: false);
			return;
		}

		if (FileKinds.IsVideo(path))
		{
			if (!StartVideo(path))
			{
				Error = I18n.L("macOS 版还不支持视频壁纸:") + path;
				_source = "";
				ApplyChrome(on: false);
				return;
			}
		}
		else
		{
			Bitmap? bitmap = ImageDecode.DecodeFile(path, 1920);
			if (bitmap is null)
			{
				Error = I18n.L("图片读不出来:") + path;
				_source = "";
				ApplyChrome(on: false);
				return;
			}

			_still.Source = bitmap;
			_still.IsVisible = true;
		}

		ApplyChrome(on: true);
	}

	/// <summary>模糊与遮罩(Windows 的 <c>SetEffects</c>;夹 0-40 / 0-80)。</summary>
	public static void SetEffects(int blur, int dim)
	{
		_blur = Clamp.Of(blur, 0, 40);
		_dimPercent = Clamp.Of(dim, 0, 80);
		BlurEffect? effect = ((_blur > 0) ? new BlurEffect { Radius = _blur } : null);
		_still.Effect = effect;
		Color canvas = Palette.Color("C.Canvas");
		_dim.Fill = new SolidColorBrush(canvas) { Opacity = (double)_dimPercent / 100.0 };
		_dim.IsVisible = (_dimPercent > 0 && Active);
	}

	/// <summary>主题变了:重新取一次遮罩底色,并把外壳取色按"有没有壁纸"再落一遍(Windows 同名)。</summary>
	public static void OnThemeChanged()
	{
		SetEffects(_blur, _dimPercent);
		ApplyChrome(Active);
	}

	/// <summary>收摊:停轮播、停视频(Windows 同名;外壳关窗时调)。</summary>
	public static void Shutdown()
	{
		StopRotation();
		StopVideo();
	}

	// ---------------------------------------------------------------- 内部

	private static void Advance()
	{
		string next = _playlist.Advance();
		if (next.Length > 0)
		{
			SetSource(next);
		}
	}

	private static void StartRotation(int minutes)
	{
		StopRotation();
		WallpaperTimer factory = TimerFactory ?? DefaultTimer;
		_rotation = factory(TimeSpan.FromMinutes(minutes), Advance);
	}

	private static void StopRotation()
	{
		IDisposable? handle = _rotation;
		_rotation = null;
		handle?.Dispose();
	}

	/// <summary>
	/// 默认轮播计时器。真桌面生命周期下就是 Windows 的那个 <c>DispatcherTimer</c>(到点换一张、不停表);
	/// 离屏(<c>--smoke</c>)与测试进程里没有人在推进 Dispatcher 的时钟(与 <c>Motion.HasClock</c>
	/// 同一个条件,见 D-105),在那里起计时器只会留一个永不触发的残留句柄 —— 所以直接返回 <c>null</c>:
	/// 轮播在那两个环境里不生效(Windows 那边同样收不到 Tick,行为等价),但**不留残留计时器**。
	/// </summary>
	private static IDisposable? DefaultTimer(TimeSpan delay, Action onFire)
	{
		if (!Motion.HasClock)
		{
			return null;
		}

		DispatcherTimer timer = new DispatcherTimer { Interval = delay };
		timer.Tick += delegate
		{
			onFire();
		};
		timer.Start();
		return new DispatcherTimerHandle(timer);
	}

	private static bool StartVideo(string path)
	{
		IVideoHost? host = VideoHost;
		if (host is null)
		{
			return false;
		}

		_video = host;
		host.Start(Path.GetFullPath(path));
		SyncPlayback();
		return true;
	}

	private static void StopVideo()
	{
		IVideoHost? video = _video;
		_video = null;
		if (video is null)
		{
			return;
		}

		try
		{
			video.Stop();
		}
		catch
		{
		}
	}

	private static void SyncPlayback()
	{
		if (_video is null)
		{
			return;
		}

		try
		{
			_video.SetActive(_windowActive);
		}
		catch
		{
		}
	}

	private static void ApplyChrome(bool on)
	{
		// 从未有过壁纸时保持默认不动(OnThemeChanged 会带 on=false 进来,
		// 不能把"没有壁纸"时的标题栏实底打回去)。
		if (!on && !_chromeOn)
		{
			return;
		}

		_chromeOn = on;
		if (_root is not null)
		{
			// WP-S12:有壁纸时给根边框不透明兜底(壁纸画在它上面;图片缺失/透明区域露出纸底)。
			if (on)
			{
				_root.Dyn(Border.BackgroundProperty, "B.Paper");
			}
			else
			{
				_root.Background = null;
			}
		}

		if (_chromeBar is not null)
		{
			// 有壁纸:标题栏玻璃让壁纸透上来;无壁纸:回主题实底。
			_chromeBar.Dyn(Border.BackgroundProperty, on ? "B.GlassStrong" : "B.TitleBar");
			_chromeBar.Dyn(Border.BorderBrushProperty, on ? "B.GlassBorder" : "B.TitleBarLine");
		}

		if (_side is not null)
		{
			_side.Dyn(Border.BackgroundProperty, "B.Glass");
			_side.Dyn(Border.BorderBrushProperty, "B.GlassBorder");
		}

		if (_wash is not null)
		{
			_wash.Dyn(Shape.FillProperty, "B.PageWash");
			_wash.Opacity = ((!on) ? 1 : 0);
		}

		_dim.IsVisible = (!on || _dimPercent <= 0) ? false : true;

		// Windows 在这里调 SideFrost.Refresh() 与 Backdrop.Refresh(window);两者属 C2c3,
		// 本包只从这两个口子喊一声(没接就是 null,不影响壁纸本身)。
		SideFrostRefresh?.Invoke();
		try
		{
			BackdropRefresh?.Invoke();
			ChromeInvalidated?.Invoke();
		}
		catch
		{
		}
	}

	/// <summary>后端设置来源(Windows <c>ReloadAsync</c> 里那 6 次 <c>TryCallAsync</c>)。</summary>
	private sealed class BridgeSettingsSource : ISettingsSource
	{
		public async Task<string> GetStringAsync(string key, string fallback)
		{
			return (await AppServices.Client.TryCallAsync("get_setting", new { key, @default = fallback }, fallback)) ?? "";
		}

		public async Task<bool> GetBoolAsync(string key, bool fallback)
		{
			return await AppServices.Client.TryCallAsync("get_setting", new { key, @default = fallback }, fallback);
		}

		public async Task<int> GetIntAsync(string key, int fallback)
		{
			return await AppServices.Client.TryCallAsync("get_setting", new { key, @default = fallback }, fallback);
		}
	}

	private sealed class DispatcherTimerHandle : IDisposable
	{
		private readonly DispatcherTimer _timer;

		internal DispatcherTimerHandle(DispatcherTimer timer)
		{
			_timer = timer;
		}

		public void Dispose()
		{
			_timer.Stop();
		}
	}
}

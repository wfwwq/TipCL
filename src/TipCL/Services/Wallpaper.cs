using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TipCL.Services;

public static class Wallpaper
{
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

		private void Scan()
		{
			if (_folder.Length == 0 || !Directory.Exists(_folder))
			{
				_files = new List<string>();
				return;
			}
			try
			{
				_files = Directory.EnumerateFiles(_folder).Where(FileKinds.IsWallpaper).OrderBy<string, string>((string p) => p, StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			catch
			{
				_files = new List<string>();
			}
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
				return _current = "";
			}
			if (_files.Count == 1)
			{
				return _current = _files[0];
			}
			if (_shuffle)
			{
				string text;
				do
				{
					text = _files[_rng.Next(_files.Count)];
				}
				while (text == _current);
				return _current = text;
			}
			int num = _files.IndexOf(_current);
			return _current = _files[((num >= 0) ? (num + 1) : 0) % _files.Count];
		}
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

	private static MediaElement? _video;

	private static readonly Rectangle _dim = new Rectangle();

	private static readonly DispatcherTimer _rotate = new DispatcherTimer();

	private static readonly Playlist _playlist = new Playlist();

	private static string _source = "";

	private static int _blur;

	private static int _dimPercent;

	private static bool _windowActive = true;

	public static string Error { get; private set; } = "";

	public static bool Active => !string.IsNullOrEmpty(_source);

	// WP-S12：壁纸开关/轮换后的窗口级联动（DWM backdrop、边框色、RootBorder 兜底）
	public static Action? ChromeInvalidated;

	public static void Attach(Grid host, Border root, Border chromeBar, Border side, Rectangle wash)
	{
		_host = host;
		_root = root;
		_chromeBar = chromeBar;
		_side = side;
		_wash = wash;
		_still.Visibility = Visibility.Collapsed;
		_dim.Visibility = Visibility.Collapsed;
		_dim.IsHitTestVisible = false;
		_still.IsHitTestVisible = false;
		RenderOptions.SetBitmapScalingMode(_still, BitmapScalingMode.HighQuality);
		host.Children.Add(_still);
		host.Children.Add(_dim);
		App.ThemeVisualsChanged -= OnThemeChanged;
		App.ThemeVisualsChanged += OnThemeChanged;
		_rotate.Tick += delegate
		{
			Advance();
		};
	}

	public static void SetWindowActive(bool active)
	{
		_windowActive = active;
		SyncPlayback();
	}

	public static async Task ReloadAsync()
	{
		if (_host != null && AppServices.Ready)
		{
			BridgeClient api = AppServices.Client;
			string image = ((await api.TryCallAsync("get_setting", new
			{
				key = "ui_background",
				@default = ""
			}, "")) ?? "").Trim();
			string folder = ((await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_folder",
				@default = ""
			}, "")) ?? "").Trim();
			bool shuffle = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_shuffle",
				@default = false
			}, fallback: false);
			int interval = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_interval",
				@default = 10
			}, 10);
			int blur = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_blur",
				@default = 0
			}, 0);
			int dim = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_dim",
				@default = 0
			}, 0);
			string source;
			if (folder.Length > 0 && _playlist.SetFolder(folder, shuffle))
			{
				source = _playlist.Current();
				_rotate.Interval = TimeSpan.FromMinutes(Clamp.Of(interval, 1, 1440));
				_rotate.Start();
			}
			else
			{
				_rotate.Stop();
				source = (FileKinds.IsWallpaper(image) ? image : "");
			}
			SetEffects(blur, dim);
			SetSource(source);
			// WP-S12：壁纸状态可能翻转，DWM backdrop/边框随刷新（SetSource→ApplyChrome 也有一份）
			Backdrop.Refresh(AppServices.Window);
			ChromeInvalidated?.Invoke();
		}
	}

	private static void Advance()
	{
		string text = _playlist.Advance();
		if (text.Length > 0)
		{
			SetSource(text);
		}
	}

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
		_still.Visibility = Visibility.Collapsed;
		if (path.Length == 0)
		{
			ApplyChrome(on: false);
			return;
		}
		if (!File.Exists(path))
		{
			Error = I18n.L("文件不存在：") + path;
			_source = "";
			ApplyChrome(on: false);
			return;
		}
		if (FileKinds.IsVideo(path))
		{
			StartVideo(path);
		}
		else
		{
			BitmapSource bitmapSource = LoadImage(path);
			if (bitmapSource == null)
			{
				Error = I18n.L("图片读不出来：") + path;
				_source = "";
				ApplyChrome(on: false);
				return;
			}
			_still.Source = bitmapSource;
			_still.Visibility = Visibility.Visible;
		}
		ApplyChrome(on: true);
	}

	public static void SetEffects(int blur, int dim)
	{
		_blur = Clamp.Of(blur, 0, 40);
		_dimPercent = Clamp.Of(dim, 0, 80);
		Effect effect = ((_blur > 0) ? new BlurEffect
		{
			Radius = _blur,
			KernelType = KernelType.Gaussian,
			RenderingBias = RenderingBias.Performance
		} : null);
		_still.Effect = effect;
		if (_video != null)
		{
			_video.Effect = effect;
		}
		Color color = ((Application.Current?.TryFindResource("C.Canvas") is Color color2) ? color2 : (App.IsDark ? Color.FromRgb(22, 22, 22) : Color.FromRgb(245, 245, 245)));
		_dim.Fill = new SolidColorBrush(color)
		{
			Opacity = (double)_dimPercent / 100.0
		};
		_dim.Visibility = ((_dimPercent <= 0 || !Active) ? Visibility.Collapsed : Visibility.Visible);
	}

	private static void StartVideo(string path)
	{
		MediaElement media = new MediaElement
		{
			LoadedBehavior = MediaState.Manual,
			UnloadedBehavior = MediaState.Manual,
			Stretch = Stretch.UniformToFill,
			IsMuted = true,
			IsHitTestVisible = false,
			ScrubbingEnabled = false
		};
		media.MediaFailed += delegate(object _, ExceptionRoutedEventArgs e)
		{
			Error = I18n.L("视频解码失败（系统可能缺 H.264 解码器）：") + e.ErrorException?.Message;
			StopVideo();
			ApplyChrome(on: false);
		};
		media.MediaEnded += delegate
		{
			media.Position = TimeSpan.Zero;
			media.Play();
		};
		if (_blur > 0)
		{
			media.Effect = new BlurEffect
			{
				Radius = _blur,
				KernelType = KernelType.Gaussian,
				RenderingBias = RenderingBias.Performance
			};
		}
		_video = media;
		_host.Children.Insert(0, media);
		media.Source = new Uri(System.IO.Path.GetFullPath(path));
		SyncPlayback();
	}

	private static void StopVideo()
	{
		MediaElement video = _video;
		_video = null;
		if (video != null)
		{
			try
			{
				video.Stop();
				video.Close();
				video.Source = null;
			}
			catch
			{
			}
			_host?.Children.Remove(video);
		}
	}

	private static void SyncPlayback()
	{
		if (_video == null)
		{
			return;
		}
		try
		{
			if (_windowActive)
			{
				_video.Play();
			}
			else
			{
				_video.Pause();
			}
		}
		catch
		{
		}
	}

	private static BitmapSource? LoadImage(string path)
	{
		try
		{
			// 读成字节再解码：Win7 没有 WebP 解码器时转 PNG 重试（见 WebpFallback）
			byte[] data = File.ReadAllBytes(System.IO.Path.GetFullPath(path));
			return WebpFallback.Decode(data, delegate(byte[] bytes)
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bitmapImage.DecodePixelWidth = 1920;
				bitmapImage.StreamSource = new MemoryStream(bytes);
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return bitmapImage;
			});
		}
		catch
		{
			return null;
		}
	}

	private static bool _chromeOn;

	private static void ApplyChrome(bool on)
	{
		// 从未有过壁纸时保持 XAML 默认不动（OnThemeChanged 会带 on=false 进来，
		// 不能把 premium 沉浸态设置的 PageWash/TopBar 玻璃打回去）
		if (!on && !_chromeOn)
		{
			return;
		}
		_chromeOn = on;
		if (_root != null)
		{
			// WP-S12：有壁纸时给 RootBorder 不透明兜底（壁纸图片画在它上面；图片缺失/透明区域露出纸底）
			if (on)
			{
				_root.SetResourceReference(Border.BackgroundProperty, "B.Paper");
			}
			else
			{
				_root.Background = null;
			}
		}
		if (_chromeBar != null)
		{
			// 有壁纸：标题栏玻璃让壁纸透上来；无壁纸：回主题实底
			_chromeBar.SetResourceReference(Border.BackgroundProperty, on ? "B.GlassStrong" : "B.TitleBar");
			_chromeBar.SetResourceReference(Border.BorderBrushProperty, on ? "B.GlassBorder" : "B.TitleBarLine");
		}
		if (_side != null)
		{
			_side.SetResourceReference(Border.BackgroundProperty, "B.Glass");
			_side.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
		}
		if (_wash != null)
		{
			_wash.SetResourceReference(Shape.FillProperty, "B.PageWash");
			_wash.Opacity = ((!on) ? 1 : 0);
		}
		_dim.Visibility = ((!on || _dimPercent <= 0) ? Visibility.Collapsed : Visibility.Visible);
		SideFrost.Refresh();
		try
		{
			Backdrop.Refresh(AppServices.Window);
			ChromeInvalidated?.Invoke();
		}
		catch
		{
		}
	}

	public static void OnThemeChanged()
	{
		SetEffects(_blur, _dimPercent);
		ApplyChrome(Active);
	}

	public static void Shutdown()
	{
		_rotate.Stop();
		StopVideo();
	}
}

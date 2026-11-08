// 设置页 · 数据装载 / 防抖自动保存 / 主题联动（M2-B5，对照 Windows 版 SettingsPage.cs
// LoadAsync / WireAutoSave / QueueSave / AutoSaveAsync / SaveAsync ~L1295-1685）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TipCL.App.Common;
using TipCL.App.Shell;
using TipCL.App.Ui;
using UiClass = TipCL.App.Ui.Ui;
using TipCL.Core;
using TipCL.Core.Launcher;

namespace TipCL.App.Pages.Settings;

public sealed partial class SettingsPage
{
    // ───────────────────────────── 主题 Extras（ui-theme.json） ─────────────────────────────

    private sealed class ThemeExtras
    {
        public string ThemeColor { get; set; } = ThemeService.DefaultAccent;
        public string Background { get; set; } = "default";
        public bool FlyAnimation { get; set; } = true;
        public int FlyDurationMs { get; set; } = 620;
    }

    private static ThemeExtras _extras = LoadExtras();

    private static string ExtrasPath =>
        Path.Combine(Utils.ROOT, "ui-theme.json");

    private static ThemeExtras LoadExtras()
    {
        try
        {
            if (File.Exists(ExtrasPath))
                return JsonSerializer.Deserialize<ThemeExtras>(File.ReadAllText(ExtrasPath)) ?? new ThemeExtras();
        }
        catch (Exception) { }
        return new ThemeExtras();
    }

    private static void SaveExtras()
    {
        try
        {
            Directory.CreateDirectory(Utils.ROOT);
            File.WriteAllText(ExtrasPath, JsonSerializer.Serialize(_extras, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    /// <summary>把持久化的主题色/背景同步进 ThemeService（启动后首次进设置页也会对齐）。</summary>
    private static void SyncPersistedVisuals(string? color, string? background)
    {
        _extras.ThemeColor = NormalizeColor(color) ?? _extras.ThemeColor;
        _extras.Background = NormalizeBackground(background) ?? _extras.Background;
        ThemeService.Apply(_extras.ThemeColor, _extras.Background);
    }

    private static string? NormalizeColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (!hex.StartsWith('#')) return null;
        return hex.Length is 7 or 9 ? hex : null;
    }

    private static string? NormalizeBackground(string? preset)
        => preset is "default" or "eye" or "warm" or "mist" ? preset : null;

    // ───────────────────────────── 装载 ─────────────────────────────

    private async Task LoadAsync()
    {
        _sync = true;
        try
        {
            var cfg = await Api.TryCallAsync<TipCL.Core.Launcher.LauncherConfig>("get_settings");
            bool backendDark = UiClass.IsDark;
            if (cfg != null)
            {
                string S(string k, string d = "") => Py.S(cfg.Get(k, d));
                bool B(string k, bool d = false) => Py.Truthy(cfg.Get(k, Py.Box(d)));
                int I(string k, int d) => (int)Py.IntOr(cfg.Get(k, Py.Box(d)), d);

                backendDark = cfg.data.ContainsKey("ui_dark") ? Py.Truthy(cfg.data.Get("ui_dark", Py.Box(backendDark))) : backendDark;
                _dark.IsChecked = backendDark;
                _threads.Value = Math.Clamp(I("download_threads", 8), 1, 64);
                _segs.Value = Math.Clamp(I("download_segments", 4), 1, 16);
                _mem.Value = Math.Clamp(I("default_memory_mb", 4096), 512, 32768);
                if (cfg.Get("default_resolution") is IEnumerable<object> res)
                {
                    var arr = res.ToList();
                    if (arr.Count >= 2)
                    {
                        _w.Text = Py.S(arr[0]);
                        _h.Text = Py.S(arr[1]);
                    }
                }
                _limit.Text = I("download_limit_kbps", 0).ToString();
                SelectKey(_src, S("download_source", "auto"));
                SelectKey(_comm, S("community_source", "auto"));
                _proxy.IsChecked = B("use_system_proxy", true);
                SelectKey(_iso, S("default_isolation", "none"));
                SelectKey(_gc, S("gc_preset", "auto"));
                SelectKey(_vis, S("launcher_visibility", "keep"));
                SelectKey(_home, S("homepage_mode", "news"));
                SelectKey(_win, S("window_mode", "window"));
                _homePath.Text = S("custom_homepage");
                _jvm.Text = S("default_jvm_args");
                _shareLib.IsChecked = B("share_libraries");
                _shareAssets.IsChecked = B("share_assets");
                _autoUpdate.IsChecked = B("auto_check_update", true);
                _cfSet = B("curseforge_api_key_set");
                _cfHint = S("curseforge_api_key_hint");
                _aiKeySet = B("ai_api_key_set");
                _aiKeyHint = S("ai_api_key_hint");
                SelectKey(_aiMode, S("ai_mode", "builtin").Trim().ToLowerInvariant() == "custom" ? "custom" : "builtin");
                _aiBase.Text = S("ai_base_url");
                SetAiModel(S("ai_model"));
                _aiCtx.Text = S("ai_context_window", "131072");
                _aiFallback.Text = S("ai_fallback_model");
                _gameDir0 = S("game_dir");
                if (string.IsNullOrEmpty(_gameDir0))
                    _gameDir0 = S("root");
                _gameDir.Text = _gameDir0;
                _bgPath.Text = S("ui_background");
                _bgFolder.Text = S("ui_background_folder");
                _bgShuffle.IsChecked = B("ui_background_shuffle");
                _bgInterval.Value = Math.Clamp(I("ui_background_interval", 10), 1, 120);
                _bgBlur.Value = Math.Clamp(I("ui_background_blur", 0), 0, 40);
                _bgDim.Value = Math.Clamp(I("ui_background_dim", 0), 0, 80);
                SyncPersistedVisuals(S("theme_color", _extras.ThemeColor), S("ui_bg_preset", _extras.Background));
            }
            else
            {
                _dark.IsChecked = backendDark;
            }
            SyncSecretBoxes();
            SyncAiRows();
            SyncAppearanceControls();
            SyncWallpaperInfo();
            await ReloadInstancesAsync();
            _multi.IsChecked = await Api.TryCallAsync("allow_multi_instance", false);
            await ReloadLanguagesAsync();
            _motion.IsChecked = Motion.Enabled;
            _fly.IsChecked = _extras.FlyAnimation;
            _flyDur.Value = Math.Clamp(_extras.FlyDurationMs, 200, 1200);
            _flyDurLbl.Text = $"{(int)_flyDur.Value} ms";
            _color = _extras.ThemeColor;
            _themeInfo.Text = "";
            if (backendDark != UiClass.IsDark)
                ApplyDark(backendDark);
            else
                ApplyVisuals();
        }
        finally
        {
            _sync = false;
        }
        _loaded = true;
        await ReloadThemePacksAsync();
    }

    private async Task ReloadInstancesAsync()
    {
        var rows = await Api.TryCallAsync<List<object>>("get_instances", new List<object>());
        var names = rows?.Select(r => Py.S(Py.D(r).Get("name"))).Where(n => n.Length > 0).ToList() ?? new List<string>();
        var sel = KeyOf(_inst);
        _inst.Items.Clear();
        foreach (var n in names) _inst.Items.Add(n);
        if (!string.IsNullOrEmpty(sel) && _inst.Items.Contains(sel))
            _inst.SelectedItem = sel;
        else if (_inst.Items.Count > 0)
            _inst.SelectedIndex = 0;
        var dflt = await Api.TryCallAsync("get_setting", "", new { key = "default_instance", dflt = "" });
        if (!string.IsNullOrEmpty(dflt) && _inst.Items.Contains(dflt))
            _inst.SelectedItem = dflt;
    }

    private async Task ReloadLanguagesAsync()
    {
        var langs = await Api.TryCallAsync<Dictionary<string, string>>("available_languages");
        _lang.Items.Clear();
        if (langs != null && langs.Count > 0)
        {
            foreach (var kv in langs)
                _lang.Items.Add(new Opt(kv.Key, kv.Value));
        }
        else
        {
            // 后端语言服务 M3 才接：给中文兜底项
            _lang.Items.Add(new Opt("zh-Hans", "简体中文"));
        }
        _lang0 = await Api.TryCallAsync("get_language", "") ?? "";
        SelectKey(_lang, string.IsNullOrEmpty(_lang0) ? null : _lang0);
        Run(PreviewLanguageAsync);
    }

    private async Task ReloadThemePacksAsync()
    {
        var packs = await Api.TryCallAsync<List<string>>("list_themes");
        _themePack.Items.Clear();
        if (packs != null)
            foreach (var p in packs)
                if (!string.IsNullOrWhiteSpace(p)) _themePack.Items.Add(p);
        _themeInfo.Text = packs == null ? "主题包后端 M3 接入，暂不可用" : (_themePack.Items.Count == 0 ? "还没有保存的主题包" : "");
    }

    // ───────────────────────────── 自动保存（500ms 防抖） ─────────────────────────────

    private void WireAutoSave()
    {
        foreach (var c in new[] { _dark, _motion, _fly, _proxy, _shareLib, _shareAssets, _multi, _autoUpdate, _bgShuffle })
            c.IsCheckedChanged += (_, _) => QueueSave();
        foreach (var c in new[] { _bg, _src, _comm, _inst, _iso, _gc, _win, _vis, _home, _lang, _aiMode, _aiModel })
            c.SelectionChanged += (_, _) => QueueSave();
        _aiModel.LostFocus += (_, _) => QueueSave();
        foreach (var s in new[] { _flyDur, _threads, _segs, _mem, _bgInterval, _bgBlur, _bgDim })
            s.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) QueueSave(); };
        // 文本框打字时不存，离开输入框（或回车）再存，免得存进半截路径
        foreach (var t in new[] { _limit, _w, _h, _jvm, _homePath, _gameDir, _aiBase, _aiKey, _aiCtx, _aiFallback, _cfKey, _bgPath, _bgFolder })
        {
            t.LostFocus += (_, _) => QueueSave();
            t.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) QueueSave(); };
        }
    }

    private void QueueSave()
    {
        if (_sync || !_loaded) return;
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                Run(AutoSaveAsync, "保存失败");
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        _saveState.Text = "保存中…";
    }

    /// <summary>离开设置页时把还没落盘的改动立刻存掉。</summary>
    private void FlushSave()
    {
        if (_saveTimer is { IsEnabled: true })
        {
            _saveTimer.Stop();
            Run(AutoSaveAsync, "保存失败");
        }
    }

    private async Task AutoSaveAsync()
    {
        if (_saving)
        {
            _savePending = true;
            return;
        }
        _saving = true;
        try
        {
            do
            {
                _savePending = false;
                await SaveAsync(auto: true);
            } while (_savePending);
            _saveState.Text = "已自动保存";
        }
        catch (Exception)
        {
            _saveState.Text = "保存失败";
            throw;
        }
        finally
        {
            _saving = false;
        }
    }

    // ───────────────────────────── 全量保存（键表照 Windows 版 SaveAsync） ─────────────────────────────

    private async Task SaveAsync(bool auto)
    {
        // 游戏目录：本地校验可写性（set_game_dir 后端 M3 接，先写 game_dir 键）
        string typed = (_gameDir.Text ?? "").Trim();
        if (typed.Length > 0 && typed != _gameDir0)
        {
            try
            {
                Directory.CreateDirectory(typed);
                string probe = Path.Combine(typed, ".tipcl-write-probe");
                await File.WriteAllTextAsync(probe, "ok");
                File.Delete(probe);
                _gameDir0 = typed;
            }
            catch (Exception ex)
            {
                _gameDir.Text = _gameDir0;
                Toast("游戏目录无效", ex.Message, ToastKind.Error);
                return;
            }
        }

        var data = new PyDict
        {
            ["ui_dark"] = Py.Box(_dark.IsChecked == true),
            ["download_threads"] = Py.Box((int)_threads.Value),
            ["download_segments"] = Py.Box((int)_segs.Value),
            ["default_memory_mb"] = Py.Box((int)_mem.Value),
            ["default_resolution"] = Py.L(IntOr(_w.Text, 854), IntOr(_h.Text, 480)),
            ["download_source"] = KeyOf(_src),
            ["community_source"] = KeyOf(_comm),
            ["use_system_proxy"] = Py.Box(_proxy.IsChecked == true),
            ["download_limit_kbps"] = Py.Box(IntOr(_limit.Text, 0)),
            ["default_isolation"] = KeyOf(_iso),
            ["default_jvm_args"] = (_jvm.Text ?? "").Trim(),
            ["gc_preset"] = KeyOf(_gc),
            ["launcher_visibility"] = KeyOf(_vis),
            ["homepage_mode"] = KeyOf(_home),
            ["custom_homepage"] = (_homePath.Text ?? "").Trim(),
            ["window_mode"] = KeyOf(_win),
            ["auto_check_update"] = Py.Box(_autoUpdate.IsChecked == true),
            ["share_libraries"] = Py.Box(_shareLib.IsChecked == true),
            ["share_assets"] = Py.Box(_shareAssets.IsChecked == true),
            ["default_instance"] = KeyOf(_inst),
            ["ai_mode"] = AiModeWire(),
            ["ai_base_url"] = (_aiBase.Text ?? "").Trim(),
            ["ai_model"] = AiModelValue(),
            ["ai_context_window"] = Py.Box(int.TryParse((_aiCtx.Text ?? "").Trim(), out var ctx) ? Math.Clamp(ctx, 8192, 2000000) : 131072),
            ["ai_fallback_model"] = (_aiFallback.Text ?? "").Trim(),
            ["ai_confirm_writes"] = Py.Box(true),
            ["ui_background"] = (_bgPath.Text ?? "").Trim(),
            ["ui_background_folder"] = (_bgFolder.Text ?? "").Trim(),
            ["ui_background_shuffle"] = Py.Box(_bgShuffle.IsChecked == true),
            ["ui_background_interval"] = Py.Box((int)_bgInterval.Value),
            ["ui_background_blur"] = Py.Box((int)_bgBlur.Value),
            ["ui_background_dim"] = Py.Box((int)_bgDim.Value),
            ["theme_color"] = _color,
            ["ui_bg_preset"] = KeyOf(_bg),
            ["ui_motion"] = Py.Box(_motion.IsChecked == true),
            ["ui_fly_animation"] = Py.Box(_fly.IsChecked == true),
            ["ui_fly_duration_ms"] = Py.Box((int)_flyDur.Value),
            ["game_dir"] = _gameDir0,
            ["allow_multi_instance"] = Py.Box(_multi.IsChecked == true),
        };
        // 秘密行：非空才提交，否则按清除标记提交 *_clear
        string cf = (_cfKey.Text ?? "").Trim();
        if (cf.Length > 0) data["curseforge_api_key"] = cf;
        else if (_cfClear) data["curseforge_api_key_clear"] = Py.Box(true);
        string aiKey = (_aiKey.Text ?? "").Trim();
        if (aiKey.Length > 0) data["ai_api_key"] = aiKey;
        else if (_aiKeyClear) data["ai_api_key_clear"] = Py.Box(true);

        await Api.CallAsync<object?>("save_settings", new { data });
        await Api.TryCallAsync<object?>("set_multi_instance", null, new { allow = _multi.IsChecked == true });

        string lang = KeyOf(_lang);
        bool langChanged = !string.IsNullOrEmpty(lang) && lang != _lang0;
        if (langChanged)
        {
            await Api.TryCallAsync<object?>("set_language", null, new { lang });
            _lang0 = lang;
        }

        // Extras 双写（后端设置 + 本地文件）
        _extras.ThemeColor = _color;
        _extras.Background = KeyOf(_bg);
        _extras.FlyAnimation = _fly.IsChecked == true;
        _extras.FlyDurationMs = (int)_flyDur.Value;
        SaveExtras();
        Motion.Enabled = _motion.IsChecked == true;
        ApplyVisuals();

        if (langChanged)
            Toast("语言已切换", "下次启动启动器后界面才会变成新语言", ToastKind.Info);
    }

    private static int IntOr(string? s, int fallback)
        => int.TryParse(s?.Trim(), out var v) && v >= 0 ? v : fallback;

    private string AiModeWire() => KeyOf(_aiMode) == "custom" ? "custom" : "builtin";

    private string AiModelValue()
    {
        string text = (_aiModel.SelectedItem as string ?? "").Trim();
        if (text.Length > 0) return text;
        return _aiModel.SelectedItem as string ?? "";
    }

    private void SetAiModel(string? name)
    {
        string text = name?.Trim() ?? "";
        if (text.Length == 0) return;
        if (!_aiModel.Items.Contains(text)) _aiModel.Items.Add(text);
        _aiModel.SelectedItem = text;
        _aiModel.SelectedItem = text;
    }

    // ───────────────────────────── 视觉应用 ─────────────────────────────

    private void ApplyDark(bool dark)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow win)
        {
            win.SetDark(dark);
            ThemeService.Replay();
        }
        else
        {
            UiClass.ApplyTheme(dark);
            ThemeService.Replay();
        }
        _saveState.Text = _loaded ? "已自动保存" : _saveState.Text;
    }

    private void ApplyVisuals()
    {
        _color = _color is null ? DefaultColor : _color;
        _extras.ThemeColor = _color;
        _extras.Background = KeyOf(_bg);
        ThemeService.Apply(_color, KeyOf(_bg));
        SaveExtras();
    }

    private void BuildSwatches()
    {
        _swatches.Children.Clear();
        foreach (var (name, hex) in Palette)
        {
            bool on = string.Equals(_color, hex, StringComparison.OrdinalIgnoreCase);
            var inner = new Border { CornerRadius = new CornerRadius(8), Width = 20, Height = 20, Background = new SolidColorBrush(Color.Parse(hex)) };
            var outer = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                BorderThickness = new Thickness(on ? 2 : 0),
                Child = inner,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Margin = new Thickness(0, 0, 2, 0),
            };
            ToolTip.SetTip(outer, name);
            outer[!Border.BorderBrushProperty] = outer.GetResourceObservable("B.AccentDeep").ToBinding();
            string pick = hex;
            outer.PointerReleased += (_, _) =>
            {
                _color = pick;
                BuildSwatches();
                ApplyVisuals();
                QueueSave();
            };
            _swatches.Children.Add(outer);
        }
    }

    private void SyncAppearanceControls()
    {
        bool sync = _sync;
        _sync = true;
        try
        {
            if (_dark.IsChecked != UiClass.IsDark)
                _dark.IsChecked = UiClass.IsDark;
            if (!string.Equals(_color, ThemeService.CurrentColor, StringComparison.OrdinalIgnoreCase))
            {
                _color = ThemeService.CurrentColor;
                BuildSwatches();
            }
            if (KeyOf(_bg) != ThemeService.CurrentBackground)
                SelectKey(_bg, ThemeService.CurrentBackground);
        }
        finally
        {
            _sync = sync;
        }
    }

    private void SyncAiRows()
    {
        bool custom = KeyOf(_aiMode) == "custom";
        foreach (var row in _aiCustomRows)
            row.IsVisible = custom;
    }

    private void SyncSecretBoxes()
    {
        _cfKey.Tag = _cfClear
            ? "已恢复为内置 Key"
            : _cfSet ? $"已保存自己的 Key（{_cfHint}），留空不修改" : "CurseForge API Key（留空用内置）";
        _aiKey.Tag = _aiKeyClear
            ? "令牌已清除"
            : _aiKeySet ? $"已保存自己的令牌（{_aiKeyHint}），留空不修改" : "sk-…";
        _cfReset.IsVisible = _cfSet && !_cfClear;
        _aiKeyReset.IsVisible = _aiKeySet && !_aiKeyClear;
    }

    private void SyncWallpaperInfo()
    {
        string folder = (_bgFolder.Text ?? "").Trim();
        string path = (_bgPath.Text ?? "").Trim();
        _bgInfo.Text = folder.Length > 0 ? "正在轮播文件夹里的图片"
            : path.Length == 0 ? "当前没有壁纸，界面走主题底色"
            : "壁纸设置已保存（渲染管线 M3 接入）";
        _bgUndo.IsVisible = false; // background_history 后端 M3 接
    }

    // ───────────────────────────── 动作 ─────────────────────────────

    private async Task BrowseGameDirAsync()
    {
        string? path = await Dlg.PickFolder("选择游戏目录");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(path);
            _gameDir0 = path;
            _gameDir.Text = path;
            Toast("已切换目录", path, ToastKind.Success);
        }
        catch (Exception ex)
        {
            Toast("目录无效", ex.Message, ToastKind.Error);
        }
        QueueSave();
    }

    private async Task TestAiAsync()
    {
        var settings = new PyDict
        {
            ["ai_mode"] = AiModeWire(),
            ["ai_base_url"] = (_aiBase.Text ?? "").Trim(),
            ["ai_api_key"] = (_aiKey.Text ?? "").Trim(),
            ["ai_model"] = AiModelValue(),
        };
        string? text = await Api.TryCallAsync<string>("test_ai_connection", null, new { settings });
        if (text == null)
            Toast("AI 连接失败", "后端 AI 网关 M3 接入，暂无法测试", ToastKind.Warning);
        else
            Toast("AI 连接成功", text, ToastKind.Success);
    }

    private Func<Task> ThemeOpAsync(string op) => async () =>
    {
        string? name = (_themePack.SelectedItem as string) ?? "";
        if (op == "save_theme")
        {
            name = await Dlg.Prompt("保存为主题包", "主题包名称", $"我的主题 {_themePack.Items.Count + 1}");
            if (string.IsNullOrWhiteSpace(name)) return;
        }
        else if (string.IsNullOrEmpty(name))
        {
            Toast("先选一个主题包", "从「已保存」下拉里挑一个", ToastKind.Warning);
            return;
        }
        var data = new PyDict
        {
            ["theme_color"] = _color,
            ["ui_bg_preset"] = KeyOf(_bg),
            ["window_mode"] = KeyOf(_win),
            ["homepage_mode"] = KeyOf(_home),
        };
        await Api.CallAsync<object?>("save_settings", new { data });
        object? result = await Api.TryCallAsync<object?>(op, null, new { name });
        if (result == null && op != "save_theme")
        {
            Toast("操作失败", $"主题包 {op} 后端 M3 接入，暂不可用", ToastKind.Warning);
            return;
        }
        Toast("主题包已保存", name, ToastKind.Success);
        await ReloadThemePacksAsync();
    };

    private async Task PreviewLanguageAsync()
    {
        string lang = KeyOf(_lang);
        if (string.IsNullOrEmpty(lang))
        {
            _langPreview.Text = "";
            return;
        }
        var samples = new List<string>();
        foreach (string k in new[] { "设置", "启动游戏", "下载任务" })
            samples.Add(await Api.TryCallAsync("translate", k, new { key = k, lang }) ?? k);
        _langPreview.Text = lang == _lang0
            ? "预览：" + string.Join(" · ", samples)
            : "切换后界面会变成：" + string.Join(" · ", samples) + "（重启后全量生效）";
    }

    /// <summary>异步动作统一入口：异常转 Toast 不崩 UI（照 Windows 版 PageBase.Run）。</summary>
    private static async void Run(Func<Task> work, string? failTitle = null)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ToastHost.Show(failTitle ?? "操作失败", ex.Message, ToastKind.Error);
        }
    }
}

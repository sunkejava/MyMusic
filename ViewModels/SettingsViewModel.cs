using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 设置页：主题、强调色、背景、音乐目录管理
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;

        SetThemeCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            Theme = key;
        });

        SetBackgroundCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            Background = key;
        });

        ChooseBackgroundImageCommand = new(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.webp"
            };
            if (dialog.ShowDialog() != true) return;

            SettingsService.Current.CustomBackgroundPath = dialog.FileName;
            SettingsService.Current.Background = "Custom";
            SettingsService.Save();
            ThemeService.ApplyBackground("Custom");
            Set(ref _background, "Custom");
            Raise(nameof(Background));
        });

        BrowseMusicDirCommand = new(() =>
        {
            var dialog = new OpenFolderDialog { Title = "选择音乐文件夹" };
            if (dialog.ShowDialog() != true) return;

            MusicDirectory = dialog.FolderName;
            SettingsService.Current.MusicDirectory = dialog.FolderName;
            SettingsService.Save();
            _ = RescanAsync();
        });

        RescanCommand = new(() => _ = RescanAsync());

        SetPlayThemeCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            PlayTheme = key;
        });

        SetSkinCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            PlayerSkin = key;
        });

        SetLyricHighlightCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            LyricHighlight = key;
        });

        SetAccentCommand = new(key =>
        {
            if (string.IsNullOrEmpty(key)) return;
            if (key == "Custom")
            {
                ChooseAccentColor();
                return;
            }
            Accent = key;
        });

        ChooseBackgroundColorCommand = new(() =>
        {
            var current = SettingsService.Current;
            var initial = ThemeService.IsHexColor(current.CustomBackgroundColor)
                ? current.CustomBackgroundColor : "#1A2028";
            var hex = PickColorHex(initial);
            if (hex == null) return;
            current.CustomBackgroundColor = hex;
            current.Background = "CustomColor";
            SettingsService.Save();
            ThemeService.ApplyBackground("CustomColor");
            _background = "CustomColor";
            Raise(nameof(Background));
        });

        PickLyricColorCommand = new(() =>
        {
            var current = SettingsService.Current;
            var initial = ThemeService.IsHexColor(current.LyricColor) ? current.LyricColor
                : ThemeService.IsHexColor(current.Accent) ? current.Accent : "#4FA8FF";
            var hex = PickColorHex(initial);
            if (hex == null) return;
            current.LyricColor = hex;
            SettingsService.Save();
            ThemeService.ApplyLyricColor();
            Raise(nameof(LyricColorText));
            Views.DeskLyricWindow.ApplySettings();
        });

        ResetLyricColorCommand = new(() => LyricColorText = "");

        // 播放页内切换模式/皮肤时，同步本页选中状态
        SettingsService.AppearanceChanged += () =>
        {
            Raise(nameof(PlayTheme));
            Raise(nameof(PlayerSkin));
        };

        SetAudioDeviceCommand = new(id =>
        {
            _main.Player.SetOutputDevice(string.IsNullOrEmpty(id) ? null : id);
            Raise(nameof(AudioDevice));
        });

        SetDeskLyricFontCommand = new(key =>
        {
            if (!string.IsNullOrEmpty(key)) DeskLyricFont = key;
        });

        SetDeskLyricColorCommand = new(hex =>
        {
            if (!string.IsNullOrEmpty(hex)) DeskLyricColor = hex;
        });

        SetDeskLyricBgColorCommand = new(hex =>
        {
            if (!string.IsNullOrEmpty(hex)) DeskLyricBgColor = hex;
        });

        // 背景滑杆防抖：拖动结束后再重建背景画刷，避免逐帧重建开销
        _bgDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _bgDebounce.Tick += (_, _) =>
        {
            _bgDebounce.Stop();
            if (Background == "Custom") ThemeService.ApplyBackground("Custom");
        };

        MusicLibraryService.ScanCompleted += (_, _) =>
        {
            Raise(nameof(IsScanning));
            Raise(nameof(SongCountText));
        };

        MusicLibraryService.ScanProgressChanged += (_, _) =>
        {
            Raise(nameof(ScanProgress));
            Raise(nameof(ScanStatusText));
        };
    }

    // ---------- 主题 ----------
    public string Theme
    {
        get => SettingsService.Current.Theme;
        set
        {
            var current = SettingsService.Current;
            if (current.Theme == value) return;
            current.Theme = value;
            SettingsService.Save();
            ThemeService.ApplyTheme(value);
            Raise();
            Raise(nameof(AvailableBackgrounds));
        }
    }

    public string Accent
    {
        get => SettingsService.Current.Accent;
        set
        {
            var current = SettingsService.Current;
            if (current.Accent == value) return;
            current.Accent = value;
            SettingsService.Save();
            ThemeService.ApplyAccent(value);
            Raise();
            Raise(nameof(AccentItems)); // 自定义色块同步显示新色
        }
    }

    /// <summary>反转滚轮/触摸板滚动方向（匹配系统“自然滚动”手感）</summary>
    public bool ReverseScroll
    {
        get => SettingsService.Current.ReverseScroll;
        set
        {
            var current = SettingsService.Current;
            if (current.ReverseScroll == value) return;
            current.ReverseScroll = value;
            SettingsService.Save();
            Raise();
        }
    }

    string _background = SettingsService.Current.Background;
    public string Background
    {
        get => _background;
        set
        {
            if (!Set(ref _background, value)) return;
            var current = SettingsService.Current;
            current.Background = value;
            SettingsService.Save();
            ThemeService.ApplyBackground(value);
        }
    }

    public IReadOnlyList<OptionItem> ThemeItems =>
        ThemeService.ThemeOptions.Select(t => new OptionItem(t.Key, t.Name)).ToList();

    public IReadOnlyList<OptionItem> AccentItems
    {
        get
        {
            var list = ThemeService.AccentOptions.Select(a => new OptionItem(a.Key, a.Name, a.Hex)).ToList();
            // 自定义取色入口：已自定义时色块显示当前色
            var custom = SettingsService.Current.Accent;
            list.Add(new OptionItem("Custom", "自定义颜色…",
                ThemeService.IsHexColor(custom) ? custom : "#8A939E"));
            return list;
        }
    }

    /// <summary>弹出取色对话框选择自定义强调色</summary>
    void ChooseAccentColor()
    {
        var current = SettingsService.Current;
        string initial = "#4FA8FF";
        if (ThemeService.IsHexColor(current.Accent)) initial = current.Accent;
        else
            foreach (var a in ThemeService.AccentOptions)
                if (a.Key == current.Accent) { initial = a.Hex; break; }
        var hex = PickColorHex(initial);
        if (hex == null) return;
        Accent = hex;
    }

    public IReadOnlyList<OptionItem> AvailableBackgrounds
    {
        get
        {
            var theme = SettingsService.Current.Theme == "Light" ? "Light" : "Dark";
            var list = ThemeService.BackgroundOptions[theme]
                .Select(b => new OptionItem(b.Key, b.Name)).ToList();
            list.Add(new OptionItem("CustomColor", "自定义颜色…"));
            list.Add(new OptionItem("Custom", "自定义图片…"));
            return list;
        }
    }

    // ---------- 歌词颜色 ----------
    /// <summary>自定义歌词高亮色文本（合法 hex 立即应用；空 = 跟随强调色）</summary>
    public string LyricColorText
    {
        get => SettingsService.Current.LyricColor;
        set
        {
            var v = (value ?? "").Trim();
            if (v.Length > 0 && v[0] != '#') v = "#" + v;
            var current = SettingsService.Current;
            if (v.Length == 0)
            {
                if (current.LyricColor.Length == 0) return;
                current.LyricColor = "";
            }
            else
            {
                if (!ThemeService.IsHexColor(v) || current.LyricColor == v) return;
                current.LyricColor = v;
            }
            SettingsService.Save();
            ThemeService.ApplyLyricColor();
            Raise();
            Views.DeskLyricWindow.ApplySettings();
        }
    }

    // ---------- 封面氛围背景 ----------
    public bool CoverAmbient
    {
        get => SettingsService.Current.CoverAmbientBackground;
        set
        {
            var current = SettingsService.Current;
            if (current.CoverAmbientBackground == value) return;
            current.CoverAmbientBackground = value;
            SettingsService.Save();
            Raise();
            _main.NowPlaying.RefreshCoverAmbient();
        }
    }

    /// <summary>弹出系统取色对话框，返回 #RRGGBB；取消返回 null</summary>
    static string? PickColorHex(string initial)
    {
        try
        {
            using var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = ToDialogColor(initial),
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
            return $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
        catch { return null; }
    }

    static System.Drawing.Color ToDialogColor(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }
        catch { return System.Drawing.Color.Gray; }
    }

    // ---------- 自定义背景微调 ----------
    /// <summary>自定义背景图片不透明度（0-100，百分比）</summary>
    public int BackgroundOpacity
    {
        get => (int)Math.Round(SettingsService.Current.CustomBackgroundOpacity * 100);
        set
        {
            var target = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(SettingsService.Current.CustomBackgroundOpacity - target) < 1e-9) return;
            SettingsService.Current.CustomBackgroundOpacity = target;
            SettingsService.Save();
            Raise();
            // 直接改可变画刷的图片层透明度，实时预览且不闪烁；
            // 画刷不在位（如刚切到自定义）才回退到防抖整体重建
            if (Background == "Custom" && !ThemeService.UpdateCustomBackgroundOpacity(target))
                DebounceBackground();
        }
    }

    /// <summary>磨砂模糊半径（0-20，0 为关闭）</summary>
    public int BackgroundBlur
    {
        get => (int)Math.Round(SettingsService.Current.CustomBackgroundBlur);
        set
        {
            var v = Math.Clamp(value, 0, 20);
            if (Math.Abs(SettingsService.Current.CustomBackgroundBlur - v) < 1e-9) return;
            SettingsService.Current.CustomBackgroundBlur = v;
            SettingsService.Save();
            Raise();
            // 后台生成模糊图后仅替换画刷内容，实时预览且不闪烁；
            // 画刷不在位才回退到防抖整体重建
            if (Background == "Custom" && !ThemeService.UpdateCustomBackgroundBlur(v))
                DebounceBackground();
        }
    }

    readonly DispatcherTimer _bgDebounce;

    void DebounceBackground()
    {
        _bgDebounce.Stop();
        _bgDebounce.Start();
    }

    // ---------- 播放页主题 ----------
    public string PlayTheme
    {
        get => SettingsService.Current.PlayTheme;
        set
        {
            var current = SettingsService.Current;
            if (current.PlayTheme == value) return;
            current.PlayTheme = value;
            SettingsService.Save();
            Raise();
            _main.NowPlaying.RefreshPlayTheme();
        }
    }

    public IReadOnlyList<OptionItem> PlayThemeItems =>
        ThemeService.PlayThemeOptions.Select(t => new OptionItem(t.Key, t.Name)).ToList();

    // ---------- 播放皮肤 ----------
    public string PlayerSkin
    {
        get => SettingsService.Current.PlayerSkin;
        set
        {
            var current = SettingsService.Current;
            if (current.PlayerSkin == value) return;
            current.PlayerSkin = value;
            SettingsService.Save();
            Raise();
            _main.NowPlaying.RefreshSkin();
        }
    }

    /// <summary>皮肤选择器色卡（与播放页抽屉共享同一目录）</summary>
    public IReadOnlyList<SkinCatalog.SkinGroup> SkinGroups => SkinCatalog.BuildGroups();

    // ---------- 歌词高亮方式 ----------
    public string LyricHighlight
    {
        get => SettingsService.Current.LyricHighlight;
        set
        {
            var current = SettingsService.Current;
            if (current.LyricHighlight == value) return;
            current.LyricHighlight = value;
            SettingsService.Save();
            Raise();
            // 桌面歌词窗实时应用新高亮方式
            Views.DeskLyricWindow.ApplySettings();
        }
    }

    public IReadOnlyList<OptionItem> LyricHighlightItems =>
        ThemeService.LyricHighlightOptions.Select(o => new OptionItem(o.Key, o.Name)).ToList();

    // ---------- 音频输出设备 ----------
    /// <summary>当前设备 Id，空字符串表示系统默认</summary>
    public string AudioDevice => SettingsService.Current.AudioDeviceId ?? "";

    public IReadOnlyList<OptionItem> AudioDevices =>
        PlaybackService.ListOutputDevices()
            .Select(d => new OptionItem(d.Id, d.Name)).ToList();

    // ---------- 桌面歌词 ----------
    public IReadOnlyList<OptionItem> DeskLyricFontItems =>
    [
        new("Microsoft YaHei UI", "微软雅黑"),
        new("SimHei", "黑体"),
        new("KaiTi", "楷体"),
        new("FangSong", "仿宋"),
        new("Segoe UI", "Segoe UI")
    ];

    public IReadOnlyList<OptionItem> DeskLyricColorItems =>
    [
        new("#FFFFFF", "白色", "#FFFFFF"),
        new("#333333", "深灰", "#333333"),
        new("#FFD54A", "金黄", "#FFD54A"),
        new("#339EF8", "蓝色", "#339EF8"),
        new("#FF6B9D", "粉色", "#FF6B9D"),
        new("#31C272", "绿色", "#31C272")
    ];

    public string DeskLyricFont
    {
        get => SettingsService.Current.DeskLyricFont;
        set
        {
            var current = SettingsService.Current;
            if (current.DeskLyricFont == value) return;
            current.DeskLyricFont = value;
            SettingsService.Save();
            Raise();
            MyQQMusic.Views.DeskLyricWindow.ApplySettings();
        }
    }

    /// <summary>桌面歌词字号（14-72）</summary>
    public int DeskLyricFontSize
    {
        get => (int)Math.Round(SettingsService.Current.DeskLyricFontSize);
        set
        {
            var v = Math.Clamp(value, 14, 72);
            if (Math.Abs(SettingsService.Current.DeskLyricFontSize - v) < 1e-9) return;
            SettingsService.Current.DeskLyricFontSize = v;
            SettingsService.Save();
            Raise();
            MyQQMusic.Views.DeskLyricWindow.ApplySettings();
        }
    }

    public string DeskLyricColor
    {
        get => SettingsService.Current.DeskLyricColor;
        set
        {
            var current = SettingsService.Current;
            if (current.DeskLyricColor == value) return;
            current.DeskLyricColor = value;
            SettingsService.Save();
            Raise();
            MyQQMusic.Views.DeskLyricWindow.ApplySettings();
        }
    }

    public string DeskLyricBgColor
    {
        get => SettingsService.Current.DeskLyricBgColor;
        set
        {
            var current = SettingsService.Current;
            if (current.DeskLyricBgColor == value) return;
            current.DeskLyricBgColor = value;
            SettingsService.Save();
            Raise();
            MyQQMusic.Views.DeskLyricWindow.ApplySettings();
        }
    }

    /// <summary>桌面歌词背景不透明度（0-100，百分比），0 为全透明</summary>
    public int DeskLyricBgOpacity
    {
        get => (int)Math.Round(SettingsService.Current.DeskLyricBgOpacity * 100);
        set
        {
            var target = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(SettingsService.Current.DeskLyricBgOpacity - target) < 1e-9) return;
            SettingsService.Current.DeskLyricBgOpacity = target;
            SettingsService.Save();
            Raise();
            MyQQMusic.Views.DeskLyricWindow.ApplySettings();
        }
    }

    // ---------- 音乐目录 ----------
    public string MusicDirectory
    {
        get => SettingsService.Current.MusicDirectory;
        set
        {
            var current = SettingsService.Current;
            if (current.MusicDirectory == value) return;
            current.MusicDirectory = value;
            SettingsService.Save();
            Raise();
        }
    }

    public bool IsScanning => MusicLibraryService.IsScanning;

    /// <summary>扫描进度 0-1</summary>
    public double ScanProgress => MusicLibraryService.ScanProgress;

    /// <summary>扫描状态文本（含已扫描/总数与当前文件）</summary>
    public string ScanStatusText => MusicLibraryService.ScanStatusText;

    public string SongCountText => MusicLibraryService.Songs.Count > 0
        ? $"已导入 {MusicLibraryService.Songs.Count} 首本地音乐"
        : "尚未导入音乐，请设置音乐目录并点击\"重新扫描\"";

    async Task RescanAsync()
    {
        await MusicLibraryService.ScanAsync(SettingsService.Current.MusicDirectory);
    }

    public RelayCommand<string> SetThemeCommand { get; }
    public RelayCommand<string> SetAccentCommand { get; }
    public RelayCommand<string> SetBackgroundCommand { get; }
    public RelayCommand ChooseBackgroundImageCommand { get; }
    public RelayCommand BrowseMusicDirCommand { get; }
    public RelayCommand RescanCommand { get; }
    public RelayCommand<string> SetPlayThemeCommand { get; }
    public RelayCommand<string> SetSkinCommand { get; }
    public RelayCommand<string> SetLyricHighlightCommand { get; }
    public RelayCommand ChooseBackgroundColorCommand { get; }
    public RelayCommand PickLyricColorCommand { get; }
    public RelayCommand ResetLyricColorCommand { get; }
    public RelayCommand<string> SetAudioDeviceCommand { get; }
    public RelayCommand<string> SetDeskLyricFontCommand { get; }
    public RelayCommand<string> SetDeskLyricColorCommand { get; }
    public RelayCommand<string> SetDeskLyricBgColorCommand { get; }
}

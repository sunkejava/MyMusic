using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 正在播放页：黑胶大封面 + 滚动歌词
/// </summary>
public class NowPlayingViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    int _loadSeq;

    public NowPlayingViewModel(MainViewModel main)
    {
        _main = main;
        ToggleLikeCommand = new(() =>
        {
            var song = CurrentSong;
            if (song != null) PlaylistService.ToggleFavorite(song.Id);
        }, () => CurrentSong != null);
    }

    public SongInfo? CurrentSong => _main.Player.CurrentSong;
    public bool IsPlaying => _main.Player.IsPlaying;
    public bool HasSong => _main.Player.CurrentSong != null;

    /// <summary>播放页模式：Standard / Vinyl / Lyric（旧键自动归一）</summary>
    public string PlayTheme => ThemeService.NormalizePlayTheme(SettingsService.Current.PlayTheme);

    /// <summary>播放页皮肤（背景/唱片/音波氛围配色）</summary>
    public PlayerSkin Skin => PlayerSkins.Find(SettingsService.Current.PlayerSkin);

    /// <summary>当前皮肤键（抽屉色卡勾选当前使用皮肤）</summary>
    public string CurrentSkinKey => SettingsService.Current.PlayerSkin;

    /// <summary>播放页抽屉内直接切换皮肤（与设置页共享配置并互相同步）</summary>
    public void SetSkin(string key)
    {
        var current = SettingsService.Current;
        if (current.PlayerSkin == key) return;
        current.PlayerSkin = key;
        SettingsService.Save();
        RefreshSkin();
    }

    /// <summary>抽屉皮肤色卡分组</summary>
    public IReadOnlyList<SkinCatalog.SkinGroup> SkinGroups => SkinCatalog.BuildGroups();

    /// <summary>播放页当前生效背景（播放页打开时主窗口播放条背景与之同步）</summary>
    public Brush? PageBackground { get; private set; }

    public void SetPageBackground(Brush? bg)
    {
        if (ReferenceEquals(PageBackground, bg)) return;
        PageBackground = bg;
        Raise(nameof(PageBackground));
    }

    /// <summary>播放页皮肤配色板（播放页打开时主窗口播放条文字/图标同步皮肤配色）</summary>
    public IReadOnlyDictionary<string, Brush>? PagePalette { get; private set; }

    public void SetPagePalette(IReadOnlyDictionary<string, Brush>? palette)
    {
        if (ReferenceEquals(PagePalette, palette)) return;
        PagePalette = palette;
        Raise(nameof(PagePalette));
    }

    /// <summary>设置页切换播放模式后刷新</summary>
    public void RefreshPlayTheme()
    {
        Raise(nameof(PlayTheme));
        EnsureAmbientIfNeeded();
    }

    /// <summary>设置页切换皮肤后刷新</summary>
    public void RefreshSkin()
    {
        Raise(nameof(Skin));
        Raise(nameof(CurrentSkinKey));
        EnsureAmbientIfNeeded();
        SettingsService.NotifyAppearanceChanged();
    }

    /// <summary>播放页内直接切换播放模式（与设置页共享同一配置并互相同步）</summary>
    public void SetMode(string key)
    {
        var current = SettingsService.Current;
        current.PlayTheme = key;
        SettingsService.Save();
        RefreshPlayTheme();
        SettingsService.NotifyAppearanceChanged();
    }

    /// <summary>歌词模式或全屏封面皮肤需要氛围背景，缺失时按需补建</summary>
    void EnsureAmbientIfNeeded()
    {
        if ((PlayTheme == "Lyric" || Skin.FullCover) && CurrentSong != null && _coverBackground == null)
            EnsureAmbientBackground();
    }

    /// <summary>仅重建氛围背景（切主题时补建，不影响封面与歌词）</summary>
    void EnsureAmbientBackground()
    {
        var song = CurrentSong;
        if (song == null) return;
        Task.Run(() =>
        {
            var ambient = BuildAmbientBackground(song.CreateFullCover());
            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(() =>
            {
                if (ReferenceEquals(CurrentSong, song)) CoverBackground = ambient;
            });
        });
    }

    Brush? _coverBackground;
    /// <summary>沉浸模式的氛围背景（模糊封面，已冻结，可跨线程赋值）</summary>
    public Brush? CoverBackground
    {
        get => _coverBackground;
        private set => Set(ref _coverBackground, value);
    }

    BitmapSource? _coverSource; // 最近解码的封面原图，供氛围主色重新提取

    Brush? _coverAmbient;
    /// <summary>跟随封面主色的播放页氛围背景画刷（设置开启且有封面时非空）</summary>
    public Brush? CoverAmbientBrush => _coverAmbient;

    /// <summary>设置页开关“跟随封面主色氛围背景”后刷新</summary>
    public void RefreshCoverAmbient()
    {
        if (!SettingsService.Current.CoverAmbientBackground)
        {
            if (Set(ref _coverAmbient, null)) Raise(nameof(CoverAmbientBrush));
            return;
        }
        var source = _coverSource;
        if (source == null) return;
        Task.Run(() =>
        {
            var brush = BuildAmbientGradient(source);
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (Set(ref _coverAmbient, brush)) Raise(nameof(CoverAmbientBrush));
            });
        });
    }

    public bool IsLiked =>
        CurrentSong != null && PlaylistService.IsFavorite(CurrentSong.Id);

    ImageSource? _fullCover;
    public ImageSource? FullCover
    {
        get => _fullCover;
        private set => Set(ref _fullCover, value);
    }

    List<LyricLine> _lyrics = new();
    public List<LyricLine> Lyrics
    {
        get => _lyrics;
        private set { Set(ref _lyrics, value); Raise(nameof(HasLyrics)); }
    }

    public bool HasLyrics => _lyrics.Count > 0;

    int _lyricIndex = -1;
    public int LyricIndex
    {
        get => _lyricIndex;
        private set
        {
            if (Set(ref _lyricIndex, value))
                LyricIndexChanged?.Invoke();
        }
    }

    /// <summary>供视图滚动歌词到当前行</summary>
    public event Action? LyricIndexChanged;

    public void OnPlayerStateChanged()
    {
        Raise(nameof(CurrentSong));
        Raise(nameof(HasSong));
        Raise(nameof(IsPlaying));
        Raise(nameof(IsLiked));

        int seq = ++_loadSeq;
        var song = CurrentSong;
        FullCover = null;
        CoverBackground = null;
        Lyrics = new List<LyricLine>();
        LyricIndex = -1;
        if (song == null) return;

        Task.Run(() =>
        {
            var cover = song.CreateFullCover();
            var ambient = BuildAmbientBackground(cover);
            // 封面主色氛围背景（设置开启时）：提取海报关键色生成柔和渐变
            var coverAmbient = SettingsService.Current.CoverAmbientBackground
                ? BuildAmbientGradient(cover) : null;
            var lyrics = song.LyricPath != null
                ? LrcService.LoadFromFile(song.LyricPath)
                : new List<LyricLine>();

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => ApplyLoaded(seq, cover, ambient, coverAmbient, lyrics));
            }
            else
            {
                ApplyLoaded(seq, cover, ambient, coverAmbient, lyrics);
            }
        });
    }

    void ApplyLoaded(int seq, ImageSource? cover, Brush? ambient, Brush? coverAmbient, List<LyricLine> lyrics)
    {
        if (seq != _loadSeq) return;
        FullCover = cover;
        CoverBackground = ambient;
        _coverSource = cover as BitmapSource;
        if (Set(ref _coverAmbient, coverAmbient)) Raise(nameof(CoverAmbientBrush));
        Lyrics = lyrics;
        OnProgress();
    }

    /// <summary>
    /// 提取封面关键色：缩略采样 → 颜色桶计数 → 取相互距离足够分开的 Top3 →
    /// 按深浅主题压暗/提亮，生成柔和的对角渐变画刷。
    /// </summary>
    static Brush? BuildAmbientGradient(ImageSource? cover)
    {
        try
        {
            if (cover is not BitmapSource src || src.PixelWidth < 8 || src.PixelHeight < 8) return null;
            var scaled = new TransformedBitmap(src, new ScaleTransform(
                32.0 / src.PixelWidth, 32.0 / src.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(Int32Rect.Empty, pixels, w * 4, 0);

            // 4bit/通道量化分桶统计
            var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                if (a < 128) continue;
                int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                var e = buckets.TryGetValue(key, out var v) ? v : (Count: 0, R: 0L, G: 0L, B: 0L);
                buckets[key] = (e.Count + 1, e.R + r, e.G + g, e.B + b);
            }
            if (buckets.Count == 0) return null;

            var picked = new List<Color>();
            foreach (var e in buckets.OrderByDescending(b => b.Value.Count))
            {
                var v = e.Value;
                var c = Color.FromRgb((byte)(v.R / v.Count), (byte)(v.G / v.Count), (byte)(v.B / v.Count));
                if (picked.Any(p => ColorDistance(p, c) < 70)) continue;
                picked.Add(c);
                if (picked.Count == 3) break;
            }

            bool light = ThemeService.IsLight();
            var stops = picked
                .Select(c => Mix(c, light ? Colors.White : Colors.Black, light ? 0.72 : 0.58))
                .ToList();
            if (stops.Count == 0) return null;
            if (stops.Count == 1) stops.Add(Mix(stops[0], light ? Colors.White : Colors.Black, 0.35));

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            for (int i = 0; i < stops.Count; i++)
                brush.GradientStops.Add(new GradientStop(stops[i], (double)i / (stops.Count - 1)));
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

    static double ColorDistance(Color a, Color b)
        => Math.Sqrt(
            (a.R - b.R) * (double)(a.R - b.R) +
            (a.G - b.G) * (double)(a.G - b.G) +
            (a.B - b.B) * (double)(a.B - b.B));

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>将封面缩小并模糊，生成冻结的氛围背景画刷（在后台线程执行）</summary>
    static Brush? BuildAmbientBackground(ImageSource? cover)
    {
        if (cover is not BitmapSource src || src.PixelWidth < 8 || src.PixelHeight < 8) return null;
        try
        {
            double scale = 256.0 / Math.Max(src.PixelWidth, src.PixelHeight);
            var small = new TransformedBitmap(src, new ScaleTransform(scale, scale));

            var visual = new DrawingVisual { Effect = new BlurEffect { Radius = 14 } };
            using (var dc = visual.RenderOpen())
                dc.DrawImage(small, new Rect(0, 0, small.PixelWidth, small.PixelHeight));

            var rtb = new RenderTargetBitmap(small.PixelWidth, small.PixelHeight,
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var brush = new ImageBrush(rtb) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    public void OnProgress()
    {
        if (_lyrics.Count == 0) return;
        LyricIndex = LrcService.FindIndex(_lyrics,
            TimeSpan.FromSeconds(_main.Player.Position));
    }

    /// <summary>当前歌词行内的播放进度（0-1），供视图做逐字变色/放大渲染</summary>
    public double GetLineProgress()
    {
        var lyrics = _lyrics;
        int idx = _lyricIndex;
        if (lyrics.Count == 0 || idx < 0 || idx >= lyrics.Count) return 0;
        double start = lyrics[idx].Time.TotalSeconds;
        double end = idx + 1 < lyrics.Count
            ? lyrics[idx + 1].Time.TotalSeconds
            : _main.Player.Duration;
        if (end <= start + 0.05) return 1;
        return Math.Clamp((_main.Player.Position - start) / (end - start), 0, 1);
    }

    public RelayCommand ToggleLikeCommand { get; }
}

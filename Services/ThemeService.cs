using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MyQQMusic.Services;

/// <summary>
/// 运行时主题切换：通过替换 App.Resources.MergedDictionaries 中的
/// 调色板 / 强调色 / 背景三个槽位实现（所有颜色均以 DynamicResource 引用）。
/// 强调色按当前主题做对比度自适应（浅色向深调整、深色向亮调整），
/// 保证在任意主题下均有足够的文字对比度；内置背景按色系映射到另一
/// 主题的对应背景。
/// </summary>
public static class ThemeService
{
    const string Base = "pack://application:,,,/Themes/";

    public static readonly (string Key, string Name)[] ThemeOptions =
    {
        ("Dark", "深色"), ("Light", "浅色")
    };

    public static readonly (string Key, string Name, string Hex)[] AccentOptions =
    {
        ("Green", "QQ 绿", "#FF31C272"),
        ("Blue", "科技蓝", "#FF0078D4"),
        ("Purple", "幻紫", "#FFA670FF"),
        ("Pink", "桃粉", "#FFFF6B9D"),
        ("Orange", "暖橙", "#FFFFA94D")
    };

    public static readonly Dictionary<string, (string Key, string Name)[]> BackgroundOptions = new()
    {
        ["Dark"] = new[]
        {
            ("Default", "静谧灰"), ("Ocean", "深海蓝"), ("Forest", "墨林绿"),
            ("Sunset", "落日橙"), ("Violet", "极夜紫")
        },
        ["Light"] = new[]
        {
            ("Default", "晨雾白"), ("Morning", "暖沙米"), ("Frost", "霜天蓝"), ("Sakura", "樱花粉")
        }
    };

    /// <summary>播放页模式选项</summary>
    public static readonly (string Key, string Name)[] PlayThemeOptions =
    {
        ("Standard", "标准模式"), ("Vinyl", "黑胶模式"), ("Lyric", "歌词模式")
    };

    /// <summary>歌词高亮方式选项</summary>
    public static readonly (string Key, string Name)[] LyricHighlightOptions =
    {
        ("Sweep", "逐渐变色"), ("SweepGrow", "变色+逐渐放大"), ("Grow", "逐渐放大"), ("Fade", "整行变色")
    };

    /// <summary>旧版播放主题键归一化到新模式键（Classic→Standard，Immersive→Lyric）</summary>
    public static string NormalizePlayTheme(string key) => key switch
    {
        "Classic" => "Standard",
        "Immersive" => "Lyric",
        "Vinyl" or "Standard" or "Lyric" => key,
        _ => "Standard",
    };

    /// <summary>深浅主题切换时，内置背景按色系映射到新主题的近似背景</summary>
    static readonly Dictionary<string, string> BackgroundCrossMap = new()
    {
        ["Ocean"] = "Frost",   // 深海蓝 → 霜天蓝
        ["Sunset"] = "Morning",// 落日橙 → 暖沙米
        ["Violet"] = "Sakura", // 极夜紫 → 樱花粉
        ["Frost"] = "Ocean",
        ["Morning"] = "Sunset",
        ["Sakura"] = "Violet"
    };

    // App.xaml 中 MergedDictionaries 固定顺序：
    // 0 Icons.xaml  1 Styles.xaml  2 Palettes/*.xaml  3 Accents/*.xaml  4 Backgrounds/*.xaml
    static ResourceDictionary Dict(int index) =>
        Application.Current.Resources.MergedDictionaries[index];

    public static bool IsLight() => SettingsService.Current.Theme == "Light";

    public static void ApplyAll(string theme, string accent, string background)
    {
        ApplyTheme(theme);
        ApplyAccent(accent);
        ApplyBackground(background);
        ApplyLyricColor();
    }

    /// <summary>是否为可直接解析的颜色代码（#RGB/#RRGGBB/#AARRGGBB）</summary>
    public static bool IsHexColor(string? s)
    {
        if (string.IsNullOrEmpty(s) || s[0] != '#') return false;
        try { _ = (Color)ColorConverter.ConvertFromString(s); return true; }
        catch { return false; }
    }

    /// <summary>应用自定义歌词高亮色（空或非法则回落强调色）</summary>
    public static void ApplyLyricColor()
    {
        var resources = Application.Current.Resources;
        var hex = SettingsService.Current.LyricColor;
        if (!IsHexColor(hex))
        {
            resources.Remove("LyricActiveBrush");
            return;
        }
        var c = (Color)ColorConverter.ConvertFromString(hex);
        // 深色主题下过暗的自定义色适当提亮，保证歌词可读
        if (!IsLight() && RelativeLuminance(c) < 0.35) c = Mix(c, Colors.White, 0.3);
        SetOverride(resources, "LyricActiveBrush", c);
    }

    public static void ApplyTheme(string theme)
    {
        ApplyThemeCore(theme);
        // 主题变更后，播放条等通过绑定取刷子的地方需要重新求值
        SettingsService.NotifyAppearanceChanged();
    }

    static void ApplyThemeCore(string theme)
    {
        var t = theme == "Light" ? "Light" : "Dark";
        Dict(2).Source = new Uri($"{Base}Palettes/{t}.xaml");

        var settings = SettingsService.Current;

        // 强调色随深浅主题重新派生（浅色下自动加深以保证文字对比度）
        ApplyAccent(settings.Accent);
        // 歌词高亮色按新主题重新校正明度
        ApplyLyricColor();

        // 自定义背景（图片/颜色）：深浅切换后底色与侧栏蒙层需按新主题重建
        if (settings.Background is "Custom" or "CustomColor")
        {
            ApplyBackground(settings.Background);
            return;
        }

        // 切换明暗后，若当前背景不属于新主题则按色系映射到另一主题的对应背景
        if (!BackgroundOptions[t].Any(b => b.Key == settings.Background))
        {
            settings.Background = BackgroundCrossMap.TryGetValue(settings.Background, out var mapped)
                && BackgroundOptions[t].Any(b => b.Key == mapped) ? mapped : "Default";
            ApplyBackground(settings.Background);
        }
    }

    public static void ApplyAccent(string accent)
    {
        Color baseColor;
        if (IsHexColor(accent))
        {
            // 自定义强调色：直接以录入色为基色做主题自适应派生
            baseColor = (Color)ColorConverter.ConvertFromString(accent);
        }
        else
        {
            if (AccentOptions.All(a => a.Key != accent)) accent = "Green";
            Dict(3).Source = new Uri($"{Base}Accents/{accent}.xaml");
            baseColor = (Color)ColorConverter.ConvertFromString(AccentOptions.First(a => a.Key == accent).Hex);
        }
        ApplyAccentColors(baseColor);
    }

    /// <summary>
    /// 由基色派生全套强调色资源并在顶层覆盖：按当前主题循环微调明度，
    /// 浅色主题向黑靠拢、深色主题向白靠拢，直至相对亮度落入可读区间（≤0.22），
    /// 使同一份基色在深浅两种主题下都能满足文字对比度要求。
    /// </summary>
    static void ApplyAccentColors(Color baseColor)
    {
        bool light = IsLight();
        var c = baseColor;
        for (int i = 0; i < 6; i++)
        {
            double lum = RelativeLuminance(c);
            if (light ? lum <= 0.22 : lum >= 0.22) break;
            c = Mix(c, light ? Colors.Black : Colors.White, 0.12);
        }

        var resources = Application.Current.Resources;
        SetOverride(resources, "AccentBrush", c);
        SetOverride(resources, "AccentHoverBrush", light ? Mix(c, Colors.Black, 0.12) : Mix(c, Colors.White, 0.12));
        SetOverride(resources, "AccentPressedBrush", light ? Mix(c, Colors.Black, 0.25) : Mix(c, Colors.Black, 0.18));
        SetOverride(resources, "AccentAlphaBrush", WithAlpha(c, 0x1F));
        SetOverride(resources, "AccentGlowBrush", WithAlpha(c, 0x2E));
        SetOverride(resources, "SelectedRowBrush", WithAlpha(c, 0x1F));
        SetOverride(resources, "OnAccentBrush",
            RelativeLuminance(c) > 0.6 ? Color.FromRgb(0x1F, 0x23, 0x29) : Colors.White);
    }

    static void SetOverride(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    public static void ApplyBackground(string key)
    {
        ApplyBackgroundCore(key);
        SettingsService.NotifyAppearanceChanged();
    }

    static void ApplyBackgroundCore(string key)
    {
        var resources = Application.Current.Resources;
        resources.Remove("AppBackgroundBrush"); // 清除自定义图片覆盖
        resources.Remove("SidebarBrush");       // 清除侧边栏覆盖，恢复由背景字典联动

        // 丢弃可变画刷引用，重新进入自定义背景时按当前参数重建
        _customBrush = null;
        _customImage = null;
        _customSidebar = null;

        if (key == "Custom")
        {
            var path = SettingsService.Current.CustomBackgroundPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                InstallCustomBackground(resources);
                return;
            }
            key = "Default";
        }

        // 自定义背景色：由录入色派生柔和渐变底色与侧栏色
        if (key == "CustomColor")
        {
            var c = IsHexColor(SettingsService.Current.CustomBackgroundColor)
                ? (Color)ColorConverter.ConvertFromString(SettingsService.Current.CustomBackgroundColor)
                : Color.FromRgb(0x1A, 0x20, 0x28);
            var light = IsLight();
            var top = Mix(c, light ? Colors.White : Colors.Black, light ? 0.12 : 0.2);
            var bottom = Mix(c, light ? Colors.White : Colors.Black, light ? 0.45 : 0.55);
            var bg = new LinearGradientBrush(top, bottom, 135);
            bg.Freeze();
            resources["AppBackgroundBrush"] = bg;
            var side = new SolidColorBrush(Mix(c, light ? Colors.White : Colors.Black, light ? 0.62 : 0.35));
            side.Freeze();
            resources["SidebarBrush"] = side;
            return;
        }

        var theme = IsLight() ? "Light" : "Dark";
        var valid = BackgroundOptions[theme].Any(b => b.Key == key);
        if (!valid) key = "Default";

        Dict(4).Source = new Uri($"{Base}Backgrounds/{theme}_{key}.xaml");
    }

    // ---------- 自定义背景：可变画刷实时预览 ----------

    // 已解码原图缓存，避免拖动滑杆时反复解码同一文件
    static string? _cachedPath;
    static BitmapSource? _cachedBitmap;

    // 模糊结果缓存（按 路径+半径 失效），仅磨砂半径变化时才重建模糊图
    static double _cachedBlur = -1;
    static BitmapSource? _blurredBitmap;

    // 当前生效的可变（未冻结）画刷部件：透明度滑杆只改内部属性，
    // 不整体重建画刷、不替换资源，避免拖动时闪烁
    static DrawingBrush? _customBrush;
    static DrawingGroup? _customImage; // 图片层用 DrawingGroup 包裹以携带 Opacity
    static SolidColorBrush? _customSidebar;
    static double _customOpacity = -1;
    static bool _customLight;

    /// <summary>
    /// 安装自定义背景：不透明主题底色矩形 + 图片层（磨砂结果，透明度联动）
    /// 组成可变 DrawingBrush；图片层始终叠在底色上，结果不透明，
    /// 图片越淡遮罩越重，控件文字始终清晰。
    /// </summary>
    static void InstallCustomBackground(ResourceDictionary resources)
    {
        var settings = SettingsService.Current;
        var light = IsLight();
        var opacity = Math.Clamp(settings.CustomBackgroundOpacity, 0, 1);
        var blur = Math.Clamp(settings.CustomBackgroundBlur, 0, 40);

        var image = PrepareBackgroundImage(settings.CustomBackgroundPath!, blur);

        var baseColor = light ? Color.FromRgb(0xF7, 0xF8, 0xFA) : Color.FromRgb(0x0E, 0x10, 0x13);
        var baseBrush = new SolidColorBrush(baseColor);
        var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);

        // ImageDrawing 本身无 Opacity，包一层 DrawingGroup 承载图片层透明度
        _customImage = new DrawingGroup { Opacity = opacity };
        _customImage.Children.Add(new ImageDrawing(image, rect));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(rect)));
        group.Children.Add(_customImage);

        var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill };
        _customBrush = brush;
        _customLight = light;
        _customOpacity = opacity;
        resources["AppBackgroundBrush"] = brush;

        // 自定义图片时侧边栏使用跟随明暗主题的半透明蒙层；
        // 图片越透明（遮罩越重）蒙层越实，保证侧栏文字清晰
        _customSidebar = new SolidColorBrush(SidebarColor(light, opacity));
        resources["SidebarBrush"] = _customSidebar;
    }

    /// <summary>
    /// 实时联动图片层透明度：仅修改可变画刷内部属性，无重建无闪烁。
    /// 返回 false 表示当前不在自定义背景状态，调用方需回退到整体应用。
    /// </summary>
    public static bool UpdateCustomBackgroundOpacity(double opacity)
    {
        if (_customBrush == null || _customImage == null || _customSidebar == null) return false;
        opacity = Math.Clamp(opacity, 0, 1);
        if (Math.Abs(_customOpacity - opacity) < 1e-9) return true;

        _customImage.Opacity = opacity;
        _customOpacity = opacity;
        _customSidebar.Color = SidebarColor(_customLight, opacity);
        return true;
    }

    // 磨砂更新序号：拖动滑杆时只有最新一次任务的结果会被应用
    static int _blurSeq;

    /// <summary>
    /// 实时更新磨砂模糊度：后台线程生成模糊图，回到 UI 线程后仅替换画刷内部的
    /// Drawing 内容，画刷实例与资源引用不变，拖动滑杆无闪烁。
    /// 返回 false 表示当前不在自定义背景状态，调用方需回退到整体应用。
    /// </summary>
    public static bool UpdateCustomBackgroundBlur(double blur)
    {
        if (_customBrush == null) return false;
        var path = SettingsService.Current.CustomBackgroundPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        blur = Math.Clamp(blur, 0, 40);

        int seq = ++_blurSeq;
        Task.Run(() =>
        {
            if (seq != _blurSeq) return; // 已有更新的目标值，放弃本次生成
            BitmapSource image;
            try { image = PrepareBackgroundImage(path, blur); }
            catch { return; }
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (seq != _blurSeq || _customBrush == null) return;
                var baseColor = _customLight
                    ? Color.FromRgb(0xF7, 0xF8, 0xFA) : Color.FromRgb(0x0E, 0x10, 0x13);
                var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
                var imageLayer = new DrawingGroup { Opacity = _customOpacity };
                imageLayer.Children.Add(new ImageDrawing(image, rect));
                var group = new DrawingGroup();
                group.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(baseColor), null, new RectangleGeometry(rect)));
                group.Children.Add(imageLayer);
                _customBrush.Drawing = group; // 仅换内容，画刷实例不变
                _customImage = imageLayer;
            });
        });
        return true;
    }

    /// <summary>取用于合成的背景图：blur=0 返回原图（避免缩小导致发虚），否则返回缩小并模糊后的图（磨砂）</summary>
    static BitmapSource PrepareBackgroundImage(string path, double blur)
    {
        if (_cachedPath != path || _cachedBitmap == null)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 1280; // 背景用途，限制解码尺寸控制内存
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _cachedPath = path;
            _cachedBitmap = bmp;
            _blurredBitmap = null;
            _cachedBlur = -1;
        }
        var source = _cachedBitmap;

        if (blur <= 0) return source;
        if (_blurredBitmap != null && Math.Abs(_cachedBlur - blur) < 1e-9) return _blurredBitmap;

        // 缩小到 1/4 再模糊：等效半径按比例缩小，代价更低且放大后天然平滑
        const double scale = 0.25;
        var small = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        small.Freeze();

        var blurVisual = new DrawingVisual { Effect = new BlurEffect { Radius = Math.Max(1, blur * scale) } };
        using (var dc = blurVisual.RenderOpen())
            dc.DrawImage(small, new Rect(0, 0, small.PixelWidth, small.PixelHeight));
        var blurred = new RenderTargetBitmap(small.PixelWidth, small.PixelHeight, 96, 96,
            PixelFormats.Pbgra32);
        blurred.Render(blurVisual);
        blurred.Freeze();

        _cachedBlur = blur;
        _blurredBitmap = blurred;
        return blurred;
    }

    /// <summary>侧栏蒙层颜色：图片越淡（透明度越低）蒙层越实，保证侧栏文字清晰</summary>
    static Color SidebarColor(bool light, double opacity)
    {
        double extra = (1.0 - opacity) * 0.25;
        byte baseA = light ? (byte)0xCC : (byte)0xB3;
        byte a = (byte)Math.Min(0xF2, baseA + (int)(extra * 255));
        return light ? Color.FromArgb(a, 0xFF, 0xFF, 0xFF) : Color.FromArgb(a, 0x00, 0x00, 0x00);
    }

    // ---------- 颜色工具 ----------

    /// <summary>按 t 比例混合两种颜色（t=0 返回 a，t=1 返回 b）</summary>
    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>WCAG 相对亮度（0-1），用于判断文字取黑还是白</summary>
    static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}

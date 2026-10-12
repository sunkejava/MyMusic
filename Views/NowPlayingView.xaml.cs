using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MyQQMusic.Services;
using MyQQMusic.ViewModels;

namespace MyQQMusic.Views;

public partial class NowPlayingView : UserControl
{
    NowPlayingViewModel? _vm;
    bool _spinning;
    PlayerSkin _skin = PlayerSkins.Default;

    public NowPlayingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { ApplySkin(); StartFxLoop(); };
        Unloaded += (_, _) => { StopLyricScroll(); StopFxLoop(); };
    }

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.LyricIndexChanged -= OnLyricIndexChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as NowPlayingViewModel;
        if (_vm != null)
        {
            _vm.LyricIndexChanged += OnLyricIndexChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            SetSpinning(_vm.IsPlaying);
            ApplySkin();
        }
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NowPlayingViewModel.IsPlaying):
                SetSpinning(_vm?.IsPlaying == true);
                UpdateFxActive();
                break;
            case nameof(NowPlayingViewModel.Skin):
                ApplySkin();
                break;
            case nameof(NowPlayingViewModel.CoverBackground):
                // 全屏封面皮肤随歌曲封面更新背景
                if (_skin.FullCover) ApplySkinBackground();
                break;
            case nameof(NowPlayingViewModel.CoverAmbientBrush):
                // 封面主色氛围背景随歌曲/设置变化
                ApplySkinBackground();
                break;
        }
    }

    // ---------- 右上角页面控制（模式 / 全屏纯享 / 窗口） ----------

    MainWindow? Host => Window.GetWindow(this) as MainWindow;

    void PageControls_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 按钮已处理自身点击；空白处按下可拖动窗口（全屏纯享时这是唯一拖动入口）
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { Host?.DragMove(); } catch { }
    }

    void Collapse_Click(object sender, RoutedEventArgs e)
    {
        SetSidePanelOpen(false);
        Host?.HidePlayerOverlay();
    }

    // ---------- 右侧抽屉：播放模式 + 播放皮肤 ----------

    bool _sidePanelOpen;

    void SwitchMode_Click(object sender, RoutedEventArgs e) => SetSidePanelOpen(!_sidePanelOpen);

    void CloseSidePanel_Click(object sender, RoutedEventArgs e) => SetSidePanelOpen(false);

    void SetSidePanelOpen(bool open)
    {
        if (_sidePanelOpen == open) return;
        _sidePanelOpen = open;
        if (open)
        {
            SidePanel.Visibility = Visibility.Visible;
            SidePanelMove.BeginAnimation(TranslateTransform.XProperty, null);
            SidePanelMove.X = 350;
            var anim = new DoubleAnimation(350, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            SidePanelMove.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            var anim = new DoubleAnimation(0, 350, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) => SidePanel.Visibility = Visibility.Collapsed;
            SidePanelMove.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    void ModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ButtonBase { Uid: string key } && _vm != null)
            _vm.SetMode(key);
    }

    void SkinClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ButtonBase { Uid: string key } && _vm != null)
            _vm.SetSkin(key);
    }

    void PureMode_Click(object sender, RoutedEventArgs e) => Host?.TogglePureMode();

    void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (Host != null) Host.WindowState = WindowState.Minimized;
    }

    void Maximize_Click(object sender, RoutedEventArgs e) => Host?.ToggleMaximize();

    void Close_Click(object sender, RoutedEventArgs e) => Host?.Close();

    // ---------- 皮肤 ----------

    /// <summary>按皮肤参数设置背景、唱片配色、光晕、星空、水印与音波氛围</summary>
    void ApplySkin()
    {
        _skin = _vm?.Skin ?? PlayerSkins.Default;
        ApplySkinBackground();

        // 皮肤配色板：非默认皮肤生效时，整页文字/歌词/图标颜色跟随皮肤
        bool bgActive = SkinBg.Visibility == Visibility.Visible;
        bool palette = bgActive && _skin.Key != "Default";
        ApplySkinPalette(palette);

        // 可读性遮罩：仅全屏封面皮肤与默认皮肤的氛围背景需要（配色板生效时文字已适配皮肤）
        if (bgActive && (!palette || _skin.FullCover))
        {
            SkinScrim.Visibility = Visibility.Visible;
            SkinScrim.Fill = palette
                ? new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00))
                : SettingsService.Current.Theme == "Light"
                    ? new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0x00, 0x00));
        }
        else SkinScrim.Visibility = Visibility.Collapsed;

        // 生肖水印大字
        GlyphText.Text = _skin.Glyph;
        if (_skin.Glyph.Length > 0)
            GlyphText.Foreground = Solid(_skin.GlyphColor ?? _skin.Glow ?? "#FFFFFF", Colors.White);

        // 星空 / 星座连线
        string? pts = null, edges = null;
        if (_skin.Stars && _skin.StarsShape != null &&
            PlayerSkins.ConstellationShapes.TryGetValue(_skin.StarsShape, out var shape))
        {
            pts = shape.Points;
            edges = shape.Edges;
        }
        StarLayer.SetPattern(pts, edges, HexColor(_skin.Glow ?? "#FFD666", Colors.Gold), _skin.Stars);

        // 唱片染色 / 厂牌圈 / 光晕
        DiscTint.Fill = DiscBrush(_skin.DiscColor);
        DiscTint.Opacity = _skin.DiscColor != null ? 0.5 : 0;
        DiscHub.Fill = _skin.Label != null
            ? Solid(_skin.Label, Colors.Gold)
            : (Brush)FindResource("CoverPlaceholderBrush");
        GlowEllipse.Fill = _skin.Glow != null
            ? GlowBrush(_skin.Glow)
            : (Brush)FindResource("AccentGlowBrush");

        // 流光扫过
        SheenClip.Visibility = _skin.Light ? Visibility.Visible : Visibility.Collapsed;
        UpdateSheenAnimation();

        // 音波：环形画在唱片层，其余画在页面层
        ConfigureWave(PageWave, discLevel: false);
        ConfigureWave(DiscWave, discLevel: true);
        UpdateFxActive();
    }

    void ApplySkinBackground()
    {
        // 背景优先级：皮肤背景（非默认）> 封面主色氛围（默认皮肤且开启）> 跟随应用主题
        Brush? bg = _skin.Key != "Default"
            ? (_skin.FullCover && _vm?.CoverBackground is { } ambient ? ambient : Gradient(_skin.Bg))
            : SettingsService.Current.CoverAmbientBackground && _vm?.CoverAmbientBrush is { } amb
                ? amb
                : null;
        if (bg == null)
        {
            SkinBg.Visibility = Visibility.Collapsed;
            _vm?.SetPageBackground(null); // 播放条恢复主题背景
            return;
        }
        SkinBg.Background = bg;
        SkinBg.Visibility = Visibility.Visible;
        _vm?.SetPageBackground(bg); // 播放页打开时，主窗口播放条背景与播放页一致
    }

    /// <summary>页面级颜色覆盖键（DynamicResource 查找优先命中视图级资源）</summary>
    static readonly string[] PaletteKeys =
    [
        "PrimaryTextBrush", "SecondaryTextBrush", "TertiaryTextBrush",
        "IconBrush", "LyricActiveBrush", "LyricInactiveBrush", "QueuePanelBrush",
    ];

    /// <summary>皮肤配色板：按皮肤底色明暗覆盖播放页内的文字/歌词/图标颜色，使整页跟随皮肤</summary>
    void ApplySkinPalette(bool active)
    {
        foreach (var key in PaletteKeys) Resources.Remove(key);
        if (!active)
        {
            _vm?.SetPagePalette(null);
            return;
        }

        bool dark = !SkinIsLight();
        var applied = new Dictionary<string, Brush>
        {
            ["PrimaryTextBrush"] = Solid(dark ? "#F5F7FA" : "#1F2328", Colors.White),
            ["SecondaryTextBrush"] = Solid(dark ? "#C6CCD4" : "#4B5259", Colors.Gray),
            ["TertiaryTextBrush"] = Solid(dark ? "#98A0AA" : "#79818B", Colors.Gray),
            ["IconBrush"] = Solid(dark ? "#D8DDE3" : "#33393F", Colors.Gray),
            // 右侧抽屉面板底色随皮肤明暗，避免浅色主题下白底配浅色文字
            ["QueuePanelBrush"] = Solid(dark ? "#1F2125" : "#F7F8FA", Colors.Gray),
        };

        // 歌词高亮色：皮肤光晕/音波色，缺失时回退主题强调色
        string? accent = _skin.Glow ?? _skin.WaveColor;
        applied["LyricActiveBrush"] = accent != null
            ? Solid(accent, Colors.Gold)
            : (Brush)FindResource("AccentBrush");
        applied["LyricInactiveBrush"] = new SolidColorBrush(dark
            ? Color.FromArgb(0xB3, 0xB8, 0xBF, 0xC7)
            : Color.FromArgb(0xB3, 0x5A, 0x61, 0x69));

        foreach (var (key, brush) in applied) Resources[key] = brush;
        _vm?.SetPagePalette(applied);
    }

    /// <summary>皮肤底色是否浅色调（渐变色的平均亮度）；全屏封面皮肤按深色处理</summary>
    bool SkinIsLight()
    {
        if (_skin.FullCover || _skin.Bg.Length == 0) return false;
        double sum = 0;
        int n = 0;
        foreach (var hex in _skin.Bg)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                sum += 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                n++;
            }
            catch { }
        }
        return n > 0 && sum / n > 150;
    }

    void ConfigureWave(WaveFxLayer layer, bool discLevel)
    {
        bool isCircle = _skin.Wave == WaveKind.CircleBars;
        layer.Kind = discLevel
            ? (isCircle ? _skin.Wave : WaveKind.None)
            : (!isCircle ? _skin.Wave : WaveKind.None);
        layer.Color = HexColor(_skin.WaveColor ?? _skin.Glow ?? "#60A0FF", Color.FromRgb(0x60, 0xA0, 0xFF));
        layer.Dense = _skin.WaveDense;
        layer.Reset();
    }

    static Color HexColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    static SolidColorBrush Solid(string hex, Color fallback) => new(HexColor(hex, fallback));

    static Brush? DiscBrush(string? discColor)
    {
        switch (discColor)
        {
            case null: return null;
            case "rainbow": return Gradient(["#FFFF9AA8", "#FFFFD27A", "#FFA8E6A0", "#FF8FD0FF", "#FFC9A2FF"]);
            case "gold": return Gradient(["#FFEFC76A", "#FFB08A30", "#FF8A6A22"]);
            default: return Solid(discColor, Colors.Gray);
        }
    }

    static LinearGradientBrush Gradient(string[] hexes)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < hexes.Length; i++)
            b.GradientStops.Add(new GradientStop(HexColor(hexes[i], Colors.Black),
                hexes.Length == 1 ? 0 : (double)i / (hexes.Length - 1)));
        b.Freeze();
        return b;
    }

    static RadialGradientBrush GlowBrush(string hex)
    {
        var c = HexColor(hex, Colors.White);
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1));
        b.Freeze();
        return b;
    }

    // ---------- 播放中逐帧驱动（歌词高亮 + 音波流动 + 流光） ----------

    bool _fxRunning;
    DateTime _lastRender = DateTime.UtcNow;

    void StartFxLoop()
    {
        if (_fxRunning) return;
        _fxRunning = true;
        _lastRender = DateTime.UtcNow;
        CompositionTarget.Rendering += OnFxRendering;
    }

    void StopFxLoop()
    {
        if (!_fxRunning) return;
        _fxRunning = false;
        CompositionTarget.Rendering -= OnFxRendering;
    }

    void OnFxRendering(object? sender, EventArgs e)
    {
        if (_vm == null) return;
        var now = DateTime.UtcNow;
        double dt = Math.Min(0.05, (now - _lastRender).TotalSeconds);
        _lastRender = now;
        bool playing = _vm.IsPlaying;

        UpdateLyricProgress();

        if (playing)
        {
            if (PageWave.Kind != WaveKind.None) PageWave.Tick(dt);
            if (DiscWave.Kind != WaveKind.None) DiscWave.Tick(dt);
        }
    }

    void UpdateFxActive()
    {
        bool playing = _vm?.IsPlaying == true;
        PageWave.Active = playing && PageWave.Kind != WaveKind.None;
        DiscWave.Active = playing && DiscWave.Kind != WaveKind.None;
        if (!playing)
        {
            PageWave.Reset();
            DiscWave.Reset();
        }
        UpdateSheenAnimation();
    }

    DoubleAnimation? _sheenAnim;

    void UpdateSheenAnimation()
    {
        bool run = _skin.Light && _vm?.IsPlaying == true && IsLoaded;
        if (run && _sheenAnim == null)
        {
            _sheenAnim = new DoubleAnimation(-240, 240, TimeSpan.FromSeconds(3.2))
            { RepeatBehavior = RepeatBehavior.Forever };
            SheenMove.BeginAnimation(TranslateTransform.XProperty, _sheenAnim);
        }
        else if (!run && _sheenAnim != null)
        {
            SheenMove.BeginAnimation(TranslateTransform.XProperty, null);
            _sheenAnim = null;
        }
    }

    // ---------- 歌词行内高亮（逐渐变色 / 逐渐放大） ----------

    TextBlock? _accentHost;      // 当前行的强调色文字（Tag=ACCENT）
    RectangleGeometry? _accentClip;
    ScaleTransform? _growScale;  // 当前行内容的放大变换

    void UpdateLyricProgress()
    {
        if (_vm == null) return;
        string mode = SettingsService.Current.LyricHighlight;
        bool sweep = mode is "Sweep" or "SweepGrow";
        bool grow = mode is "Grow" or "SweepGrow";
        double p = _vm.GetLineProgress();

        if (_accentHost != null)
        {
            if (sweep)
            {
                if (_accentClip == null)
                {
                    _accentClip = new RectangleGeometry();
                    _accentHost.Clip = _accentClip;
                }
                _accentClip.Rect = new Rect(0, 0,
                    _accentHost.ActualWidth * p, Math.Max(1, _accentHost.ActualHeight));
            }
            else if (_accentClip != null)
            {
                _accentHost.Clip = null;
                _accentClip = null;
            }
        }
        if (_growScale != null)
        {
            double s = grow ? 1 + 0.10 * p : 1;
            if (_growScale.ScaleX != s)
            {
                _growScale.ScaleX = s;
                _growScale.ScaleY = s;
            }
        }
    }

    /// <summary>切换当前歌词行时，重新定位该行的强调文字与放大变换</summary>
    void ResolveLyricTargets(int index)
    {
        // 清理旧行残留的裁剪与放大
        if (_accentHost != null) _accentHost.Clip = null;
        if (_growScale != null) { _growScale.ScaleX = 1; _growScale.ScaleY = 1; }
        _accentHost = null;
        _accentClip = null;
        _growScale = null;
        if (index < 0) return;

        if (LyricList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;
        container.ApplyTemplate();
        _accentHost = FindDescendant(container,
            d => d is TextBlock tb && (tb.Tag as string) == "ACCENT") as TextBlock;
        _growScale = container.FindName("GrowScale") as ScaleTransform;
    }

    static DependencyObject? FindDescendant(DependencyObject root, Func<DependencyObject, bool> match)
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (match(child)) return child;
            var result = FindDescendant(child, match);
            if (result != null) return result;
        }
        return null;
    }

    // ---------- 黑胶旋转 ----------

    /// <summary>播放时黑胶旋转，暂停时停在当前角度</summary>
    void SetSpinning(bool spinning)
    {
        if (spinning == _spinning) return;
        _spinning = spinning;

        if (spinning)
        {
            var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(22))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            DiscRot.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        else
        {
            double angle = DiscRot.Angle;
            DiscRot.BeginAnimation(RotateTransform.AngleProperty, null);
            DiscRot.Angle = angle;
        }
    }

    // ---------- 歌词平滑滚动 ----------

    int _lastScrolledIndex = -1;

    // VerticalOffset 是只读依赖属性，.NET (Core) 3.0+ 其元数据带 IsAnimationProhibited 标记，
    // BeginAnimation 会抛 ArgumentException；改用渲染回调逐帧驱动实现同样的平滑滚动。
    EventHandler? _scrollRender;
    ScrollViewer? _scrollTarget;
    double _scrollFrom, _scrollTo;
    DateTime _scrollStart;
    const int ScrollMs = 550;

    void StopLyricScroll()
    {
        if (_scrollRender == null) return;
        CompositionTarget.Rendering -= _scrollRender;
        _scrollRender = null;
        _scrollTarget = null;
    }

    /// <summary>歌词行切换时，把当前行平滑滚动到视口中央</summary>
    void OnLyricIndexChanged()
    {
        // 延迟到 Loaded 优先级：等容器生成与布局更新完成后再计算目标位置，
        // 避免基于过期布局动画到旧位置而产生二段跳动
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollToCurrentLyric);
    }

    void ScrollToCurrentLyric()
    {
        int index = LyricList.SelectedIndex;
        if (index < 0)
        {
            ResolveLyricTargets(-1);
            _lastScrolledIndex = -1;
            return;
        }
        ResolveLyricTargets(index);
        if (index == _lastScrolledIndex) return; // 同一行重复触发直接合并
        _lastScrolledIndex = index;

        var sv = FindScrollViewer(LyricList);
        var container = LyricList.ItemContainerGenerator
            .ContainerFromIndex(index) as FrameworkElement;
        if (sv == null || container == null)
        {
            _lastScrolledIndex = -1;
            if (LyricList.SelectedItem != null)
                LyricList.ScrollIntoView(LyricList.SelectedItem);
            return;
        }

        GeneralTransform gt;
        try { gt = container.TransformToVisual((Visual)sv.Content); }
        catch { _lastScrolledIndex = -1; return; }

        // 目标偏移：当前行中心位于视口中央，并限制在可滚动范围内
        Point rowCenter = gt.Transform(new Point(0, container.ActualHeight / 2));
        double target = Math.Clamp(rowCenter.Y - sv.ViewportHeight / 2, 0, sv.ScrollableHeight);

        // 已在目标位置附近（如用户刚手动滚动到位）：跳过动画，避免多余抖动
        if (Math.Abs(target - sv.VerticalOffset) < 1.0) return;

        // 平滑滚动：渲染回调逐帧驱动（VerticalOffset 禁止 BeginAnimation），
        // 快速换行时从当前瞬时偏移起步，衔接顺滑；缓动与时长保持原观感。
        StopLyricScroll();
        _scrollTarget = sv;
        _scrollFrom = sv.VerticalOffset;
        _scrollTo = target;
        _scrollStart = DateTime.UtcNow;
        _scrollRender = (_, _) =>
        {
            if (_scrollTarget == null) return;
            double t = Math.Min(1.0, (DateTime.UtcNow - _scrollStart).TotalMilliseconds / ScrollMs);
            double eased = 1 - Math.Pow(1 - t, 5); // 等效 QuinticEase EaseOut
            _scrollTarget.ScrollToVerticalOffset(_scrollFrom + (_scrollTo - _scrollFrom) * eased);
            if (t >= 1.0) StopLyricScroll();
        };
        CompositionTarget.Rendering += _scrollRender;
    }

    /// <summary>在控件子树中查找 ScrollViewer（不依赖默认模板部件名）</summary>
    static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }
}

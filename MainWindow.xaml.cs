using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using MyQQMusic.Models;
using MyQQMusic.Services;
using MyQQMusic.ViewModels;
using MyQQMusic.Views;

namespace MyQQMusic;

/// <summary>
/// 主窗口：无边框框架、窗口按钮、拖拽、进度条拖拽事件挂接
/// </summary>
public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    double _lastVolume = 0.7;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // 进度条 Thumb 拖拽（Slider 自身不暴露 DragStarted/Completed）
        ProgressSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _vm.BeginProgressDrag()));
        ProgressSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => _vm.EndProgressDrag(ProgressSlider.Value)));

        // 可选的滚动方向反转：部分触摸板系统级“自然滚动”会让传统应用滚向相反
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), true);

        // 播放条歌曲海报随播放状态旋转 / 停转；播放页皮肤配色同步到播放条文字/图标
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPlaying))
                SetBarCoverSpinning(_vm.IsPlaying);
            else if (e.PropertyName == nameof(NowPlayingViewModel.PagePalette))
                ApplyWindowPalette(_overlayOpen ? _vm.NowPlaying.PagePalette : null);
        };
        Loaded += (_, _) => SetBarCoverSpinning(_vm.IsPlaying);
    }

    void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || !SettingsService.Current.ReverseScroll) return;
        e.Handled = true;
        // 取反 delta 后以冒泡方式重新投递给原来源，恢复与系统一致的滚动方向
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, -e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = e.Source,
        };
        (e.OriginalSource as IInputElement)?.RaiseEvent(args);
    }

    // ---------- 顶栏 / 窗口控制 ----------

    void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    public void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // ---------- 全屏纯享模式（播放页按钮调用） ----------

    bool _pureMode;
    WindowState _prePureState;

    /// <summary>全屏纯享：隐藏顶栏与侧栏并最大化，只保留播放内容与播放条；再次调用恢复</summary>
    public void TogglePureMode()
    {
        _pureMode = !_pureMode;
        if (_pureMode)
        {
            _prePureState = WindowState;
            TopBar.Visibility = Visibility.Collapsed;
            Sidebar.Visibility = Visibility.Collapsed;
            NavColumn.Width = new GridLength(0);
            RootBorder.CornerRadius = new CornerRadius(0);
            WindowState = WindowState.Maximized;
        }
        else
        {
            TopBar.Visibility = Visibility.Visible;
            Sidebar.Visibility = Visibility.Visible;
            NavColumn.Width = new GridLength(196);
            RootBorder.CornerRadius = new CornerRadius(12);
            WindowState = _prePureState == WindowState.Maximized ? WindowState.Normal : _prePureState;
        }
    }

    // ---------- 播放条 ----------

    void Cover_Click(object sender, MouseButtonEventArgs e) => ShowPlayerOverlay();

    void ModeButton_Click(object sender, RoutedEventArgs e) => _vm.Mode = _vm.Mode;

    /// <summary>播放页打开时，把皮肤配色板应用到主窗口层，使播放条文字/图标与播放页配色一致；关闭后恢复主题</summary>
    List<string>? _appliedPaletteKeys;

    void ApplyWindowPalette(IReadOnlyDictionary<string, Brush>? palette)
    {
        if (_appliedPaletteKeys != null)
        {
            foreach (var key in _appliedPaletteKeys) Resources.Remove(key);
            _appliedPaletteKeys = null;
        }
        if (palette == null) return;
        _appliedPaletteKeys = [.. palette.Keys];
        foreach (var (key, brush) in palette) Resources[key] = brush;
    }

    /// <summary>播放条歌曲海报：播放时匀速旋转，暂停时停在当前角度</summary>
    void SetBarCoverSpinning(bool spinning)
    {
        double angle = (double)BarCoverRot.GetValue(RotateTransform.AngleProperty) % 360;
        BarCoverRot.BeginAnimation(RotateTransform.AngleProperty, null);
        BarCoverRot.Angle = angle;
        if (!spinning) return;
        var anim = new DoubleAnimation(angle, angle + 360, TimeSpan.FromSeconds(18))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        BarCoverRot.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    // ---------- 正在播放页浮层（网易云式：从下往上滑出，向下按钮收起） ----------

    bool _overlayOpen;

    /// <summary>打开正在播放页：上滑覆盖顶栏与内容区，播放条保留作为控制条</summary>
    public void ShowPlayerOverlay()
    {
        if (_overlayOpen) return;
        _overlayOpen = true;
        _vm.OverlayOpen = true; // 播放条背景随播放页同步
        _vm.NowPlaying.OnPlayerStateChanged(); // 进入前同步当前播放状态
        ApplyWindowPalette(_vm.NowPlaying.PagePalette); // 播放条文字/图标随播放页配色
        PlayerOverlay.Visibility = Visibility.Visible;
        double height = ActualHeight;
        PlayerOverlayMove.BeginAnimation(TranslateTransform.YProperty, null);
        PlayerOverlayMove.Y = height;
        var anim = new DoubleAnimation(height, 0, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        PlayerOverlayMove.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    /// <summary>收起正在播放页：下滑隐藏，回到之前的页面</summary>
    public void HidePlayerOverlay()
    {
        if (!_overlayOpen) return;
        _overlayOpen = false;
        _vm.OverlayOpen = false; // 播放条背景恢复主题
        ApplyWindowPalette(null); // 播放条文字/图标恢复主题配色
        var anim = new DoubleAnimation(0, ActualHeight, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) => PlayerOverlay.Visibility = Visibility.Collapsed;
        PlayerOverlayMove.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    // ---------- 播放队列面板（右侧滑出） ----------

    bool _queueOpen;

    void QueueButton_Click(object sender, RoutedEventArgs e) => SetQueueOpen(!_queueOpen);

    void QueueClose_Click(object sender, RoutedEventArgs e) => SetQueueOpen(false);

    void SetQueueOpen(bool open)
    {
        if (_queueOpen == open) return;
        _queueOpen = open;
        if (open)
        {
            QueuePanel.Visibility = Visibility.Visible;
            QueueTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            QueueTranslate.X = 350;
            var anim = new DoubleAnimation(350, 0, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            QueueTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            var anim = new DoubleAnimation(0, 350, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (_, _) => QueuePanel.Visibility = Visibility.Collapsed;
            QueueTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    void QueueList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        // 从点击源沿视觉树向上找列表项，不依赖选中状态更新的时序；找不到再回退 SelectedItem
        SongInfo? song = null;
        var d = e.OriginalSource as DependencyObject;
        try
        {
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            if (d is ListBoxItem lbi) song = lbi.Content as SongInfo;
        }
        catch { }
        song ??= lb.SelectedItem as SongInfo;
        if (song == null) return;
        _vm.PlayQueueSong(song);
        lb.SelectedItem = null; // 高亮以“正在播放”为准，清除选中残留
    }

    // ---------- 收藏 / 桌面歌词 / 更多 ----------

    void FavoriteToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Player.CurrentSong is not SongInfo song) return;
        var dlg = new FavoriteDialog(song) { Owner = this };
        dlg.ShowDialog();
    }

    void DeskLyric_Click(object sender, RoutedEventArgs e) => DeskLyricWindow.Toggle(_vm);

    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var song = _vm.Player.CurrentSong;
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("QQMenuStyle"),
            PlacementTarget = (FrameworkElement)sender,
            Placement = PlacementMode.Top
        };
        menu.Items.Add(MakeMenuItem("打开正在播放页", "IconLyric",
            (_, _) => ShowPlayerOverlay(), song != null));
        menu.Items.Add(MakeMenuItem("分享（复制歌曲信息）", "IconMusic", (_, _) =>
        {
            if (song == null) return;
            try { Clipboard.SetText($"{song.DisplayTitle} - {song.Artist}"); } catch { }
        }, song != null));
        menu.Items.Add(MakeMenuItem("复制文件路径", "IconFolder", (_, _) =>
        {
            if (song == null) return;
            try { Clipboard.SetText(song.Id); } catch { }
        }, song != null));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("同步在线元信息…", "IconImage", (_, _) =>
        {
            if (song == null) return;
            var dlg = new SyncMetaWindow(song) { Owner = this };
            dlg.ShowDialog();
        }, song != null));
        menu.IsOpen = true;
    }

    static MenuItem MakeMenuItem(string header, string iconKey, RoutedEventHandler onClick, bool enabled)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)Application.Current.FindResource("QQMenuItemStyle"),
            IsEnabled = enabled
        };
        item.Icon = new Path
        {
            Data = (Geometry)Application.Current.FindResource(iconKey),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Fill = (Brush)Application.Current.FindResource("IconBrush")
        };
        item.Click += onClick;
        return item;
    }

    void VolumeMute_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsMuted)
            _vm.Volume = _lastVolume > 0.01 ? _lastVolume : 0.7;
        else
        {
            _lastVolume = _vm.Volume;
            _vm.Volume = 0;
        }
    }

    void ProgressSlider_Click(object sender, MouseButtonEventArgs e) =>
        _vm.EndProgressDrag(ProgressSlider.Value);

    // ---------- 生命周期 ----------

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // 最大化时补偿 WindowChrome 溢出，去掉圆角
        if (WindowState == WindowState.Maximized)
        {
            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.Padding = new Thickness(7);
            PlayerOverlay.CornerRadius = new CornerRadius(0);
        }
        else
        {
            RootBorder.CornerRadius = new CornerRadius(12);
            RootBorder.Padding = new Thickness(0);
            PlayerOverlay.CornerRadius = new CornerRadius(12, 12, 0, 0);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _vm.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyQQMusic.Models;
using MyQQMusic.Services;
using MyQQMusic.ViewModels;

namespace MyQQMusic.Views;

/// <summary>
/// 桌面歌词窗：全透明悬浮、可拖动，展示当前/下一行歌词；
/// 暂停或无歌词时展示歌名与歌手；字体/颜色/背景可在设置页配置。
/// </summary>
public partial class DeskLyricWindow : Window
{
    static readonly List<DeskLyricWindow> Instances = [];

    readonly MainViewModel _vm;
    List<LyricLine> _lyrics = [];
    string _lyricSongId = "";
    int _lyricIndex = -1;
    int _loadSeq;

    public DeskLyricWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Instances.Add(this);
        Closed += (_, _) =>
        {
            Instances.Remove(this);
            _vm.Player.StateChanged -= OnPlayerStateChanged;
            _vm.Player.ProgressChanged -= OnProgressChanged;
            CompositionTarget.Rendering -= OnSweepRender;
        };
        _vm.Player.StateChanged += OnPlayerStateChanged;
        _vm.Player.ProgressChanged += OnProgressChanged;
        Loaded += (_, _) =>
        {
            Apply();
            PlaceAtBottom();
            LoadLyricsForCurrent();
            UpdateContent();
            CompositionTarget.Rendering += OnSweepRender;
        };
    }

    /// <summary>开关切换：已存在则全部关闭，否则打开一个新窗口</summary>
    public static void Toggle(MainViewModel vm)
    {
        if (Instances.Count > 0)
        {
            foreach (var w in Instances.ToArray()) w.Close();
            return;
        }
        new DeskLyricWindow(vm).Show();
    }

    /// <summary>设置页修改后实时应用到所有已打开的桌面歌词窗</summary>
    public static void ApplySettings()
    {
        foreach (var w in Instances.ToArray()) w.Apply();
    }

    /// <summary>当前歌曲歌词文件更新后（如在线同步歌词），强制所有窗口重新加载歌词</summary>
    public static void InvalidateLyrics()
    {
        foreach (var w in Instances.ToArray())
        {
            w._lyricSongId = "";
            w.Dispatcher.BeginInvoke(() =>
            {
                w.LoadLyricsForCurrent();
                UpdateLyricLineSafe(w);
            });
        }
    }

    static void UpdateLyricLineSafe(DeskLyricWindow w)
    {
        // 先按当前播放位置直接定位新歌词行，避免等待下一次进度事件才刷新
        w.UpdateContent();
        if (w._vm.Player.IsPlaying) w.UpdateLyricLine(w._vm.Player.Position);
    }

    void PlaceAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 60;
    }

    // ---------- 外观（读取设置） ----------

    void Apply()
    {
        var s = SettingsService.Current;
        Root.Background = WithAlpha(ParseColor(s.DeskLyricBgColor, Colors.Black), s.DeskLyricBgOpacity);

        var fg = ParseColor(s.DeskLyricColor, Colors.White);
        var dim = Color.FromArgb((byte)(fg.A * 0.4), fg.R, fg.G, fg.B);
        var ff = new FontFamily(s.DeskLyricFont);
        double size = Math.Clamp(s.DeskLyricFontSize, 14, 72);
        bool sweep = s.LyricHighlight is "Sweep" or "SweepGrow";

        LineNow.FontFamily = ff; LineNow.FontSize = size;
        // 卡拉OK模式：底色行淡色，强调色覆盖层按进度裁剪
        LineNow.Foreground = new SolidColorBrush(sweep ? Color.FromArgb((byte)(fg.A * 0.45), fg.R, fg.G, fg.B) : fg);
        LineNowAccent.FontFamily = ff; LineNowAccent.FontSize = size;
        LineNowAccent.Foreground = new SolidColorBrush(fg);
        if (!sweep) LineNowAccent.Text = "";
        LineNext.FontFamily = ff; LineNext.FontSize = Math.Max(14, size * 0.72);
        LineNext.Foreground = new SolidColorBrush(dim);
        TitleText.FontFamily = ff; TitleText.FontSize = size * 0.8;
        TitleText.Foreground = new SolidColorBrush(fg);
        ArtistText.FontFamily = ff; ArtistText.FontSize = Math.Max(13, size * 0.55);
        ArtistText.Foreground = new SolidColorBrush(dim);
        CloseIcon.Fill = new SolidColorBrush(fg);
    }

    static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    static SolidColorBrush WithAlpha(Color c, double opacity) =>
        new(Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B));

    // ---------- 播放状态联动 ----------

    void OnPlayerStateChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            LoadLyricsForCurrent();
            UpdateContent();
        });

    void OnProgressChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => UpdateLyricLine(_vm.Player.Position));

    void LoadLyricsForCurrent()
    {
        var song = _vm.Player.CurrentSong;
        string id = song?.Id ?? "";
        if (id == _lyricSongId) return;
        _lyricSongId = id;
        _lyrics = [];
        _lyricIndex = -1;
        if (song?.LyricPath == null) return;

        // 后台加载 LRC，seq 守卫避免快速切歌时旧结果覆盖新结果
        int seq = ++_loadSeq;
        string path = song.LyricPath;
        Task.Run(() => LrcService.LoadFromFile(path)).ContinueWith(t =>
        {
            var lines = t.IsCompletedSuccessfully ? t.Result : new List<LyricLine>();
            Dispatcher.BeginInvoke(() =>
            {
                if (seq != _loadSeq) return;
                _lyrics = lines;
                UpdateContent();
            });
        });
    }

    void UpdateContent()
    {
        bool showLyric = _lyrics.Count > 0 && _vm.Player.IsPlaying;
        LyricView.Visibility = showLyric ? Visibility.Visible : Visibility.Collapsed;
        TitleView.Visibility = showLyric ? Visibility.Collapsed : Visibility.Visible;
        if (showLyric) return;

        _lyricIndex = -1; // 恢复播放后重新定位当前行
        var song = _vm.Player.CurrentSong;
        TitleText.Text = song?.DisplayTitle ?? "未在播放";
        ArtistText.Text = song?.Artist ?? "";
    }

    void UpdateLyricLine(double position)
    {
        if (_lyrics.Count == 0 || !_vm.Player.IsPlaying) return;
        int idx = LrcService.FindIndex(_lyrics, TimeSpan.FromSeconds(position));
        if (idx == _lyricIndex) return;
        _lyricIndex = idx;
        if (idx < 0) return;

        LineNow.Text = _lyrics[idx].Text;
        LineNowAccent.Text = SettingsService.Current.LyricHighlight is "Sweep" or "SweepGrow"
            ? _lyrics[idx].Text : "";
        LineNext.Text = idx + 1 < _lyrics.Count ? _lyrics[idx + 1].Text : "";
        // 切行淡入
        LineNow.BeginAnimation(TextBlock.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        LineNowAccent.BeginAnimation(TextBlock.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
        LineNext.BeginAnimation(TextBlock.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
    }

    // ---------- 卡拉OK逐字变色（按行内进度裁剪强调层） ----------

    RectangleGeometry? _lineClip;

    void OnSweepRender(object? sender, EventArgs e) => UpdateSweep();

    void UpdateSweep()
    {
        bool sweep = SettingsService.Current.LyricHighlight is "Sweep" or "SweepGrow";
        if (!sweep || !IsVisible || !_vm.Player.IsPlaying || _lyrics.Count == 0 || _lyricIndex < 0)
        {
            if (_lineClip != null)
            {
                LineNowAccent.Clip = null;
                _lineClip = null;
            }
            return;
        }
        double start = _lyrics[_lyricIndex].Time.TotalSeconds;
        double end = _lyricIndex + 1 < _lyrics.Count
            ? _lyrics[_lyricIndex + 1].Time.TotalSeconds
            : _vm.Player.Duration;
        double p = end <= start + 0.05 ? 1
            : Math.Clamp((_vm.Player.Position - start) / (end - start), 0, 1);
        _lineClip ??= new RectangleGeometry();
        LineNowAccent.Clip = _lineClip;
        _lineClip.Rect = new Rect(0, 0,
            LineNowAccent.ActualWidth * p, Math.Max(1, LineNowAccent.ActualHeight));
    }

    // ---------- 窗口交互 ----------

    void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ShowPlayerOverlay();
                mw.Show();
                if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
                mw.Activate();
            }
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // 悬停时淡入磨砂背景与缩放手柄，方便定位控件范围与调整大小
    void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        CloseBtn.Visibility = Visibility.Visible;
        Fade(FrostBg, 1);
        Fade(ResizeGrip, 1);
    }

    void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_resizing) return; // 拖拽缩放手柄移出边界时不隐藏提示层
        CloseBtn.Visibility = Visibility.Hidden;
        Fade(FrostBg, 0);
        Fade(ResizeGrip, 0);
    }

    static void Fade(UIElement target, double to)
    {
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(
            to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    // ---------- 右下角拖拽调整大小 ----------

    bool _resizing;
    Point _gripStart;
    double _startW, _startH;

    void ResizeGrip_Down(object sender, MouseButtonEventArgs e)
    {
        _resizing = true;
        _gripStart = PointToScreen(e.GetPosition(this));
        _startW = Width;
        _startH = Height;
        ResizeGrip.CaptureMouse();
        e.Handled = true; // 阻止冒泡到 Root 触发窗口拖动
    }

    void ResizeGrip_Move(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var p = PointToScreen(e.GetPosition(this));
        Width = Math.Max(320, _startW + p.X - _gripStart.X);
        Height = Math.Max(84, _startH + p.Y - _gripStart.Y);
        e.Handled = true;
    }

    void ResizeGrip_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ResizeGrip.ReleaseMouseCapture();
        e.Handled = true;
    }

    void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 主窗口 ViewModel：导航、播放条状态、全局命令
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
    public PlaybackService Player { get; } = new();

    public ObservableCollection<SongInfo> Songs => MusicLibraryService.Songs;
    public ObservableCollection<Playlist> Playlists => PlaylistService.Playlists;

    public HomeViewModel Home { get; }
    public LibraryViewModel Library { get; }
    public PlaylistViewModel PlaylistVm { get; }
    public NowPlayingViewModel NowPlaying { get; }
    public SettingsViewModel SettingsVm { get; }

    /// <summary>侧边导航项（Key 即视图名）</summary>
    public IReadOnlyList<OptionItem> NavItems { get; } =
    [
        new("Home", "首页"),
        new("Library", "音乐库"),
        new("Playlist", "歌单"),
        new("Settings", "设置")
    ];

    public MainViewModel()
    {
        Home = new HomeViewModel(this);
        Library = new LibraryViewModel(this);
        PlaylistVm = new PlaylistViewModel(this);
        NowPlaying = new NowPlayingViewModel(this);
        SettingsVm = new SettingsViewModel(this);

        Player.Mode = SettingsService.Current.PlayMode;
        Player.Volume = SettingsService.Current.Volume;

        Player.StateChanged += (_, _) => RaisePlayerState();
        Player.ProgressChanged += (_, _) => RaiseProgress();
        // 标签编辑/重新同步后，替换播放队列中的歌曲引用；
        // 若更新的是当前播放歌曲，同步刷新播放页封面/氛围背景/歌词
        MusicLibraryService.SongUpdated += (_, song) =>
        {
            Player.UpdateSong(song);
            if (Player.CurrentSong?.Id == song.Id)
            {
                NowPlaying.OnPlayerStateChanged();
                MyQQMusic.Views.DeskLyricWindow.InvalidateLyrics();
            }
        };
        PlaylistService.PlaylistsChanged += (_, _) => Raise(nameof(IsCurrentFavorite));
        MusicLibraryService.ScanCompleted += (_, _) =>
        {
            Raise(nameof(SongCount));
            Home.Refresh();
        };

        NavigateCommand = new(view => { if (!string.IsNullOrWhiteSpace(view)) Navigate(view); });
        GoBackCommand = new(() =>
        {
            if (_back.Count == 0) return;
            _forward.Push(_currentView);
            SetCurrent(_back.Pop());
        });
        GoForwardCommand = new(() =>
        {
            if (_forward.Count == 0) return;
            _back.Push(_currentView);
            SetCurrent(_forward.Pop());
        });
        SearchCommand = new(() => Navigate("Library"));
        TogglePlayCommand = new(() => Player.TogglePlay(), () => Player.CurrentSong != null);
        NextCommand = new(() => Player.Next());
        PreviousCommand = new(() => Player.Previous());
        ToggleFavoriteCommand = new(() =>
        {
            if (Player.CurrentSong != null)
                PlaylistService.ToggleFavorite(Player.CurrentSong.Id);
        }, () => Player.CurrentSong != null);
        OpenFolderCommand =new (path =>
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe",
                    $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch { }
        });
        AddToPlaylistCommand = new(args =>
        {
            if (args is { Length: 2 } &&
                args[0] is SongInfo song && args[1] is Playlist playlist)
            {
                PlaylistService.AddSong(playlist.Id, song.Id);
            }
        });
        PlaySongDispatchCommand = new(song =>
        {
            if (song != null) PlaySongFromCurrentView(song);
        });

        // 首次打开即填充首页推荐：缓存命中时不会触发 ScanCompleted，需主动刷新一次
        Home.Refresh();

        // 播放页切换模式/皮肤时，播放条封面样式同步更新
        SettingsService.AppearanceChanged += OnAppearanceChanged;

        // 播放页背景变化时，播放条背景同步
        NowPlaying.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NowPlayingViewModel.PageBackground))
                Raise(nameof(BarBackground));
        };
    }

    void OnAppearanceChanged() => Raise(nameof(BarBackground));

    // ---------- 导航 ----------
    string _currentView = "Home";
    public string CurrentView => _currentView;

    /// <summary>侧边导航 SelectedValue 双向绑定入口</summary>
    public string SelectedView
    {
        get => _currentView;
        set { if (!string.IsNullOrWhiteSpace(value)) Navigate(value); }
    }

    /// <summary>当前页面对应的子 ViewModel（ContentControl 按类型映射视图）</summary>
    public object CurrentPage => _currentView switch
    {
        "Library" => Library,
        "Playlist" => PlaylistVm,
        "NowPlaying" => NowPlaying,
        "Settings" => SettingsVm,
        _ => Home
    };

    readonly Stack<string> _back = new();
    readonly Stack<string> _forward = new();

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public RelayCommand<string> NavigateCommand { get; }
    public RelayCommand GoBackCommand { get; }
    public RelayCommand GoForwardCommand { get; }

    public void Navigate(string view)
    {
        if (_currentView == view) return;
        _back.Push(_currentView);
        _forward.Clear();
        SetCurrent(view);
    }

    void SetCurrent(string view)
    {
        _currentView = view;
        Raise(nameof(CurrentView));
        Raise(nameof(SelectedView));
        Raise(nameof(CurrentPage));
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));

        switch (view)
        {
            case "Home": Home.Refresh(); break;
            case "Library": Library.Refresh(); break;
            case "NowPlaying": NowPlaying.OnPlayerStateChanged(); break;
        }
    }

    // ---------- 搜索 ----------
    string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                Library.Filter = value;
        }
    }

    public RelayCommand SearchCommand { get; }

    // ---------- 播放条状态 ----------
    public SongInfo? CurrentSong => Player.CurrentSong;
    public bool IsPlaying => Player.IsPlaying;

    bool _overlayOpen;

    /// <summary>播放页浮层是否打开（打开时播放条背景与播放页一致）</summary>
    public bool OverlayOpen
    {
        get => _overlayOpen;
        set
        {
            if (_overlayOpen == value) return;
            _overlayOpen = value;
            Raise(nameof(BarBackground));
        }
    }

    /// <summary>播放条背景：播放页打开时与播放页背景完全一致（文字颜色由主窗口同步皮肤配色板保证可读），否则跟随主题</summary>
    public Brush BarBackground
    {
        get
        {
            if (_overlayOpen)
            {
                return NowPlaying.PageBackground is { } bg
                    ? bg
                    : (Brush)Application.Current.FindResource("AppBackgroundBrush");
            }
            return (Brush)Application.Current.FindResource("PlayerBarBrush");
        }
    }

    public bool HasCurrentSong => Player.CurrentSong != null;
    public double Position => Player.Position;
    public double Duration => Player.Duration;
    public string PositionText => FormatTime(Player.Position);
    public string DurationText => FormatTime(Player.Duration);
    public int SongCount => Songs.Count;

    public double Volume
    {
        get => Player.Volume;
        set
        {
            Player.Volume = value;
            Raise();
            Raise(nameof(IsMuted));
        }
    }

    public bool IsMuted => Player.Volume <= 0.001;

    public PlayMode Mode
    {
        get => Player.Mode;
        set
        {
            if (Player.Mode == value)
            {
                // 点击循环按钮时轮换到下一模式
                value = (PlayMode)(((int)value + 1) % 4);
            }
            Player.Mode = value;
            SettingsService.Current.PlayMode = value;
            SettingsService.Save();
            Raise();
            Raise(nameof(ModeName));
        }
    }

    public string ModeName => Mode switch
    {
        PlayMode.Sequential => "顺序播放",
        PlayMode.SingleLoop => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "列表循环"
    };

    public bool IsCurrentFavorite =>
        Player.CurrentSong != null && PlaylistService.IsFavorite(Player.CurrentSong.Id);

    /// <summary>当前播放队列（播放条右侧列表面板展示，不分页）</summary>
    public IReadOnlyList<SongInfo>? PlayQueue => Player.Queue;

    /// <summary>播放队列中的指定位置</summary>
    public void PlayQueueAt(int index)
    {
        var queue = Player.Queue;
        if (queue != null && index >= 0 && index < queue.Count)
            Player.PlayFrom(queue, index);
    }

    /// <summary>播放队列中的指定歌曲</summary>
    public void PlayQueueSong(SongInfo song)
    {
        var queue = Player.Queue;
        if (queue == null) return;
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i] == song) { Player.PlayFrom(queue, i); return; }
        }
    }

    bool _draggingProgress;
    public void BeginProgressDrag() => _draggingProgress = true;

    public void EndProgressDrag(double value)
    {
        _draggingProgress = false;
        Player.Seek(value);
    }

    void RaisePlayerState()
    {
        Raise(nameof(CurrentSong));
        Raise(nameof(IsPlaying));
        Raise(nameof(HasCurrentSong));
        Raise(nameof(Duration));
        Raise(nameof(DurationText));
        Raise(nameof(IsCurrentFavorite));
        Raise(nameof(PlayQueue));
        NowPlaying.OnPlayerStateChanged();
    }

    void RaiseProgress()
    {
        if (_draggingProgress) return;
        Raise(nameof(Position));
        Raise(nameof(PositionText));
        NowPlaying.OnProgress();
    }

    public RelayCommand TogglePlayCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand<string> OpenFolderCommand { get; }
    public RelayCommand<object[]> AddToPlaylistCommand { get; }
    public RelayCommand<SongInfo> PlaySongDispatchCommand { get; }

    /// <summary>按当前页面分发播放请求（共享歌曲行模板使用）</summary>
    public void PlaySongFromCurrentView(SongInfo song)
    {
        switch (_currentView)
        {
            case "Home":
                if (Home.Recommendations.Contains(song)) PlayFromList(Home.Recommendations, song);
                else if (Home.HotSongs.Contains(song)) PlayFromList(Home.HotSongs, song);
                else PlayFromList(Songs, song);
                break;
            case "Library":
                Library.PlaySongCommand.Execute(song);
                break;
            case "Playlist":
                PlaylistVm.PlaySongCommand.Execute(song);
                break;
            default:
                PlayFromList(Songs, song);
                break;
        }
    }

    /// <summary>切换指定歌曲的收藏状态</summary>
    public void ToggleFavoriteFor(SongInfo song) => PlaylistService.ToggleFavorite(song.Id);

    /// <summary>从某个列表播放指定歌曲（作为播放队列）</summary>
    public void PlayFromList(IReadOnlyList<SongInfo> list, SongInfo song)
    {
        int index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == song) { index = i; break; }
        }
        if (index >= 0) Player.PlayFrom(list, index);
    }

    public void PlayList(IReadOnlyList<SongInfo> list)
    {
        if (list.Count > 0) Player.PlayFrom(list, 0);
    }

    static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
    }

    public void Dispose()
    {
        SettingsService.AppearanceChanged -= OnAppearanceChanged;
        Player.Dispose();
    }
}

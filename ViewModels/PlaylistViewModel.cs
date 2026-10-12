using System.Collections.ObjectModel;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 歌单页：歌单管理与歌单内歌曲
/// </summary>
public class PlaylistViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    List<SongInfo> _songs = new();

    public PlaylistViewModel(MainViewModel main)
    {
        _main = main;

        CreatePlaylistCommand = new(() =>
        {
            if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;
            var playlist = PlaylistService.Create(NewPlaylistName);
            NewPlaylistName = "";
            SelectedPlaylist = playlist;
        }, () => !string.IsNullOrWhiteSpace(NewPlaylistName));

        DeletePlaylistCommand = new(playlist =>
        {
            if (playlist == null || playlist.IsSystem) return;
            if (SelectedPlaylist == playlist) SelectedPlaylist = null;
            PlaylistService.Delete(playlist.Id);
        });

        PlaySongCommand = new(song =>
        {
            if (song != null) _main.PlayFromList(SelectedSongs, song);
        });

        PlayAllCommand = new(() => _main.PlayList(SelectedSongs));

        // 循环/随机播放当前歌单：先切换播放模式，再从第一首开始播放
        PlayLoopCommand = new(() =>
        {
            if (_songs.Count == 0) return;
            _main.Player.Mode = PlayMode.ListLoop;
            _main.PlayList(SelectedSongs);
        });

        PlayShuffleCommand = new(() =>
        {
            if (_songs.Count == 0) return;
            _main.Player.Mode = PlayMode.Shuffle;
            _main.PlayList(SelectedSongs);
        });

        RemoveSongCommand = new(song =>
        {
            if (song != null && SelectedPlaylist != null)
                PlaylistService.RemoveSong(SelectedPlaylist.Id, song.Id);
        });

        PrevPageCommand = new(() =>
        {
            if (_pageIndex <= 0) return;
            _pageIndex--;
            RebuildPage();
        });

        NextPageCommand = new(() =>
        {
            if (_pageIndex >= PageCount - 1) return;
            _pageIndex++;
            RebuildPage();
        });

        PlaylistService.PlaylistsChanged += (_, _) =>
        {
            RefreshSelected();
            DeletePlaylistCommand.RaiseCanExecuteChanged();
            CreatePlaylistCommand.RaiseCanExecuteChanged();
        };
    }

    public ObservableCollection<Playlist> Playlists => PlaylistService.Playlists;

    Playlist? _selected;
    public Playlist? SelectedPlaylist
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                RefreshSelected();
        }
    }

    public IReadOnlyList<SongInfo> SelectedSongs => _songs;
    public bool HasSelection => _selected != null;
    public bool HasSongs => _songs.Count > 0;

    // ---------- 分页：每页 100 条 ----------
    public const int PageSize = 100;

    readonly ObservableCollection<SongInfo> _paged = new();

    /// <summary>当前页歌曲（列表实际绑定源）</summary>
    public IReadOnlyCollection<SongInfo> PagedSongs => _paged;

    int _pageIndex;
    public int PageCount { get; private set; } = 1;

    /// <summary>仅多页时显示分页条</summary>
    public bool HasPagination => PageCount > 1;
    public bool HasPreviousPage => _pageIndex > 0;
    public bool HasNextPage => _pageIndex < PageCount - 1;
    public string PageInfoText => $"第 {_pageIndex + 1} / {PageCount} 页";

    /// <summary>按当前歌单歌曲重建当前页内容；resetPage 时回到第一页</summary>
    void RebuildPage(bool resetPage = false)
    {
        if (resetPage) _pageIndex = 0;
        PageCount = Math.Max(1, (_songs.Count + PageSize - 1) / PageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, PageCount - 1);

        _paged.Clear();
        int start = _pageIndex * PageSize;
        int end = Math.Min(start + PageSize, _songs.Count);
        for (int i = start; i < end; i++) _paged.Add(_songs[i]);

        Raise(nameof(HasPagination));
        Raise(nameof(HasPreviousPage));
        Raise(nameof(HasNextPage));
        Raise(nameof(PageInfoText));
    }

    void RefreshSelected()
    {
        _songs = _selected != null
            ? PlaylistService.ResolveSongs(_selected)
            : new List<SongInfo>();
        Raise(nameof(SelectedSongs));
        Raise(nameof(HasSongs));
        Raise(nameof(HasSelection));
        Raise(nameof(SelectedPlaylist));
        RebuildPage(resetPage: true);
    }

    string _newPlaylistName = "";
    public string NewPlaylistName
    {
        get => _newPlaylistName;
        set
        {
            if (Set(ref _newPlaylistName, value))
            {
                CreatePlaylistCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand CreatePlaylistCommand { get; }
    public RelayCommand<Playlist> DeletePlaylistCommand { get; }
    public RelayCommand<SongInfo> PlaySongCommand { get; }
    public RelayCommand PlayAllCommand { get; }
    public RelayCommand PlayLoopCommand { get; }
    public RelayCommand PlayShuffleCommand { get; }
    public RelayCommand<SongInfo> RemoveSongCommand { get; }
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
}

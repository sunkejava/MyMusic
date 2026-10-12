using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 音乐库：全量歌曲列表，支持搜索过滤与排序
/// </summary>
public class LibraryViewModel : ViewModelBase
{
    readonly MainViewModel _main;
    readonly ICollectionView _view;

    public LibraryViewModel(MainViewModel main)
    {
        _main = main;
        _view = CollectionViewSource.GetDefaultView(MusicLibraryService.Songs);
        _view.Filter = obj =>
        {
            if (obj is not SongInfo song) return false;
            if (string.IsNullOrWhiteSpace(_filter)) return true;
            var f = _filter.Trim();
            return song.DisplayTitle.Contains(f, StringComparison.OrdinalIgnoreCase)
                || song.Artist.Contains(f, StringComparison.OrdinalIgnoreCase)
                || song.Album.Contains(f, StringComparison.OrdinalIgnoreCase);
        };

        MusicLibraryService.ScanCompleted += (_, _) =>
        {
            _view.Refresh();
            RebuildPage(resetPage: true);
            Raise(nameof(StatusText));
        };

        MusicLibraryService.ScanProgressChanged += (_, _) => Raise(nameof(StatusText));

        SetSortCommand = new(key => { if (!string.IsNullOrEmpty(key)) SortKey = key; });
        PlaySongCommand = new(song =>
        {
            if (song != null) _main.PlayFromList(VisibleSongs, song);
        });
        PlayAllCommand = new(() => _main.PlayList(VisibleSongs));
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

        RebuildPage(resetPage: true);
    }

    public ICollectionView Songs => _view;

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

    /// <summary>按当前过滤/排序结果重建当前页内容；resetPage 时回到第一页</summary>
    void RebuildPage(bool resetPage = false)
    {
        var all = VisibleSongs;
        if (resetPage) _pageIndex = 0;
        PageCount = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        _pageIndex = Math.Clamp(_pageIndex, 0, PageCount - 1);

        _paged.Clear();
        int start = _pageIndex * PageSize;
        int end = Math.Min(start + PageSize, all.Count);
        for (int i = start; i < end; i++) _paged.Add(all[i]);

        Raise(nameof(HasPagination));
        Raise(nameof(HasPreviousPage));
        Raise(nameof(HasNextPage));
        Raise(nameof(PageInfoText));
    }

    string _filter = "";
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                _view.Refresh();
                RebuildPage(resetPage: true);
                Raise(nameof(StatusText));
            }
        }
    }

    string _sortKey = "Title";
    public string SortKey
    {
        get => _sortKey;
        set
        {
            if (!Set(ref _sortKey, value)) return;
            ApplySort();
            Raise(nameof(SortTitleActive));
            Raise(nameof(SortArtistActive));
            Raise(nameof(SortAlbumActive));
            Raise(nameof(SortTimeActive));
        }
    }

    public bool SortTitleActive => _sortKey == "Title";
    public bool SortArtistActive => _sortKey == "Artist";
    public bool SortAlbumActive => _sortKey == "Album";
    public bool SortTimeActive => _sortKey == "Duration";

    void ApplySort()
    {
        _view.SortDescriptions.Clear();
        var direction = ListSortDirection.Ascending;
        switch (_sortKey)
        {
            case "Artist":
                _view.SortDescriptions.Add(new SortDescription(nameof(SongInfo.Artist), direction));
                break;
            case "Album":
                _view.SortDescriptions.Add(new SortDescription(nameof(SongInfo.Album), direction));
                break;
            case "Duration":
                _view.SortDescriptions.Add(new SortDescription(nameof(SongInfo.Duration), direction));
                break;
            default:
                _view.SortDescriptions.Add(new SortDescription(nameof(SongInfo.DisplayTitle), direction));
                break;
        }
        _view.SortDescriptions.Add(new SortDescription(nameof(SongInfo.DisplayTitle), direction));
        _view.Refresh();
        RebuildPage(resetPage: true);
    }

    public List<SongInfo> VisibleSongs
    {
        get
        {
            var list = new List<SongInfo>();
            foreach (var obj in _view)
            {
                if (obj is SongInfo song) list.Add(song);
            }
            return list;
        }
    }

    public string StatusText
    {
        get
        {
            if (MusicLibraryService.IsScanning) return MusicLibraryService.ScanStatusText;
            int total = MusicLibraryService.Songs.Count;
            if (!string.IsNullOrWhiteSpace(_filter))
            {
                int matched = VisibleSongs.Count;
                return $"找到 {matched} 首结果";
            }
            return $"共 {total} 首";
        }
    }

    public void Refresh()
    {
        _view.Refresh();
        RebuildPage();
        Raise(nameof(StatusText));
    }

    public RelayCommand<string> SetSortCommand { get; }
    public RelayCommand<SongInfo> PlaySongCommand { get; }
    public RelayCommand PlayAllCommand { get; }
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
}

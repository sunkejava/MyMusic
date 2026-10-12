using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 首页：推荐歌曲与热门歌曲
/// </summary>
public class HomeViewModel : ViewModelBase
{
    readonly MainViewModel _main;

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        PlayRecommendCommand = new(song =>
        {
            if (song != null) _main.PlayFromList(Recommendations, song);
        });
        PlayHotCommand = new(song =>
        {
            if (song != null) _main.PlayFromList(HotSongs, song);
        });
        PlayHotAllCommand = new(() => _main.PlayList(HotSongs));
        ShuffleAllCommand = new(() =>
        {
            var all = main.Songs.ToList();
            if (all.Count == 0) return;
            var rnd = new Random();
            main.PlayList(all.OrderBy(_ => rnd.Next()).ToList());
        });
    }

    public List<SongInfo> Recommendations { get; private set; } = new();
    public List<SongInfo> HotSongs { get; private set; } = new();
    public bool HasSongs => MusicLibraryService.Songs.Count > 0;

    public void Refresh()
    {
        var all = MusicLibraryService.Songs;
        var history = PlayHistoryService.Snapshot();

        // 热门歌曲：按播放次数与最近播放时间排序；无历史时回退为最近加入的歌曲
        HotSongs = history.Count > 0
            ? [.. all
                .OrderByDescending(s => history.TryGetValue(s.Id, out var h) ? h.Count : 0)
                .ThenByDescending(s => history.TryGetValue(s.Id, out var h) ? h.Last : DateTime.MinValue)
                .Take(12)]
            : [.. all.OrderByDescending(s => s.DateAdded).Take(12)];

        Recommendations = BuildRecommendations(all, history, _main.Player.CurrentSong?.Id);
        Raise(nameof(Recommendations));
        Raise(nameof(HotSongs));
        Raise(nameof(HasSongs));
    }

    /// <summary>
    /// 每日推荐：播放次数 + 常听歌手偏好 + 时间衰减加权，近 3 天听过的降权、
    /// 带随机噪声保证新鲜感；没有任何历史时等价于随机推荐。
    /// </summary>
    static List<SongInfo> BuildRecommendations(IReadOnlyList<SongInfo> all,
        Dictionary<string, PlayHistoryService.PlayStat> history, string? currentId)
    {
        var artistScore = new Dictionary<string, double>();
        int maxCount = 1;
        foreach (var s in all)
        {
            if (!history.TryGetValue(s.Id, out var h)) continue;
            maxCount = Math.Max(maxCount, h.Count);
            artistScore[s.Artist] = artistScore.GetValueOrDefault(s.Artist) + h.Count * 2.0;
        }
        double maxArtist = artistScore.Count == 0 ? 1 : artistScore.Values.Max();

        var rnd = new Random();
        return [.. all
            .Where(s => s.Id != currentId) // 正在播放的歌不再推荐
            .Select(s =>
            {
                double score = rnd.NextDouble() * 0.25; // 随机噪声：保证每次打开内容有新意
                if (history.TryGetValue(s.Id, out var h))
                {
                    score += 0.45 * h.Count / maxCount;
                    double days = (DateTime.Now - h.Last).TotalDays;
                    // 近 3 天听过降权；久远播放按指数衰减给回赏
                    score += days < 3 ? -0.35 : 0.15 * Math.Exp(-days / 30.0);
                }
                if (artistScore.TryGetValue(s.Artist, out var av))
                    score += 0.35 * av / maxArtist; // 常听歌手的其他歌优先
                return (Song: s, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Take(8)
            .Select(x => x.Song)];
    }

    public RelayCommand<SongInfo> PlayRecommendCommand { get; }
    public RelayCommand<SongInfo> PlayHotCommand { get; }
    public RelayCommand PlayHotAllCommand { get; }
    public RelayCommand ShuffleAllCommand { get; }
}

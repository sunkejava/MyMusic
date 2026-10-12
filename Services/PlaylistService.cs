using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using MyQQMusic.Models;

namespace MyQQMusic.Services;

/// <summary>
/// 歌单管理（持久化到 playlists.json），内置"我喜欢"系统歌单
/// </summary>
public static class PlaylistService
{
    public const string FavoritesId = "favorites";
    public const string FavoritesName = "我喜欢";

    public static ObservableCollection<Playlist> Playlists { get; } = new();
    public static event EventHandler? PlaylistsChanged;

    public static void Load()
    {
        List<Playlist>? list = null;
        try
        {
            if (File.Exists(SettingsService.PlaylistsFilePath))
            {
                var json = File.ReadAllText(SettingsService.PlaylistsFilePath);
                list = JsonSerializer.Deserialize<List<Playlist>>(json);
            }
        }
        catch
        {
            list = null;
        }

        Playlists.Clear();
        if (list != null)
        {
            foreach (var pl in list) Playlists.Add(pl);
        }

        if (Playlists.All(p => p.Id != FavoritesId))
        {
            Playlists.Insert(0, new Playlist { Id = FavoritesId, Name = FavoritesName, IsSystem = true });
        }
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Playlists.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsService.PlaylistsFilePath, json);
        }
        catch
        {
            // 保存失败不影响运行
        }
    }

    public static Playlist Create(string name)
    {
        var pl = new Playlist { Name = string.IsNullOrWhiteSpace(name) ? "新建歌单" : name.Trim() };
        Playlists.Add(pl);
        Save();
        PlaylistsChanged?.Invoke(null, EventArgs.Empty);
        return pl;
    }

    public static void Delete(string id)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == id);
        if (pl == null || pl.IsSystem) return;
        Playlists.Remove(pl);
        Save();
        PlaylistsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void AddSong(string playlistId, string songPath)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (pl == null || pl.SongPaths.Contains(songPath)) return;
        pl.SongPaths.Add(songPath);
        pl.RaiseCount();
        Save();
        PlaylistsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void RemoveSong(string playlistId, string songPath)
    {
        var pl = Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (pl == null) return;
        if (pl.SongPaths.Remove(songPath))
        {
            pl.RaiseCount();
            Save();
            PlaylistsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static Playlist Favorites =>
        Playlists.First(p => p.Id == FavoritesId);

    public static bool IsFavorite(string path) => Favorites.SongPaths.Contains(path);

    public static void ToggleFavorite(string path)
    {
        if (Favorites.SongPaths.Contains(path))
            Favorites.SongPaths.Remove(path);
        else
            Favorites.SongPaths.Insert(0, path);
        Favorites.RaiseCount();
        Save();
        PlaylistsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// 扫描完成后按歌曲所在目录名自动创建/补全歌单：
    /// 同名歌单（不区分大小写）存在则仅补充缺失的歌曲，否则新建；根目录直属歌曲归入以根目录命名的歌单。
    /// </summary>
    public static void SyncFolderPlaylists(IEnumerable<SongInfo> songs)
    {
        // 按目录名分组收集歌曲路径
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var song in songs)
        {
            var dir = Path.GetDirectoryName(song.FilePath);
            if (string.IsNullOrEmpty(dir)) continue;
            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) continue; // 磁盘根目录（如 C:\）无名称，跳过
            if (!groups.TryGetValue(name, out var paths))
                groups[name] = paths = new List<string>();
            paths.Add(song.FilePath);
        }
        if (groups.Count == 0) return;

        bool changed = false;
        foreach (var (name, paths) in groups)
        {
            var pl = Playlists.FirstOrDefault(p =>
                !p.IsSystem && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (pl == null)
            {
                pl = new Playlist { Name = name };
                Playlists.Add(pl);
            }
            foreach (var path in paths)
            {
                if (pl.SongPaths.Contains(path)) continue;
                pl.SongPaths.Add(path);
                pl.RaiseCount();
                changed = true;
            }
        }

        if (changed)
        {
            Save();
            PlaylistsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>把歌单中的路径解析为当前曲库中的歌曲（缺失的跳过）</summary>
    public static List<SongInfo> ResolveSongs(Playlist playlist)
    {
        var result = new List<SongInfo>();
        foreach (var path in playlist.SongPaths)
        {
            var song = MusicLibraryService.Songs.FirstOrDefault(s => s.Id == path);
            if (song != null) result.Add(song);
        }
        return result;
    }
}

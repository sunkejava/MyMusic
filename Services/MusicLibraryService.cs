using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyQQMusic.Models;

namespace MyQQMusic.Services;

/// <summary>
/// 本地曲库：扫描目录并用 TagLib 解析元数据
/// </summary>
public static class MusicLibraryService
{
    public static ObservableCollection<SongInfo> Songs { get; } = new();
    public static bool IsScanning { get; private set; }
    public static event EventHandler? ScanCompleted;

    /// <summary>某首歌的元数据被更新（标签编辑/重新同步）后触发，供播放队列等替换引用</summary>
    public static event EventHandler<SongInfo>? SongUpdated;

    /// <summary>扫描进度（0-1），仅在扫描中有效</summary>
    public static double ScanProgress { get; private set; }
    /// <summary>扫描状态文本，如"正在扫描 12/340 · 歌曲.mp3"</summary>
    public static string ScanStatusText { get; private set; } = "";
    public static event EventHandler? ScanProgressChanged;

    static readonly string[] Supported = { ".mp3", ".flac", ".m4a", ".wav", ".wma", ".aac", ".ogg" };

    // ---------- 曲库本地缓存：扫描一次即持久化，重启直接加载，避免每次重新扫描（网络目录尤其慢） ----------

    static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyQQMusic");
    static readonly string LibraryFile = Path.Combine(CacheDir, "library.json");
    static readonly string CoverDir = Path.Combine(CacheDir, "covers");

    /// <summary>文件指纹索引：路径 → (LastWriteTimeUtc Ticks, 长度)，用于启动时的增量比对</summary>
    static readonly Dictionary<string, (long Ticks, long Len)> _fileIndex = new(StringComparer.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly object SaveLock = new();

    sealed class CacheEntry
    {
        public string P { get; set; } = "";   // 文件路径
        public string T { get; set; } = "";   // 标题
        public string A { get; set; } = "";   // 歌手
        public string AL { get; set; } = "";  // 专辑
        public int TK { get; set; }           // 音轨号
        public double D { get; set; }         // 时长（秒）
        public long WT { get; set; }          // LastWriteTimeUtc Ticks
        public long LN { get; set; }          // 文件长度
    }

    sealed class CacheRoot
    {
        public int V { get; set; } = 1;
        public string Dir { get; set; } = "";
        public List<CacheEntry> Entries { get; set; } = new();
    }

    /// <summary>封面字节按路径哈希存为本地文件，避免把大体积图片塞进 JSON</summary>
    static string CoverPathFor(string filePath)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(filePath)))
            .ToLowerInvariant();
        return Path.Combine(CoverDir, hash + ".img");
    }

    /// <summary>
    /// 把封面写入本地缓存：统一转码为 640px JPEG（列表缩略图与播放页封面均够用），
    /// 缓存体积可比原始字节缩小数倍；解码失败时仅对小图按原样缓存。
    /// </summary>
    static void WriteCover(SongInfo song)
    {
        if (song.CoverData is not { Length: > 0 } data) return;
        byte[]? toStore = null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 640;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            toStore = ms.ToArray();
        }
        catch
        {
            if (data.Length <= 256 * 1024) toStore = data;
        }
        if (toStore == null) return;
        try
        {
            Directory.CreateDirectory(CoverDir);
            File.WriteAllBytes(CoverPathFor(song.FilePath), toStore);
        }
        catch { }
    }

    static void IndexSet(string path, long ticks, long len)
    {
        lock (_fileIndex) _fileIndex[path] = (ticks, len);
    }

    // 当前曲库对应的音乐目录（写缓存与删除维护用）
    static string? _currentDirectory;

    /// <summary>枚举目录内全部受支持音频文件及其指纹（后台线程调用）</summary>
    static Dictionary<string, (long Ticks, long Len)> EnumerateFilesWithStats(string directory)
    {
        var current = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (Array.IndexOf(Supported, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
            long wt = 0, ln = 0;
            try
            {
                var fi = new FileInfo(f);
                wt = fi.LastWriteTimeUtc.Ticks;
                ln = fi.Length;
            }
            catch { }
            current[f] = (wt, ln);
        }
        return current;
    }

    static void SortByTitle(List<SongInfo> list) =>
        list.Sort((a, b) => string.Compare(a.DisplayTitle, b.DisplayTitle,
            StringComparison.OrdinalIgnoreCase));

    public static async Task ScanAsync(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        _currentDirectory = directory;
        IsScanning = true;
        SetProgress(0, 0, null);

        // Progress<T> 在 UI 线程创建，回调自动回到 UI 线程，可安全更新静态状态并触发事件。
        // 注意：Report 是 IProgress<T> 的显式接口实现，需以接口类型持有。
        IProgress<(int done, int total, string? file)> progress =
            new Progress<(int done, int total, string? file)>(p => SetProgress(p.done, p.total, p.file));

        try
        {
            var found = await Task.Run(() =>
            {
                // 第一步：先枚举出全部受支持的音频文件及其指纹，得到总数以便上报进度
                Dictionary<string, (long Ticks, long Len)> current;
                try
                {
                    current = EnumerateFilesWithStats(directory);
                }
                catch
                {
                    return new List<SongInfo>();
                }

                var list = new List<SongInfo>(current.Count);
                progress.Report((0, current.Count, null));

                // 第二步：逐文件解析标签，按节流上报进度（最多每 5 个文件上报一次，最后一个必报）
                int done = 0, lastReport = 0;
                foreach (var file in current.Keys)
                {
                    var song = ParseFile(file);
                    if (song != null)
                    {
                        WriteCover(song); // 封面同步写入本地缓存
                        list.Add(song);
                    }

                    done++;
                    if (done == current.Count || done - lastReport >= 5)
                    {
                        lastReport = done;
                        progress.Report((done, current.Count, Path.GetFileName(file)));
                    }
                }

                SortByTitle(list);

                // 记录文件指纹索引，供下次启动增量比对
                lock (_fileIndex)
                {
                    _fileIndex.Clear();
                    foreach (var (path, stats) in current) _fileIndex[path] = stats;
                }
                return list;
            });

            Songs.Clear();
            foreach (var s in found) Songs.Add(s);

            // 扫描完成后按歌曲所在目录名自动创建/补全对应歌单
            PlaylistService.SyncFolderPlaylists(found);

            // 扫描一次即保存缓存：下次启动直接加载，不再重新扫描
            SaveCacheToDisk(directory);
        }
        finally
        {
            IsScanning = false;
            SetProgress(0, 0, null);
            ScanCompleted?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 尝试从本地缓存加载曲库：目录一致且缓存有效时直接填充曲库并返回 true（启动免扫描的关键路径）。
    /// 必须在 UI 线程、视图绑定之前调用。
    /// </summary>
    public static bool TryLoadFromCache(string? directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
            if (!File.Exists(LibraryFile)) return false;

            var root = JsonSerializer.Deserialize<CacheRoot>(File.ReadAllText(LibraryFile));
            if (root == null || root.V != 1 ||
                !string.Equals(root.Dir, directory, StringComparison.OrdinalIgnoreCase))
                return false;

            var loaded = new List<SongInfo>(root.Entries.Count);
            lock (_fileIndex) _fileIndex.Clear();
            foreach (var e in root.Entries)
            {
                byte[]? cover = null;
                try
                {
                    var cp = CoverPathFor(e.P);
                    if (File.Exists(cp)) cover = File.ReadAllBytes(cp);
                }
                catch { }

                loaded.Add(new SongInfo
                {
                    FilePath = e.P,
                    Title = e.T,
                    Artist = e.A,
                    Album = e.AL,
                    TrackNumber = e.TK,
                    Duration = TimeSpan.FromSeconds(e.D),
                    CoverData = cover,
                });
                IndexSet(e.P, e.WT, e.LN);
            }

            SortByTitle(loaded);
            Songs.Clear();
            foreach (var s in loaded) Songs.Add(s);
            PlaylistService.SyncFolderPlaylists(loaded);
            _currentDirectory = directory;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启动后的后台增量刷新：仅解析新增/变更（按修改时间与长度判断）的文件，
    /// 移除已删除的条目；无变化时不做任何 UI 改动。
    /// 必须在 UI 线程调用（集合变更在 UI 线程应用）。
    /// </summary>
    public static async Task RefreshIfChangedAsync(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        if (IsScanning) return;

        _currentDirectory = directory;

        var diff = await Task.Run(() =>
        {
            Dictionary<string, (long Ticks, long Len)> current;
            try { current = EnumerateFilesWithStats(directory); }
            catch
            {
                // 枚举失败（目录断开等）：按"无变化"处理，保持现状
                return (Parsed: new List<SongInfo>(), Removed: new List<string>(),
                        Current: new Dictionary<string, (long Ticks, long Len)>(StringComparer.OrdinalIgnoreCase));
            }

            List<string> toParse, toRemove;
            lock (_fileIndex)
            {
                toParse = [.. current.Where(kv =>
                    !_fileIndex.TryGetValue(kv.Key, out var old) || old != kv.Value).Select(kv => kv.Key)];
                toRemove = [.. _fileIndex.Keys.Where(k => !current.ContainsKey(k))];
            }

            if (toParse.Count == 0 && toRemove.Count == 0)
                return (Parsed: new List<SongInfo>(), Removed: new List<string>(), Current: current);

            var parsed = new List<SongInfo>();
            foreach (var f in toParse)
            {
                var song = ParseFile(f);
                if (song != null)
                {
                    WriteCover(song);
                    parsed.Add(song);
                }
            }
            foreach (var f in toRemove)
            {
                try
                {
                    var cp = CoverPathFor(f);
                    if (File.Exists(cp)) File.Delete(cp);
                }
                catch { }
            }
            return (Parsed: parsed, Removed: toRemove, Current: current);
        });

        if (diff.Parsed.Count == 0 && diff.Removed.Count == 0) return; // 无变化，保持现状

        // UI 线程应用变更：删除已不存在的歌曲，替换/新增变更的歌曲
        foreach (var path in diff.Removed)
        {
            for (int i = Songs.Count - 1; i >= 0; i--)
            {
                if (Songs[i].FilePath == path) { Songs.RemoveAt(i); break; }
            }
        }
        foreach (var s in diff.Parsed)
        {
            int idx = -1;
            for (int i = 0; i < Songs.Count; i++)
            {
                if (Songs[i].FilePath == s.FilePath) { idx = i; break; }
            }
            if (idx >= 0)
            {
                Songs[idx] = s; // Replace 事件驱动列表行刷新
                SongUpdated?.Invoke(null, s);
            }
            else
            {
                Songs.Add(s);
            }
        }

        lock (_fileIndex)
        {
            foreach (var (path, stats) in diff.Current) _fileIndex[path] = stats;
            foreach (var path in diff.Removed) _fileIndex.Remove(path);
        }

        var all = Songs.ToList();
        SortByTitle(all);
        Songs.Clear();
        foreach (var s in all) Songs.Add(s);

        PlaylistService.SyncFolderPlaylists(all);
        ScanCompleted?.Invoke(null, EventArgs.Empty);
        SaveCacheToDisk(directory);
    }

    /// <summary>把当前曲库快照写入本地缓存（UI 线程取快照，后台线程写盘，不阻塞界面）</summary>
    static void SaveCacheToDisk(string? directory)
    {
        var snapshot = Songs.ToList();
        _ = Task.Run(() =>
        {
            try
            {
                lock (SaveLock)
                {
                    Directory.CreateDirectory(CacheDir);
                    var entries = new List<CacheEntry>(snapshot.Count);
                    foreach (var s in snapshot)
                    {
                        long wt = 0, ln = 0;
                        lock (_fileIndex)
                        {
                            if (_fileIndex.TryGetValue(s.FilePath, out var st))
                            {
                                wt = st.Ticks; ln = st.Len;
                            }
                        }
                        entries.Add(new CacheEntry
                        {
                            P = s.FilePath, T = s.Title, A = s.Artist, AL = s.Album,
                            TK = s.TrackNumber, D = s.Duration.TotalSeconds, WT = wt, LN = ln,
                        });
                    }
                    var root = new CacheRoot { V = 1, Dir = directory ?? "", Entries = entries };
                    File.WriteAllText(LibraryFile, JsonSerializer.Serialize(root, JsonOpts));
                }
            }
            catch { }
        });
    }

    /// <summary>
    /// 文件已从磁盘删除后，从曲库、指纹索引与封面缓存中同步移除并持久化缓存。
    /// 必须在 UI 线程调用（曲库集合变更在 UI 线程）。
    /// </summary>
    public static void RemoveFromLibrary(string path)
    {
        for (int i = Songs.Count - 1; i >= 0; i--)
        {
            if (Songs[i].FilePath == path) { Songs.RemoveAt(i); break; }
        }
        lock (_fileIndex) _fileIndex.Remove(path);
        try { File.Delete(CoverPathFor(path)); } catch { }
        ScanCompleted?.Invoke(null, EventArgs.Empty); // 刷新曲库计数与首页统计
        SaveCacheToDisk(_currentDirectory);
    }

    static void SetProgress(int done, int total, string? currentFile)
    {
        ScanProgress = total > 0 ? (double)done / total : 0;
        ScanStatusText = done > 0 && total > 0
            ? $"正在扫描 {done}/{total} 首" + (string.IsNullOrEmpty(currentFile) ? "" : $" · {currentFile}")
            : total > 0 ? $"正在扫描，共发现 {total} 首音频…" : "正在扫描本地音乐…";
        ScanProgressChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>解析单个文件的元数据（公开：供标签编辑器预览与重新同步使用）</summary>
    public static SongInfo? ParseFile(string file)
    {
        try
        {
            using var tagFile = TagLib.File.Create(file);
            var tag = tagFile.Tag;

            var title = string.IsNullOrWhiteSpace(tag.Title)
                ? Path.GetFileNameWithoutExtension(file) : tag.Title;

            var artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer) ? tag.FirstPerformer
                : tag.Performers.Length > 0 ? string.Join(", ", tag.Performers)
                : "未知歌手";

            var album = string.IsNullOrWhiteSpace(tag.Album) ? "未知专辑" : tag.Album;

            byte[]? cover = null;
            var pic = tag.Pictures.FirstOrDefault();
            if (pic != null && pic.Data.Data.Length > 0)
                cover = pic.Data.Data;

            return new SongInfo
            {
                FilePath = file,
                Title = title,
                Artist = artist,
                Album = album,
                Duration = tagFile.Properties.Duration,
                TrackNumber = (int)tag.Track,
                CoverData = cover
            };
        }
        catch
        {
            // 标签解析失败时退化为文件名
            return new SongInfo
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file)
            };
        }
    }

    /// <summary>
    /// 将编辑后的标签写回音频文件并刷新曲库。
    /// 返回更新后的歌曲；返回 null 表示写入失败（文件被占用或格式不支持写标签）。
    /// </summary>
    public static async Task<SongInfo?> UpdateTagsAsync(SongInfo song, string title,
        string artist, string album, int track)
    {
        var updated = await Task.Run(() =>
        {
            try
            {
                using var tagFile = TagLib.File.Create(song.FilePath);
                tagFile.Tag.Title = title;
                tagFile.Tag.Performers = string.IsNullOrWhiteSpace(artist)
                    ? Array.Empty<string>() : new[] { artist };
                tagFile.Tag.Album = album ?? "";
                tagFile.Tag.Track = (uint)Math.Max(0, track);
                tagFile.Save();
            }
            catch
            {
                return null;
            }
            return ParseFile(song.FilePath);
        });

        if (updated != null)
        {
            ApplyUpdate(song.Id, updated);
            // 标签写盘后文件指纹已变化：更新索引并持久化缓存，避免下次启动误判为外部变更
            try
            {
                var fi = new FileInfo(updated.FilePath);
                IndexSet(updated.FilePath, fi.LastWriteTimeUtc.Ticks, fi.Length);
            }
            catch { }
            SaveCacheToDisk(SettingsService.Current.MusicDirectory);
        }
        return updated;
    }

    /// <summary>重新读取文件最新元信息并同步到曲库（文件被外部修改后使用）</summary>
    public static async Task<SongInfo?> ResyncAsync(SongInfo song)
    {
        var updated = await Task.Run(() =>
        {
            var parsed = ParseFile(song.FilePath);
            if (parsed != null) WriteCover(parsed); // 外部可能改过封面，重新同步时刷新缓存
            return parsed;
        });
        if (updated != null)
        {
            ApplyUpdate(song.Id, updated);
            try
            {
                var fi = new FileInfo(updated.FilePath);
                IndexSet(updated.FilePath, fi.LastWriteTimeUtc.Ticks, fi.Length);
            }
            catch { }
            SaveCacheToDisk(SettingsService.Current.MusicDirectory);
        }
        return updated;
    }

    /// <summary>用新解析结果原位替换曲库条目，并通知订阅方更新引用</summary>
    static void ApplyUpdate(string id, SongInfo updated)
    {
        for (int i = 0; i < Songs.Count; i++)
        {
            if (Songs[i].Id != id) continue;
            Songs[i] = updated; // Replace 事件驱动列表行刷新
            break;
        }
        SongUpdated?.Invoke(null, updated);
    }
}

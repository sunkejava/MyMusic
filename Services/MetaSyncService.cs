using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MyQQMusic.Models;

namespace MyQQMusic.Services;

/// <summary>在线元信息音源</summary>
public enum MetaSource { QQ, Netease, Kugou }

/// <summary>在线搜索候选（Id 为源内标识；Extra/Tags 携带源特有字段）</summary>
public sealed class MetaCandidate
{
    public MetaSource Source { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public int DurationSeconds { get; init; }
    /// <summary>QQ:专辑 mid（封面用）；酷狗:专辑封面直链（可能为空）</summary>
    public string? Extra { get; init; }
    /// <summary>酷狗风格标签（其余源不提供）</summary>
    public string? Tags { get; init; }

    public string DurationText => $"{DurationSeconds / 60:00}:{DurationSeconds % 60:00}";
}

/// <summary>从音源获取的完整元信息（null 字段表示该源未提供，不会写回）</summary>
public sealed class MetaResult
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public byte[]? CoverData { get; init; }
    public string? LyricText { get; init; }

    /// <summary>是否存在可写回的文本元数据（封面/歌词单独可选）</summary>
    public bool HasText => Title != null || Artist != null || Album != null || Genre != null;
}

/// <summary>
/// 在线元信息同步：从 QQ音乐 / 网易云音乐 / 酷狗音乐 搜索并采集元信息，
/// 将歌名、歌手、专辑、风格、封面写回音频文件标签，歌词保存为同名 .lrc，
/// 并重新解析以更新曲库与本地缓存。
/// 三个源均为公开 Web 接口（非官方 API），个别字段可能缺失，缺失字段不写回。
/// </summary>
public static class MetaSyncService
{
    public static readonly (MetaSource Source, string Name)[] Sources =
    [
        (MetaSource.QQ, "QQ音乐"),
        (MetaSource.Netease, "网易云音乐"),
        (MetaSource.Kugou, "酷狗音乐"),
    ];

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return client;
    }

    // ---------- 搜索 ----------

    public static async Task<List<MetaCandidate>> SearchAsync(MetaSource source, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return [];
        return source switch
        {
            MetaSource.QQ => await SearchQQAsync(keyword),
            MetaSource.Netease => await SearchNeteaseAsync(keyword),
            _ => await SearchKugouAsync(keyword),
        };
    }

    static async Task<List<MetaCandidate>> SearchQQAsync(string keyword)
    {
        var url = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?format=json&p=1&n=10&w={Uri.EscapeDataString(keyword)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", "https://y.qq.com/");
        using var doc = await GetJsonAsync(req);

        var list = new List<MetaCandidate>();
        if (doc == null ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("song", out var song) ||
            !song.TryGetProperty("list", out var arr)) return list;

        foreach (var item in arr.EnumerateArray())
        {
            var mid = GetStr(item, "songmid");
            if (mid.Length == 0) continue;
            var artists = item.TryGetProperty("singer", out var singers) && singers.ValueKind == JsonValueKind.Array
                ? string.Join(", ", singers.EnumerateArray().Select(s => GetStr(s, "name")).Where(s => s.Length > 0))
                : "";
            list.Add(new MetaCandidate
            {
                Source = MetaSource.QQ,
                Id = mid,
                Title = GetStr(item, "songname"),
                Artist = artists,
                Album = GetStr(item, "albumname"),
                DurationSeconds = GetInt(item, "interval"),
                Extra = NullIfEmpty(GetStr(item, "albummid")), // 封面 T002 直链需要专辑 mid
            });
        }
        return list;
    }

    static async Task<List<MetaCandidate>> SearchNeteaseAsync(string keyword)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["s"] = keyword, ["type"] = "1", ["total"] = "true", ["limit"] = "10", ["offset"] = "0",
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com/api/search/get")
        {
            Content = content
        };
        req.Headers.TryAddWithoutValidation("Referer", "https://music.163.com/");
        using var doc = await GetJsonAsync(req);

        var list = new List<MetaCandidate>();
        if (doc == null ||
            !doc.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs)) return list;

        foreach (var item in songs.EnumerateArray())
        {
            var id = GetId(item, "id");
            if (id.Length == 0) continue;
            var artists = item.TryGetProperty("artists", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? string.Join(", ", arr.EnumerateArray().Select(a => GetStr(a, "name")).Where(s => s.Length > 0))
                : "";
            var album = item.TryGetProperty("album", out var al) ? GetStr(al, "name") : "";
            list.Add(new MetaCandidate
            {
                Source = MetaSource.Netease,
                Id = id,
                Title = GetStr(item, "name"),
                Artist = artists,
                Album = album,
                DurationSeconds = GetInt(item, "duration") / 1000,
            });
        }
        return list;
    }

    static async Task<List<MetaCandidate>> SearchKugouAsync(string keyword)
    {
        var url = $"https://songsearch.kugou.com/song_search_v2?keyword={Uri.EscapeDataString(keyword)}&page=1&pagesize=10&area_code=1";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", "https://www.kugou.com/");
        using var doc = await GetJsonAsync(req);

        var list = new List<MetaCandidate>();
        if (doc == null ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("lists", out var lists)) return list;

        foreach (var item in lists.EnumerateArray())
        {
            var id = GetId(item, "SongID");
            if (id.Length == 0) continue;
            string? tags = null;
            if (item.TryGetProperty("TagDetails", out var td) && td.ValueKind == JsonValueKind.Array)
            {
                var joined = string.Join(", ", td.EnumerateArray()
                    .Select(t => GetStr(t, "tag_name")).Where(s => s.Length > 0));
                tags = NullIfEmpty(joined);
            }
            list.Add(new MetaCandidate
            {
                Source = MetaSource.Kugou,
                Id = id,
                Title = GetStr(item, "SongName"),
                Artist = GetStr(item, "SingerName"),
                Album = GetStr(item, "AlbumName"),
                DurationSeconds = GetInt(item, "Duration"),
                Extra = NullIfEmpty(GetStr(item, "AlbumImage")), // 封面直链（公开接口常为空）
                Tags = tags,
            });
        }
        return list;
    }

    // ---------- 获取完整元信息 ----------

    public static async Task<MetaResult?> FetchAsync(MetaCandidate c)
    {
        return c.Source switch
        {
            MetaSource.QQ => await FetchQQAsync(c),
            MetaSource.Netease => await FetchNeteaseAsync(c),
            _ => await FetchKugouAsync(c),
        };
    }

    static async Task<MetaResult> FetchQQAsync(MetaCandidate c)
    {
        byte[]? cover = null;
        if (c.Extra is { Length: > 0 } albumMid)
            cover = await GetBytesAsync($"https://y.gtimg.cn/music/photo_new/T002R500x500M000{albumMid}.jpg");

        string? lyric = null;
        try
        {
            var payload = "{\"req_0\":{\"module\":\"music.musichallSong.PlayLyricInfo\"," +
                $"\"method\":\"GetPlayLyricInfo\",\"param\":{{\"songMid\":\"{c.Id}\",\"ct\":0,\"cv\":0}}}}";
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data={Uri.EscapeDataString(payload)}");
            req.Headers.TryAddWithoutValidation("Referer", "https://y.qq.com/");
            using var doc = await GetJsonAsync(req);
            if (doc != null &&
                doc.RootElement.TryGetProperty("req_0", out var r0) &&
                r0.TryGetProperty("data", out var d) &&
                d.TryGetProperty("lyric", out var l) &&
                l.ValueKind == JsonValueKind.String)
            {
                lyric = DecodeBase64Utf8(l.GetString());
            }
        }
        catch { }

        return new MetaResult
        {
            Title = NullIfEmpty(c.Title),
            Artist = NullIfEmpty(c.Artist),
            Album = NullIfEmpty(c.Album),
            CoverData = cover,
            LyricText = NormalizeLrc(lyric),
        };
    }

    static async Task<MetaResult> FetchNeteaseAsync(MetaCandidate c)
    {
        byte[]? cover = null;
        string? lyric = null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://music.163.com/api/song/detail?ids=[{c.Id}]");
            req.Headers.TryAddWithoutValidation("Referer", "https://music.163.com/");
            using var doc = await GetJsonAsync(req);
            if (doc != null &&
                doc.RootElement.TryGetProperty("songs", out var songs) &&
                songs.ValueKind == JsonValueKind.Array)
            {
                foreach (var song in songs.EnumerateArray())
                {
                    if (!song.TryGetProperty("album", out var album)) continue;
                    var pic = GetStr(album, "picUrl");
                    if (pic.Length > 0) cover = await GetBytesAsync(pic);
                    break;
                }
            }
        }
        catch { }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://music.163.com/api/song/lyric?id={c.Id}&lv=1&kv=1&tv=-1");
            req.Headers.TryAddWithoutValidation("Referer", "https://music.163.com/");
            using var doc = await GetJsonAsync(req);
            if (doc != null && doc.RootElement.TryGetProperty("lrc", out var lrc))
                lyric = GetStr(lrc, "lyric");
        }
        catch { }

        return new MetaResult
        {
            Title = NullIfEmpty(c.Title),
            Artist = NullIfEmpty(c.Artist),
            Album = NullIfEmpty(c.Album),
            CoverData = cover,
            LyricText = NormalizeLrc(lyric),
        };
    }

    static async Task<MetaResult> FetchKugouAsync(MetaCandidate c)
    {
        // 酷狗公开搜索接口：封面/风格标签可能为空；歌词为 krc 二进制格式，不适合直接存 .lrc，故不同步歌词
        byte[]? cover = null;
        if (c.Extra is { Length: > 0 } img) cover = await GetBytesAsync(img);
        return new MetaResult
        {
            Title = NullIfEmpty(c.Title),
            Artist = NullIfEmpty(c.Artist),
            Album = NullIfEmpty(c.Album),
            Genre = c.Tags,
            CoverData = cover,
        };
    }

    // ---------- 批量同步 ----------

    /// <summary>批量同步规则：选择写回哪些字段、匹配严格程度、以及跳过已有内容的歌曲</summary>
    public sealed class SyncOptions
    {
        public bool SyncText = true;
        public bool SyncCover = true;
        public bool SyncLyric = true;
        /// <summary>仅当时长差 ≤5 秒才视为同一曲目；否则按最接近时长匹配并容忍较大偏差</summary>
        public bool StrictDuration;
        public bool SkipExistingCover;
        public bool SkipExistingLyric;
    }

    public enum SyncOutcome { Updated, NoMatch, Skipped, Failed }

    /// <summary>用本地标题+歌手构造搜索关键词（去除常见干扰后缀）</summary>
    public static string BuildKeyword(SongInfo song)
    {
        var title = song.DisplayTitle;
        foreach (var suffix in new[] { " (Live)", " - Live", "（现场版）", "（伴奏）", " (伴奏)" })
            title = title.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
        var artist = song.Artist == "未知歌手" ? "" : song.Artist;
        return $"{title} {artist}".Trim();
    }

    /// <summary>
    /// 对单首歌曲执行一次在线同步（搜索→按时长择优匹配，优先选择能取到歌词的候选→抓取→按规则写回），
    /// 供歌单批量同步使用。Detail 为结果说明（匹配到的歌名或失败原因）。
    /// </summary>
    public static async Task<(SyncOutcome Outcome, string Detail)> SyncOneAsync(
        SongInfo song, MetaSource source, SyncOptions opt)
    {
        var results = await SearchAsync(source, BuildKeyword(song));
        if (results.Count == 0) return (SyncOutcome.NoMatch, "无搜索结果");

        int target = (int)Math.Round(song.Duration.TotalSeconds);
        int tol = opt.StrictDuration ? 5 : Math.Max(20, target / 4);
        // 时长匹配容差内的候选按接近程度排序，最多探测前 3 个
        var candidates = results
            .Select(c => (c, diff: Math.Abs(c.DurationSeconds - target)))
            .Where(x => x.diff <= tol)
            .OrderBy(x => x.diff)
            .Take(3)
            .ToList();
        if (candidates.Count == 0) return (SyncOutcome.NoMatch, "时长不匹配");

        // 优先采用带歌词的候选；都取不到歌词时退回第一个抓取成功的候选
        MetaCandidate best = candidates[0].c;
        MetaResult? meta = null;
        foreach (var (cand, _) in candidates)
        {
            var m = await FetchAsync(cand);
            if (m == null) continue;
            if (meta == null) { meta = m; best = cand; } // 兜底：第一个抓取成功的候选
            if (m.LyricText != null || !opt.SyncLyric) { meta = m; best = cand; break; }
        }
        if (meta == null) return (SyncOutcome.Failed, "详情获取失败");

        // 按用户规则裁剪写回字段；跳过规则命中时对应字段置空
        bool hasCover = song.CoverData is { Length: > 0 };
        bool hasLyric = song.LyricPath != null;
        meta = new MetaResult
        {
            Title = opt.SyncText ? meta.Title : null,
            Artist = opt.SyncText ? meta.Artist : null,
            Album = opt.SyncText ? meta.Album : null,
            Genre = opt.SyncText ? meta.Genre : null,
            CoverData = opt.SyncCover && !(opt.SkipExistingCover && hasCover) ? meta.CoverData : null,
            LyricText = opt.SyncLyric && !(opt.SkipExistingLyric && hasLyric) ? meta.LyricText : null,
        };
        if (!meta.HasText && meta.CoverData == null && meta.LyricText == null)
            return (SyncOutcome.Skipped, "按规则无需写回");

        return await ApplyAsync(song, meta)
            ? (SyncOutcome.Updated, best.Title)
            : (SyncOutcome.Failed, "写入失败");
    }

    // ---------- 写回本地 ----------

    /// <summary>
    /// 将元信息写回音频文件标签（歌名/歌手/专辑/风格/封面），歌词保存为同名 .lrc，
    /// 随后重新解析更新曲库与本地缓存。返回 false 表示文件写入失败。
    /// </summary>
    public static async Task<bool> ApplyAsync(SongInfo song, MetaResult meta)
    {
        var written = await Task.Run(() =>
        {
            try
            {
                using (var f = TagLib.File.Create(song.FilePath))
                {
                    ApplyTags(f.Tag, meta);
                    if (meta.CoverData is { Length: > 0 } cover)
                    {
                        try
                        {
                            var pic = new TagLib.Picture(new TagLib.ByteVector(cover))
                            { Type = TagLib.PictureType.FrontCover };
                            f.Tag.Pictures = [pic];
                        }
                        catch { /* WAV 等不支持内嵌封面的格式：跳过封面 */ }
                    }
                    f.Save();
                }
                return true;
            }
            catch
            {
                // 整体写入失败（如封面导致）：仅重试文本标签
                try
                {
                    using var f = TagLib.File.Create(song.FilePath);
                    ApplyTags(f.Tag, meta);
                    f.Save();
                    return true;
                }
                catch { return false; }
            }
        });
        if (!written) return false;

        if (!string.IsNullOrWhiteSpace(meta.LyricText))
        {
            try
            {
                File.WriteAllText(Path.ChangeExtension(song.FilePath, ".lrc"),
                    meta.LyricText, new UTF8Encoding(false));
            }
            catch { }
        }

        // 重新解析并更新曲库（含封面缓存与指纹索引）
        return await MusicLibraryService.ResyncAsync(song) != null;
    }

    static void ApplyTags(TagLib.Tag tag, MetaResult meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.Title)) tag.Title = meta.Title;
        if (!string.IsNullOrWhiteSpace(meta.Artist)) tag.Performers = [meta.Artist];
        if (!string.IsNullOrWhiteSpace(meta.Album)) tag.Album = meta.Album;
        if (!string.IsNullOrWhiteSpace(meta.Genre)) tag.Genres = [meta.Genre];
    }

    // ---------- 通用辅助 ----------

    static async Task<JsonDocument?> GetJsonAsync(HttpRequestMessage request)
    {
        try
        {
            using var resp = await Http.SendAsync(request);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }
        catch { return null; }
    }

    static async Task<byte[]?> GetBytesAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return bytes.Length > 0 ? bytes : null;
        }
        catch { return null; }
    }

    static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    /// <summary>数字或字符串型 Id 字段统一取字符串</summary>
    static string GetId(JsonElement el, string name) => el.TryGetProperty(name, out var v)
        ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        }
        : "";

    static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : 0;

    static string? DecodeBase64Utf8(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return null; }
    }

    static string? NormalizeLrc(string? text) => NullIfEmpty(text?.Trim());

    static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MyQQMusic.Services;

/// <summary>
/// 听歌历史：记录每首歌（以文件路径为键）的播放次数与最近播放时间，
/// 持久化到 %AppData%\MyQQMusic\history.json，供首页推荐与热门歌曲计算。
/// </summary>
public static class PlayHistoryService
{
    public sealed record PlayStat(int Count, DateTime Last);

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyQQMusic");
    static readonly string HistoryFile = Path.Combine(Dir, "history.json");

    static readonly Dictionary<string, PlayStat> _stats = new();
    static readonly object _lock = new();
    static bool _loaded;

    /// <summary>一首歌成功开始播放时记录</summary>
    public static void Record(string songId)
    {
        if (string.IsNullOrEmpty(songId)) return;
        EnsureLoaded();
        lock (_lock)
        {
            var old = _stats.TryGetValue(songId, out var s) ? s : new PlayStat(0, DateTime.MinValue);
            _stats[songId] = old with { Count = old.Count + 1, Last = DateTime.Now };
            Save();
        }
    }

    /// <summary>当前历史快照副本（供首页推荐计算，避免遍历期间被修改）</summary>
    public static Dictionary<string, PlayStat> Snapshot()
    {
        EnsureLoaded();
        lock (_lock) return new Dictionary<string, PlayStat>(_stats);
    }

    static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(HistoryFile))
            {
                var json = File.ReadAllText(HistoryFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, PlayStat>>(json);
                if (data != null)
                    foreach (var (k, v) in data) _stats[k] = v;
            }
        }
        catch
        {
            // 历史损坏时从零开始记录
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(_stats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFile, json);
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}

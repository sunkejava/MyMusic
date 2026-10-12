using System.IO;
using System.Text.RegularExpressions;
using MyQQMusic.Models;

namespace MyQQMusic.Services;

/// <summary>
/// LRC 歌词解析
/// </summary>
public static class LrcService
{
    static readonly Regex LineRegex = new(@"\[(\d+):(\d+)\.(\d+)\](.*)", RegexOptions.Compiled);

    public static List<LyricLine> LoadFromFile(string path)
    {
        var result = new List<LyricLine>();
        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var m = LineRegex.Match(raw);
                if (!m.Success) continue;

                var msText = m.Groups[3].Value;
                int ms = int.Parse(msText);
                if (msText.Length == 2) ms *= 10; // .xx 表示百分之一秒

                var time = TimeSpan.FromSeconds(
                    int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) + ms / 1000.0);
                var text = m.Groups[4].Value.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                result.Add(new LyricLine { Time = time, Text = text });
            }
            result.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        catch
        {
            // 解析失败返回空列表
        }
        return result;
    }

    /// <summary>返回 position 之前最近的一行索引，无匹配返回 -1</summary>
    public static int FindIndex(List<LyricLine> lyrics, TimeSpan position)
    {
        int idx = -1;
        for (int i = 0; i < lyrics.Count; i++)
        {
            if (lyrics[i].Time <= position) idx = i;
            else break;
        }
        return idx;
    }
}

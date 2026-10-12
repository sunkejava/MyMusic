namespace MyQQMusic.Models;

/// <summary>
/// 一行 LRC 歌词
/// </summary>
public class LyricLine
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = "";
}

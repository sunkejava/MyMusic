using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyQQMusic.Models;

/// <summary>
/// 本地歌曲信息（以文件路径作为唯一标识）
/// </summary>
public class SongInfo
{
    /// <summary>稳定 Id：文件路径（跨扫描保持收藏/歌单引用有效）</summary>
    public string Id => FilePath;

    public string FilePath { get; init; } = "";
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(FilePath);
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "未知歌手";
    public string Album { get; init; } = "未知专辑";
    public int TrackNumber { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime DateAdded { get; init; } = DateTime.Now;

    /// <summary>封面原始字节（来自 ID3 等标签）</summary>
    public byte[]? CoverData { get; init; }
    public bool HasCover => CoverData is { Length: > 0 };

    ImageSource? _coverThumb;

    /// <summary>列表用低分辨率封面缩略图（懒加载并缓存）</summary>
    public ImageSource? CoverThumb => _coverThumb ??= DecodeCover(96);

    /// <summary>正在播放页用高分辨率封面（按需解码）</summary>
    public ImageSource? CreateFullCover() => DecodeCover(640);

    ImageSource? DecodeCover(int decodePixelWidth)
    {
        if (CoverData is not { Length: > 0 }) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.StreamSource = new MemoryStream(CoverData);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>同名 .lrc 歌词文件路径（不存在时为 null）</summary>
    string? _lyricPath;
    public string? LyricPath
    {
        get
        {
            _lyricPath ??= File.Exists(System.IO.Path.ChangeExtension(FilePath, ".lrc"))
                ? System.IO.Path.ChangeExtension(FilePath, ".lrc")
                : null;
            return _lyricPath;
        }
    }

    public string DurationText => $"{(int)Duration.TotalMinutes:00}:{Duration.Seconds:00}";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
}

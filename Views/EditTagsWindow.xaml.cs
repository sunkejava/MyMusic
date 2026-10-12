using System.Windows;
using System.Windows.Media.Imaging;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 标签编辑对话框：修改标题/歌手/专辑/曲目号并写回音频文件，
/// 或从文件重新读取最新元信息填入表单。
/// 批量模式：留空字段保持各文件原值不变，标题不批量修改。
/// </summary>
public partial class EditTagsWindow : Window
{
    readonly SongInfo? _song;
    readonly IReadOnlyList<SongInfo>? _songs;

    public EditTagsWindow(SongInfo song)
    {
        InitializeComponent();
        _song = song;

        NameText.Text = song.FileName;
        PathText.Text = song.FilePath;
        if (song.CoverThumb is BitmapSource bmp) CoverImg.Source = bmp;
        FillFrom(song);
    }

    /// <summary>批量编辑构造：预填所有歌曲共同的歌手/专辑/曲目号，无共同值则留空</summary>
    public EditTagsWindow(IReadOnlyList<SongInfo> songs)
    {
        InitializeComponent();
        _songs = songs;

        NameText.Text = $"已选择 {songs.Count} 首歌曲";
        PathText.Text = "留空的字段将保持原值不变；批量模式不修改标题";
        CoverBox.Visibility = Visibility.Collapsed;
        TitleBox.IsEnabled = false;
        ReloadBtn.Visibility = Visibility.Collapsed;
        SaveText.Text = $"保存到 {songs.Count} 首";

        ArtistBox.Text = CommonValue(songs, s => s.Artist, "未知歌手");
        AlbumBox.Text = CommonValue(songs, s => s.Album, "未知专辑");
        TrackBox.Text = CommonTrack(songs);
    }

    /// <summary>取所有歌曲一致的字段值（占位值按空处理），不一致返回空</summary>
    static string CommonValue(IReadOnlyList<SongInfo> songs, Func<SongInfo, string> get, string placeholder)
    {
        string? common = null;
        foreach (var s in songs)
        {
            var v = get(s) == placeholder ? "" : get(s);
            if (common == null) common = v;
            else if (common != v) return "";
        }
        return common ?? "";
    }

    /// <summary>所有歌曲曲目号一致且有效时返回该值，否则返回空</summary>
    static string CommonTrack(IReadOnlyList<SongInfo> songs)
    {
        int first = -1;
        foreach (var s in songs)
        {
            if (first < 0) first = s.TrackNumber;
            else if (first != s.TrackNumber) return "";
        }
        return first > 0 ? first.ToString() : "";
    }

    void FillFrom(SongInfo s)
    {
        TitleBox.Text = s.Title;
        ArtistBox.Text = s.Artist == "未知歌手" ? "" : s.Artist;
        AlbumBox.Text = s.Album == "未知专辑" ? "" : s.Album;
        TrackBox.Text = s.TrackNumber > 0 ? s.TrackNumber.ToString() : "";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>从文件重新读取最新标签填入表单（文件被外部修改后同步）</summary>
    async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_song == null) return;
        var latest = await Task.Run(() => MusicLibraryService.ParseFile(_song.FilePath));
        if (latest == null)
        {
            ShowError("读取文件失败，文件可能已被移动或删除");
            return;
        }
        FillFrom(latest);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(TrackBox.Text.Trim(), out int track);
        string artistInput = ArtistBox.Text.Trim();
        string albumInput = AlbumBox.Text.Trim();
        SaveBtn.IsEnabled = false;

        int total, failed = 0;
        if (_song != null)
        {
            total = 1;
            var updated = await MusicLibraryService.UpdateTagsAsync(_song,
                TitleBox.Text.Trim(), artistInput, albumInput, track);
            if (updated == null) failed = 1;
        }
        else
        {
            total = _songs!.Count;
            foreach (var s in _songs)
            {
                // 留空 = 保持原值；"未知歌手/未知专辑"占位按空处理
                string artist = artistInput.Length > 0 ? artistInput : (s.Artist == "未知歌手" ? "" : s.Artist);
                string album = albumInput.Length > 0 ? albumInput : (s.Album == "未知专辑" ? "" : s.Album);
                int t = track > 0 ? track : s.TrackNumber;
                var updated = await MusicLibraryService.UpdateTagsAsync(s, s.Title, artist, album, t);
                if (updated == null) failed++;
            }
        }
        SaveBtn.IsEnabled = true;

        if (failed > 0)
        {
            ShowError(failed == total
                ? "写入标签失败：文件可能正在播放被占用，或该格式不支持写入标签"
                : $"{failed}/{total} 首写入失败：文件可能正在播放被占用，或该格式不支持写入标签");
            return;
        }
        Close();
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

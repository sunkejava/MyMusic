using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 在线元信息同步对话框：选择音源（QQ音乐/网易云音乐/酷狗音乐）搜索匹配歌曲，
/// 预览其歌名、歌手、专辑、风格、封面与歌词，确认后写回本地文件标签与 .lrc。
/// </summary>
public partial class SyncMetaWindow : Window
{
    readonly SongInfo _song;
    MetaSource _source = MetaSource.QQ;
    MetaResult? _meta;
    int _searchSeq;   // 搜索请求序号，用于丢弃过期响应
    int _fetchSeq;    // 详情请求序号

    public SyncMetaWindow(SongInfo song)
    {
        InitializeComponent();
        _song = song;

        SongTitle.Text = song.DisplayTitle;
        SongArtist.Text = song.Artist;
        SrcQQ.Tag = true; // 默认选中 QQ 音乐
        SearchBox.Text = MetaSyncService.BuildKeyword(song);
        StatusText.Text = "输入歌名/歌手后回车搜索，点击候选条目查看元信息";

        Loaded += (_, _) => DoSearch();
    }

    // ---------- 音源切换 ----------

    void Source_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        foreach (var child in SourcePanel.Children)
            if (child is Button b) b.Tag = ReferenceEquals(b, btn);

        _source = btn == SrcNetease ? MetaSource.Netease
                : btn == SrcKugou ? MetaSource.Kugou
                : MetaSource.QQ;
        DoSearch();
    }

    // ---------- 搜索 ----------

    void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    async void DoSearch()
    {
        var keyword = SearchBox.Text.Trim();
        if (keyword.Length == 0) return;

        var seq = ++_searchSeq;
        ResultList.ItemsSource = null;
        ClearPreview();
        ApplyBtn.IsEnabled = false;
        StatusText.Text = "正在搜索…";

        var results = await MetaSyncService.SearchAsync(_source, keyword);
        if (seq != _searchSeq) return; // 已有更新的搜索请求

        ResultList.ItemsSource = results;
        StatusText.Text = results.Count > 0
            ? $"找到 {results.Count} 个结果，点击条目查看元信息（酷狗音源不提供歌词）"
            : "未找到结果，可尝试更换音源或简化关键词";
    }

    // ---------- 详情预览 ----------

    async void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultList.SelectedItem is not MetaCandidate c) return;

        var seq = ++_fetchSeq;
        ApplyBtn.IsEnabled = false;
        StatusText.Text = "正在获取元信息…";

        var meta = await MetaSyncService.FetchAsync(c) ?? new MetaResult();
        if (seq != _fetchSeq) return; // 已选择了其他候选

        _meta = meta;
        ShowPreview(c, meta);

        var parts = new List<string>();
        if (meta.Title != null || meta.Artist != null || meta.Album != null) parts.Add("文本元数据");
        if (meta.CoverData != null) parts.Add("封面海报");
        if (meta.LyricText != null) parts.Add("歌词");
        StatusText.Text = parts.Count > 0
            ? $"可同步：{string.Join("、", parts)}，点击“同步到本地”写回"
            : "该条目未采集到可写回的元信息";
        ApplyBtn.IsEnabled = parts.Count > 0;
    }

    void ShowPreview(MetaCandidate c, MetaResult meta)
    {
        CoverImg.Source = meta.CoverData is { Length: > 0 } bytes ? ToBitmap(bytes) : null;
        PTitle.Text = meta.Title ?? "（未提供）";
        PArtist.Text = "歌手：" + (meta.Artist ?? "未提供");
        PAlbum.Text = "专辑：" + (meta.Album ?? "未提供");
        PGenre.Text = "风格：" + (meta.Genre ?? (c.Source == MetaSource.Kugou ? "未提供" : "该音源不提供"));

        if (c.Source == MetaSource.Kugou)
        {
            PLyric.Text = "酷狗音源不提供歌词同步";
        }
        else if (meta.LyricText is { Length: > 0 } lyric)
        {
            // 预览前 12 行
            var lines = lyric.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            PLyric.Text = string.Join("\n", lines.Take(12));
        }
        else
        {
            PLyric.Text = "未获取到歌词";
        }
    }

    void ClearPreview()
    {
        _meta = null;
        CoverImg.Source = null;
        PTitle.Text = "";
        PArtist.Text = "";
        PAlbum.Text = "";
        PGenre.Text = "";
        PLyric.Text = "";
    }

    static BitmapImage? ToBitmap(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ---------- 写回 ----------

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_meta == null) return;
        ApplyBtn.IsEnabled = false;
        StatusText.Text = "正在写回本地文件…";

        var ok = await MetaSyncService.ApplyAsync(_song, _meta);
        if (ok)
        {
            Close();
            return;
        }

        StatusText.Text = "写入失败：文件可能被占用、只读或格式不支持标签写入";
        ApplyBtn.IsEnabled = true;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Drag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

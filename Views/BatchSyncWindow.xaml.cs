using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 歌单批量在线同步：选择音源与写回内容、匹配与跳过规则后，
/// 逐首搜索匹配并写回，展示进度与结果统计，可随时取消。
/// </summary>
public partial class BatchSyncWindow : Window
{
    readonly IReadOnlyList<SongInfo> _songs;
    MetaSource _source = MetaSource.Netease; // 默认优先使用网易云音乐源
    bool _running;
    bool _cancel;

    public BatchSyncWindow(Playlist playlist, IReadOnlyList<SongInfo> songs)
    {
        InitializeComponent();
        _songs = songs;

        SubtitleText.Text = $"“{playlist.Name}” 共 {songs.Count} 首 · 逐首搜索匹配并写回";
        SrcNetease.Tag = true;
        Progress.Maximum = Math.Max(1, songs.Count);

        if (songs.Count == 0)
        {
            StartBtn.IsEnabled = false;
            StatusText.Text = "歌单为空，没有可同步的歌曲";
        }
    }

    void Source_Click(object sender, RoutedEventArgs e)
    {
        if (_running || sender is not Button btn) return;
        foreach (var child in SourcePanel.Children)
            if (child is Button b) b.Tag = ReferenceEquals(b, btn);

        _source = btn == SrcNetease ? MetaSource.Netease
                : btn == SrcKugou ? MetaSource.Kugou
                : MetaSource.QQ;
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (OptText.IsChecked != true && OptCover.IsChecked != true && OptLyric.IsChecked != true)
        {
            StatusText.Text = "请至少勾选一种写回内容";
            return;
        }

        var opt = new MetaSyncService.SyncOptions
        {
            SyncText = OptText.IsChecked == true,
            SyncCover = OptCover.IsChecked == true,
            SyncLyric = OptLyric.IsChecked == true,
            StrictDuration = OptStrict.IsChecked == true,
            SkipExistingCover = OptSkipCover.IsChecked == true,
            SkipExistingLyric = OptSkipLyric.IsChecked == true,
        };

        _running = true;
        _cancel = false;
        StartBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;

        int done = 0, updated = 0, skipped = 0, noMatch = 0, failed = 0;
        foreach (var song in _songs)
        {
            if (_cancel) break;
            CurrentText.Text = $"正在同步：{song.DisplayTitle} — {song.Artist}";

            var (outcome, _) = await MetaSyncService.SyncOneAsync(song, _source, opt);
            switch (outcome)
            {
                case MetaSyncService.SyncOutcome.Updated: updated++; break;
                case MetaSyncService.SyncOutcome.Skipped: skipped++; break;
                case MetaSyncService.SyncOutcome.NoMatch: noMatch++; break;
                default: failed++; break;
            }

            done++;
            Progress.Value = done;
            StatusText.Text = $"已处理 {done}/{_songs.Count} · 成功 {updated} · 跳过 {skipped} · 未匹配 {noMatch} · 失败 {failed}";
        }

        _running = false;
        CancelBtn.IsEnabled = false;
        StartBtn.IsEnabled = true;
        CurrentText.Text = _cancel ? "已取消" : done >= _songs.Count ? "全部处理完成" : "已停止";
        SummaryText.Text = $"结果：成功 {updated} · 跳过 {skipped} · 未匹配 {noMatch} · 失败 {failed}";
        StatusText.Text = _cancel
            ? $"已在第 {done + 1} 首处取消；之前的结果已保存"
            : "批量同步完成，列表与播放页已自动刷新";
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancel = true;
        CancelBtn.IsEnabled = false;
        StatusText.Text = "正在取消…（当前这一首完成后停止）";
    }

    void Close_Click(object sender, RoutedEventArgs e)
    {
        _cancel = true;
        Close();
    }

    void Drag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

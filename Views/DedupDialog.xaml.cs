using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 歌单去重对话框：按“歌名+歌手+时长”自动识别歌单中的重复歌曲（不同目录/格式的同曲副本），
/// 每组默认推荐保留音质最好的一份，其余可勾选删除；可同时删除本地文件。
/// </summary>
public partial class DedupDialog : Window
{
    readonly Playlist _playlist;
    readonly List<RadioButton> _radios = [];

    public DedupDialog(Playlist playlist, IReadOnlyList<SongInfo> songs)
    {
        InitializeComponent();
        _playlist = playlist;
        SubtitleText.Text = $"“{playlist.Name}” 共 {songs.Count} 首 · 按歌名、歌手与时长识别重复";

        var groups = FindDuplicates(songs);
        if (groups.Count == 0)
        {
            GroupsPanel.Children.Add(new TextBlock
            {
                Text = "未检测到重复歌曲",
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 60, 0, 0),
                Foreground = Brush("TertiaryTextBrush"),
            });
            StatusText.Text = "没有需要处理的重复项";
            return;
        }

        int extra = groups.Sum(g => g.Count - 1);
        StatusText.Text = $"发现 {groups.Count} 组重复、{extra} 个多余副本；每组保留选中的一首";
        for (int gi = 0; gi < groups.Count; gi++)
            BuildGroup(groups[gi], gi);
        DeleteBtn.IsEnabled = true;
    }

    // ---------- 重复检测 ----------

    /// <summary>按（标题+歌手）分组后，组内按时长聚类（相邻差 ≤3 秒视为同一曲目副本）</summary>
    static List<List<SongInfo>> FindDuplicates(IReadOnlyList<SongInfo> songs)
    {
        var result = new List<List<SongInfo>>();
        foreach (var g in songs.GroupBy(s => Norm(s.DisplayTitle) + "\u0001" + Norm(s.Artist)))
        {
            var sorted = g.OrderBy(s => s.Duration.TotalSeconds).ToList();
            var cluster = new List<SongInfo>();
            foreach (var s in sorted)
            {
                if (cluster.Count > 0 &&
                    Math.Abs(s.Duration.TotalSeconds - cluster[^1].Duration.TotalSeconds) <= 3)
                {
                    cluster.Add(s);
                }
                else
                {
                    if (cluster.Count > 1) result.Add(cluster);
                    cluster = [s];
                }
            }
            if (cluster.Count > 1) result.Add(cluster);
        }
        return result;
    }

    /// <summary>标题/歌手归一化：仅保留字母数字并转小写，容忍标点与大小写差异</summary>
    static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>推荐保留：无损格式优先，其次文件更大（码率更高）</summary>
    static SongInfo Recommend(List<SongInfo> group)
    {
        static int Rank(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".flac" or ".ape" or ".wav" => 3,
            ".m4a" or ".m4b" or ".aac" => 2,
            ".mp3" => 1,
            _ => 0,
        };
        static long Size(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }
        return group.OrderByDescending(s => Rank(s.FilePath))
                    .ThenByDescending(s => Size(s.FilePath))
                    .First();
    }

    // ---------- 界面构建 ----------

    void BuildGroup(List<SongInfo> group, int gi)
    {
        var keep = Recommend(group);

        GroupsPanel.Children.Add(new TextBlock
        {
            Text = $"{group[0].DisplayTitle} — {group[0].Artist}（{group.Count} 份副本）",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 10, 2, 5),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("PrimaryTextBrush"),
        });

        foreach (var s in group)
        {
            var radio = new RadioButton
            {
                GroupName = $"keep{gi}",
                IsChecked = ReferenceEquals(s, keep),
                Content = ReferenceEquals(s, keep) ? "保留（推荐）" : "保留",
                Style = (Style)Application.Current.FindResource("ThemeRadioStyle"),
                Tag = s,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _radios.Add(radio);

            var fileName = new TextBlock
            {
                Text = Path.GetFileName(s.FilePath),
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("PrimaryTextBrush"),
            };

            string size;
            try { size = SizeText(new FileInfo(s.FilePath).Length); }
            catch { size = "大小未知"; }
            var meta = new TextBlock
            {
                Text = $"{Path.GetExtension(s.FilePath).TrimStart('.').ToUpperInvariant()} · {size} · {s.DurationText} · {Path.GetDirectoryName(s.FilePath)}",
                FontSize = 11,
                Margin = new Thickness(2, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush("TertiaryTextBrush"),
            };

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var row1 = new StackPanel { Orientation = Orientation.Horizontal };
            row1.Children.Add(radio);
            row1.Children.Add(fileName);
            left.Children.Add(row1);
            left.Children.Add(meta);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            GroupsPanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Background = Brush("InputBrush"),
                Child = grid,
            });
        }
    }

    static string SizeText(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        > 0 => $"{bytes / 1024:0} KB",
        _ => "大小未知",
    };

    static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    // ---------- 执行删除 ----------

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var toRemove = _radios.Where(r => r.IsChecked != true)
                              .Select(r => (SongInfo)r.Tag!).ToList();
        if (toRemove.Count == 0)
        {
            StatusText.Text = "没有勾选要删除的副本（每组必须保留一首）";
            return;
        }

        DeleteBtn.IsEnabled = false;
        bool delFiles = DelFilesBox.IsChecked == true;

        // 磁盘删除（NAS 上可能较慢）放后台；失败集合用于跳过曲库移除
        var failed = delFiles
            ? await Task.Run(() =>
            {
                var fail = new List<string>();
                foreach (var s in toRemove)
                {
                    try { File.Delete(s.FilePath); } catch { fail.Add(s.FilePath); }
                }
                return fail;
            })
            : [];

        foreach (var s in toRemove)
        {
            PlaylistService.RemoveSong(_playlist.Id, s.FilePath);
            if (delFiles && !failed.Contains(s.FilePath))
                MusicLibraryService.RemoveFromLibrary(s.FilePath);
        }

        if (failed.Count > 0)
        {
            StatusText.Text = $"已从歌单移除 {toRemove.Count} 项；{failed.Count} 个文件删除失败（可能被占用或只读）";
            DeleteBtn.IsEnabled = true;
            return;
        }
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Drag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

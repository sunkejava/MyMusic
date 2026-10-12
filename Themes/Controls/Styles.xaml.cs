using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyQQMusic.Models;
using MyQQMusic.Services;
using MyQQMusic.ViewModels;

namespace MyQQMusic.Themes.Controls;

/// <summary>
/// Styles.xaml 的 code-behind：承载共享歌曲行模板（SongRowTemplate）的事件处理器
/// </summary>
public partial class Styles : ResourceDictionary
{
    static MainViewModel? Main => Application.Current.MainWindow?.DataContext as MainViewModel;

    static SongInfo? SongOf(object sender) => (sender as FrameworkElement)?.DataContext as SongInfo;

    private void RowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return; // Border 无 DoubleClick 事件，用 ClickCount 判定
        e.Handled = true;
        if (SongOf(sender) is { } song) Main?.PlaySongDispatchCommand.Execute(song);
    }

    private void PlayButtonClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Main is not { } main || SongOf(sender) is not { } song) return;
        if (main.CurrentSong == song) main.Player.TogglePlay();
        else main.PlaySongDispatchCommand.Execute(song);
    }

    private void RowMoreClick(object sender, RoutedEventArgs e)
    {
        // 向上找到挂有 ContextMenu 的行容器并打开
        DependencyObject? cur = sender as DependencyObject;
        while (cur != null)
        {
            if (cur is Border row && row.ContextMenu != null)
            {
                row.ContextMenu.PlacementTarget = row;
                row.ContextMenu.IsOpen = true;
                return;
            }
            cur = VisualTreeHelper.GetParent(cur);
        }
    }

    private void RowMenuPlayClick(object sender, RoutedEventArgs e)
    {
        if (SongOf(sender) is { } song) Main?.PlaySongDispatchCommand.Execute(song);
    }

    private void RowMenuFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (SongOf(sender) is { } song) Main?.ToggleFavoriteFor(song);
    }

    private void RowMenuOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (SongOf(sender) is { } song) Main?.OpenFolderCommand.Execute(song.FilePath);
    }

    /// <summary>右键不改变选择：无修饰键且点击行不在选中内时，将选中归一化为该行</summary>
    private void RowRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
        if (sender is not FrameworkElement row) return;
        if (row.DataContext is not SongInfo song) return;
        if (FindListBox(row) is not { } lb) return;
        if (!lb.SelectedItems.Contains(song))
            lb.SelectedItem = song;
    }

    /// <summary>沿视觉/逻辑树向上查找行所在的 ListBox</summary>
    static ListBox? FindListBox(DependencyObject? start)
    {
        while (start != null)
        {
            if (start is ListBox lb) return lb;
            DependencyObject? parent = null;
            if (start is Visual)
            {
                try { parent = VisualTreeHelper.GetParent(start); } catch { }
            }
            start = parent ?? LogicalTreeHelper.GetParent(start);
        }
        return null;
    }

    /// <summary>菜单目标歌曲：右键行处于多选状态时返回全部选中歌曲，否则仅该首</summary>
    static IReadOnlyList<SongInfo> MenuTargets(object sender)
    {
        var result = new List<SongInfo>();
        object? cur = sender;
        while (cur is MenuItem mi) cur = mi.Parent;
        if (cur is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target)
            return result;
        if (target.DataContext is not SongInfo song)
            return result;

        var lb = FindListBox(target);
        if (lb != null && lb.SelectedItems.Count > 1 && lb.SelectedItems.Contains(song))
        {
            foreach (object o in lb.SelectedItems)
                if (o is SongInfo s) result.Add(s);
            return result;
        }
        result.Add(song);
        return result;
    }

    /// <summary>打开标签编辑对话框（多选时批量编辑）</summary>
    private void RowMenuEditTagsClick(object sender, RoutedEventArgs e)
    {
        var targets = MenuTargets(sender);
        if (targets.Count == 0) return;
        var dlg = targets.Count == 1
            ? new MyQQMusic.Views.EditTagsWindow(targets[0])
            : new MyQQMusic.Views.EditTagsWindow(targets);
        dlg.Owner = Application.Current.MainWindow;
        dlg.ShowDialog();
    }

    /// <summary>从文件重新读取元信息并刷新列表（支持批量）</summary>
    private async void RowMenuResyncClick(object sender, RoutedEventArgs e)
    {
        foreach (var song in MenuTargets(sender))
            await MusicLibraryService.ResyncAsync(song);
    }

    /// <summary>从在线音源（QQ/网易云/酷狗）采集元信息并写回</summary>
    private void RowMenuSyncMetaClick(object sender, RoutedEventArgs e)
    {
        if (SongOf(sender) is not { } song) return;
        var dlg = new MyQQMusic.Views.SyncMetaWindow(song) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    /// <summary>"加入歌单"子菜单：每次展开时按当前歌单列表重建</summary>
    private void RowMenuPlaylistSubmenu(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;

        item.Items.Clear();
        var style = (Style)Application.Current.FindResource("QQMenuItemStyle");
        foreach (var playlist in PlaylistService.Playlists)
        {
            var mi = new MenuItem
            {
                Style = style,
                Header = playlist.Name,
                DataContext = playlist
            };
            mi.Click += RowMenuAddToPlaylistClick;
            item.Items.Add(mi);
        }
    }

    private void RowMenuAddToPlaylistClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.DataContext is not Playlist playlist) return;
        if ((item.Parent as MenuItem)?.DataContext is not SongInfo song) return;
        Main?.AddToPlaylistCommand.Execute(new object[] { song, playlist });
    }
}

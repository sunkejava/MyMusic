using System.Windows;
using System.Windows.Controls;
using MyQQMusic.ViewModels;

namespace MyQQMusic.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView() => InitializeComponent();

    /// <summary>歌单去重：检测重复歌曲并让用户确认删除</summary>
    void Dedup_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistViewModel vm || vm.SelectedPlaylist is not { } pl) return;
        if (vm.SelectedSongs.Count == 0) return;
        var dlg = new DedupDialog(pl, vm.SelectedSongs) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    /// <summary>批量同步歌单全部歌曲的在线元信息</summary>
    void BatchSync_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PlaylistViewModel vm || vm.SelectedPlaylist is not { } pl) return;
        if (vm.SelectedSongs.Count == 0) return;
        var dlg = new BatchSyncWindow(pl, vm.SelectedSongs) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }
}

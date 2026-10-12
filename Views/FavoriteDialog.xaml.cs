using System.Windows;
using System.Windows.Input;
using MyQQMusic.Models;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 收藏弹窗：选择已有歌单或新建歌单，将当前歌曲收藏进去
/// </summary>
public partial class FavoriteDialog : Window
{
    readonly SongInfo _song;

    public FavoriteDialog(SongInfo song)
    {
        InitializeComponent();
        _song = song;
        SongTitle.Text = song.DisplayTitle;
        SongArtist.Text = song.Artist;
        PlaylistBox.ItemsSource = PlaylistService.Playlists;
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Drag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Confirm_Click(object sender, RoutedEventArgs e) => AddTo(PlaylistBox.SelectedItem as Playlist);

    void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddTo(PlaylistBox.SelectedItem as Playlist);

    void AddTo(Playlist? playlist)
    {
        if (playlist == null) return;
        PlaylistService.AddSong(playlist.Id, _song.Id);
        Close();
    }

    void NewNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CreateNew();
    }

    void CreateAndAdd_Click(object sender, RoutedEventArgs e) => CreateNew();

    void CreateNew()
    {
        string name = NewNameBox.Text.Trim();
        if (name.Length == 0) return;
        var playlist = PlaylistService.Create(name);
        PlaylistService.AddSong(playlist.Id, _song.Id);
        Close();
    }
}

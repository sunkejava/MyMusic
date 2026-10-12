using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyQQMusic.Models;

/// <summary>
/// 歌单（歌曲以文件路径引用）
/// </summary>
public class Playlist : INotifyPropertyChanged
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public List<string> SongPaths { get; set; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>系统歌单（如"我喜欢"）不可删除</summary>
    public bool IsSystem { get; init; }

    public int Count => SongPaths.Count;

    /// <summary>歌曲增删后刷新 Count 绑定</summary>
    public void RaiseCount() => OnPropertyChanged(nameof(Count));

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

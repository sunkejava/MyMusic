using System.IO;
using System.Text.Json;
using MyQQMusic.Models;

namespace MyQQMusic.Services;

/// <summary>
/// 设置与数据持久化（%AppData%\MyQQMusic）
/// </summary>
public static class SettingsService
{
    /// <summary>播放外观（模式/皮肤）变更通知，用于播放页与设置页跨页同步选中状态</summary>
    public static event Action? AppearanceChanged;

    public static void NotifyAppearanceChanged() => AppearanceChanged?.Invoke();

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyQQMusic");
    static readonly string SettingsFile = Path.Combine(Dir, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static string PlaylistsFilePath => Path.Combine(Dir, "playlists.json");

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                // 旧版本保存的播放主题键迁移到新键
                Current.PlayTheme = ThemeService.NormalizePlayTheme(Current.PlayTheme);
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}

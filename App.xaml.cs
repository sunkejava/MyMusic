using System.IO;
using System.Windows;
using System.Windows.Threading;
using MyQQMusic.Services;

namespace MyQQMusic;

/// <summary>
/// 应用入口：启动时加载设置/歌单/主题并扫描曲库
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：记录日志，避免 UI 线程异常直接崩溃退出
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogError("AppDomain", args.ExceptionObject as Exception);

        SettingsService.Load();
        PlaylistService.Load();

        var settings = SettingsService.Current;
        ThemeService.ApplyAll(settings.Theme, settings.Accent, settings.Background);

        // 优先加载上次扫描保存的本地缓存（秒开）；命中后仅在后台做增量比对，
        // 只解析新增/变更的文件；无缓存或目录不一致时才执行完整扫描。
        if (MusicLibraryService.TryLoadFromCache(settings.MusicDirectory))
            _ = MusicLibraryService.RefreshIfChangedAsync(settings.MusicDirectory);
        else
            _ = MusicLibraryService.ScanAsync(settings.MusicDirectory);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("Dispatcher", e.Exception);
        e.Handled = true; // 吞掉异常保持程序存活，防止单页异常导致整体崩溃
    }

    static void LogError(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyQQMusic");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save();
        PlaylistService.Save();
        base.OnExit(e);
    }
}

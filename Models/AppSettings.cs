namespace MyQQMusic.Models;

/// <summary>播放模式</summary>
public enum PlayMode
{
    /// <summary>顺序播放</summary>
    Sequential = 0,
    /// <summary>列表循环</summary>
    ListLoop = 1,
    /// <summary>单曲循环</summary>
    SingleLoop = 2,
    /// <summary>随机播放</summary>
    Shuffle = 3
}

/// <summary>
/// 应用设置（持久化到 %AppData%\MyQQMusic\settings.json）
/// </summary>
public class AppSettings
{
    /// <summary>明暗主题："Dark" / "Light"</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>强调色："Green" / "Blue" / "Purple" / "Pink" / "Orange"</summary>
    public string Accent { get; set; } = "Green";

    /// <summary>内置背景键："Default" / "Ocean" / "Forest" / "Sunset" / "Violet"（深色）；"Default" / "Morning" / "Frost" / "Sakura"（浅色）；"Custom" 表示自定义图片</summary>
    public string Background { get; set; } = "Default";

    /// <summary>自定义背景图片路径</summary>
    public string? CustomBackgroundPath { get; set; }

    /// <summary>自定义背景图片不透明度（0-1），越低遮罩越重、文字越清晰</summary>
    public double CustomBackgroundOpacity { get; set; } = 0.7;

    /// <summary>自定义背景磨砂模糊半径（0-40），0 表示不模糊</summary>
    public double CustomBackgroundBlur { get; set; } = 10;

    /// <summary>播放页模式："Standard" 标准 / "Vinyl" 黑胶 / "Lyric" 歌词（旧键 Classic/Immersive 自动迁移）</summary>
    public string PlayTheme { get; set; } = "Standard";

    /// <summary>播放页皮肤（PlayerSkins 中定义的 Key；Default 表示跟随应用主题）</summary>
    public string PlayerSkin { get; set; } = "Default";

    /// <summary>歌词高亮方式：Sweep 逐渐变色 / SweepGrow 变色+放大 / Grow 逐渐放大 / Fade 整行变色</summary>
    public string LyricHighlight { get; set; } = "Sweep";

    /// <summary>自定义歌词高亮色（hex #RRGGBB；空 = 跟随强调色）</summary>
    public string LyricColor { get; set; } = "";

    /// <summary>自定义背景色（hex，配合 Background="CustomColor" 使用）</summary>
    public string CustomBackgroundColor { get; set; } = "#1A2028";

    /// <summary>播放页是否跟随当前歌曲封面主色生成氛围背景</summary>
    public bool CoverAmbientBackground { get; set; } = true;

    /// <summary>音频输出设备 ID（null 或空表示系统默认设备）</summary>
    public string? AudioDeviceId { get; set; }

    /// <summary>反转滚轮/触摸板滚动方向（默认关闭，即标准 Windows 滚动方向；可在设置页开启）</summary>
    public bool ReverseScroll { get; set; }

    public string MusicDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    /// <summary>桌面歌词字体族</summary>
    public string DeskLyricFont { get; set; } = "Microsoft YaHei UI";

    /// <summary>桌面歌词字号（14-72）</summary>
    public double DeskLyricFontSize { get; set; } = 30;

    /// <summary>桌面歌词文字颜色（hex）</summary>
    public string DeskLyricColor { get; set; } = "#FFFFFF";

    /// <summary>桌面歌词背景颜色（hex）</summary>
    public string DeskLyricBgColor { get; set; } = "#000000";

    /// <summary>桌面歌词背景不透明度（0-1），0 表示全透明</summary>
    public double DeskLyricBgOpacity { get; set; } = 0.0;

    public double Volume { get; set; } = 0.8;
    public PlayMode PlayMode { get; set; } = PlayMode.ListLoop;
    public string? LastSongPath { get; set; }
}

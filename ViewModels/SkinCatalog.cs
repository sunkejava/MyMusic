using System.Windows;
using System.Windows.Media;
using MyQQMusic.Services;

namespace MyQQMusic.ViewModels;

/// <summary>
/// 播放皮肤选择器共享数据：色卡与分组构建，
/// 设置页皮肤画廊与播放页右侧抽屉复用同一份定义。
/// </summary>
public static class SkinCatalog
{
    /// <summary>皮肤色卡（Preview 背景渐变 / Dot 唱片主色）</summary>
    public sealed record Swatch(string Key, string Name, Brush Preview, Brush Dot);

    /// <summary>皮肤分组（WPF 绑定需要真实属性名，不能用 ValueTuple）</summary>
    public sealed record SkinGroup(string Group, IReadOnlyList<Swatch> Items);

    public static IReadOnlyList<SkinGroup> BuildGroups() =>
        [.. PlayerSkins.All.GroupBy(s => s.Group).Select(g =>
            new SkinGroup(g.Key, [.. g.Select(s => new Swatch(s.Key, s.Name, BuildPreview(s), BuildDot(s)))]))];

    static Brush BuildPreview(PlayerSkin s)
    {
        if (s.Key == "Default")
            return new LinearGradientBrush(Color.FromRgb(0x9A, 0xA5, 0xB0), Color.FromRgb(0x6E, 0x77, 0x82), 45);
        // 全屏封面皮肤用中性夜色示意
        string[] stops = s.FullCover ? ["#FF3A4250", "#FF1A2028"] : s.Bg;
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < stops.Length; i++)
            b.GradientStops.Add(new GradientStop(ParseHex(stops[i], Colors.Black),
                stops.Length == 1 ? 0 : (double)i / (stops.Length - 1)));
        return b;
    }

    static Brush BuildDot(PlayerSkin s)
    {
        string? hex = s.DiscColor ?? s.Label ?? s.WaveColor ?? s.Glow;
        if (hex == "rainbow")
            return new LinearGradientBrush(Color.FromRgb(0xFF, 0x9A, 0xA8), Color.FromRgb(0xC9, 0xA2, 0xFF), 45);
        if (hex == "gold")
            return new LinearGradientBrush(Color.FromRgb(0xEF, 0xC7, 0x6A), Color.FromRgb(0x8A, 0x6A, 0x22), 45);
        return new SolidColorBrush(ParseHex(hex, Color.FromRgb(0x20, 0x22, 0x26)));
    }

    static Color ParseHex(string? hex, Color fallback)
    {
        try { return string.IsNullOrEmpty(hex) ? fallback : (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }
}

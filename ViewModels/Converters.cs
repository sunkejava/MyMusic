using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MyQQMusic.ViewModels;

/// <summary>把多个绑定值合并为 object[]，供多参数命令使用</summary>
public class ArrayMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Clone();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => (object[])value;
}

/// <summary>判断两个值是否相等（用于"当前播放行"高亮）</summary>
public class EqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => (object[])value;
}

/// <summary>将绑定值与 ConverterParameter 指定的键比较（用于菜单项选中态，避免 Tag 自引用）</summary>
public class EqualParamConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 1 && Equals(values[0], parameter as string);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>导航 Key → 图标资源</summary>
public class NavIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            "Home" => "IconHome",
            "Library" => "IconMusic",
            "Playlist" => "IconPlaylist",
            "NowPlaying" => "IconDisc",
            "Settings" => "IconSettings",
            _ => "IconMusic"
        };
        return Application.Current.TryFindResource(key) as Geometry
               ?? Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>bool → 图标资源（parameter 形如 "IconOff:IconOn"，true 取后者）</summary>
public class ToggleIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var keys = ((string)parameter).Split(':');
        var on = value is bool b && b;
        var key = on && keys.Length > 1 ? keys[1] : keys[0];
        return Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>播放模式 → 图标资源</summary>
public class ModeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Models.PlayMode.Sequential => "IconSequential",
            Models.PlayMode.SingleLoop => "IconRepeatOne",
            Models.PlayMode.Shuffle => "IconShuffle",
            _ => "IconRepeat"
        };
        return Application.Current.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

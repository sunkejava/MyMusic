namespace MyQQMusic.Models;

/// <summary>
/// 设置页选项（主题/强调色/背景等），Extra 可携带色值等信息
/// </summary>
public record OptionItem(string Key, string Name, string? Extra = null);

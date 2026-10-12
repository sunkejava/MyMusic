namespace MyQQMusic.Services;

/// <summary>播放页音波氛围类型</summary>
public enum WaveKind { None, Bars, CircleBars, Particles, Planet, Waves }

/// <summary>
/// 播放页皮肤定义：背景渐变、唱片配色、光晕、音波氛围、星空/水印字等，
/// 由 NowPlayingView 按参数统一渲染（纯矢量绘制，不依赖外部图片）。
/// </summary>
public sealed class PlayerSkin
{
    public string Key { get; init; } = "Default";
    public string Name { get; init; } = "跟随主题";
    public string Group { get; init; } = "默认";
    /// <summary>背景渐变色（2-4 个 hex，左上→右下）；空 = 跟随应用主题</summary>
    public string[] Bg { get; init; } = [];
    /// <summary>唱片样式："Vinyl" 黑胶圆盘 / "Cover" 唱片封面（彩色厂牌圈）</summary>
    public string Disc { get; init; } = "Vinyl";
    /// <summary>唱片染色（hex；"rainbow"/"gold" 为特殊渐变；null 保持默认）</summary>
    public string? DiscColor { get; init; }
    /// <summary>中心厂牌圈颜色</summary>
    public string? Label { get; init; }
    /// <summary>唱片外光晕颜色（null 使用主题强调色）</summary>
    public string? Glow { get; init; }
    public WaveKind Wave { get; init; }
    public string? WaveColor { get; init; }
    /// <summary>音波加密/加长变体</summary>
    public bool WaveDense { get; init; }
    /// <summary>是否绘制星空（星座皮肤附带连线图形）</summary>
    public bool Stars { get; init; }
    /// <summary>星座图形键（ConstellationShapes 中的 Key）</summary>
    public string? StarsShape { get; init; }
    /// <summary>背景大字水印（生肖皮肤）</summary>
    public string Glyph { get; init; } = "";
    public string? GlyphColor { get; init; }
    /// <summary>以当前封面模糊图作为全屏背景</summary>
    public bool FullCover { get; init; }
    /// <summary>唱片流光扫过效果</summary>
    public bool Light { get; init; }
}

/// <summary>播放页皮肤库：默认 + 黑胶/艺术/祝福/色彩/典藏/音波/星座/生肖系列</summary>
public static class PlayerSkins
{
    public static readonly PlayerSkin Default = new();

    static readonly PlayerSkin[] AllSkins =
    [
        // ---------- 黑胶系列 ----------
        New("ClassicVinyl", "经典黑胶", "黑胶系列", ["#FF16171A", "#FF0B0C0E"]),
        New("LightVinyl", "光影黑胶", "黑胶系列", ["#FF1A1C22", "#FF0D0F13"], light: true),
        New("CoverDisc", "唱片封面", "黑胶系列", ["#FF2B2433", "#FF141019"], disc: "Cover", label: "#FFD8B45A"),
        New("FullCover", "全屏封面", "黑胶系列", ["#FF121317", "#FF0A0B0E"], fullCover: true),
        New("YouthColor", "青春彩胶", "黑胶系列", ["#FF2B2140", "#FF1A1430"], discColor: "rainbow", glow: "#FFFF7FB0"),
        New("TimeFlow", "时光模式", "黑胶系列", ["#FF3A2B1E", "#FF171009"], discColor: "#FF6A4A2A", glow: "#FFFFB35C"),
        New("CyberDisc", "赛博唱片", "黑胶系列", ["#FF120B2E", "#FF2E1065"], discColor: "#FF2A1A5E", glow: "#FF33E0FF",
            wave: WaveKind.CircleBars, waveColor: "#FF33E0FF"),
        New("GlassWave", "琉璃光波", "黑胶系列", ["#FF0E2A3A", "#FF1E5A6E"], discColor: "#FF1E4A5A", glow: "#FF3AC8C8",
            wave: WaveKind.Waves, waveColor: "#FF58D6C0"),

        // ---------- 艺术系列 ----------
        New("MonetSunrise", "莫奈《日出印象》", "艺术系列", ["#FF1E2A38", "#FF2E3E4E"], glow: "#FFFF8A4C",
            discColor: "#FF22303E", wave: WaveKind.Waves, waveColor: "#FF6A8AA8"),
        New("ThousandRivers", "千里江山图", "艺术系列", ["#FF0E3B33", "#FF1F6B52"], discColor: "#FF14503F", glow: "#FF3FA87A"),
        New("RousseauDream", "卢梭《梦》", "艺术系列", ["#FF141B3C", "#FF3A2A5E"], discColor: "#FF2A2450", glow: "#FFFF8FB8"),
        New("StarryNight", "梵高《星夜》", "艺术系列", ["#FF0B1F4A", "#FF1E3A7A"], discColor: "#FF12295E", glow: "#FFFFD666",
            stars: true),
        New("TheScream", "蒙克《呐喊》", "艺术系列", ["#FF8A3A16", "#FF3A1A0E"], discColor: "#FF4A2412", glow: "#FFFF7A45"),

        // ---------- 祝福系列 ----------
        New("PeachBlossom", "桃花旺", "祝福系列", ["#FF5A1E2E", "#FF2E0E18"], discColor: "#FF8A3A52", glow: "#FFFF8FA3"),
        New("FortuneHealth", "福安康", "祝福系列", ["#FF6E1A1A", "#FF320C0C"], label: "#FFE8C060", glow: "#FFFF6A5A"),
        New("FullGranary", "富满仓", "祝福系列", ["#FF5A3A12", "#FF2A1A08"], discColor: "#FF8A6A2A", glow: "#FFFFC060"),
        New("DragonGate", "跃龙门", "祝福系列", ["#FF123A4A", "#FF0A1E28"], label: "#FFE8B04A", glow: "#FF4AC8E0"),
        New("CareerRise", "事业升", "祝福系列", ["#FF2A1E5A", "#FF140E30"], discColor: "#FF3A2E6E", glow: "#FF8A7AFF"),

        // ---------- 色彩系列 ----------
        New("OchreRed", "复古赭石红", "色彩系列", ["#FF4A2A22", "#FF2A1612"], discColor: "#FF6A3A2A", glow: "#FFD08A5A"),
        New("SnowWhite", "简约雪域白", "色彩系列", ["#FF8FA4B8", "#FFC4D4E2"], light: true),
        New("LycheeWhite", "复古荔肉白", "色彩系列", ["#FF9A8F80", "#FFCFC4B6"], light: true),
        New("SwallowBlue", "复古燕颔蓝", "色彩系列", ["#FF2E4258", "#FF5A7288"]),
        New("ObsidianBlack", "简约曜石黑", "色彩系列", ["#FF121214", "#FF1E1E22"], discColor: "#FF1A1A1E"),

        // ---------- 典藏系列 ----------
        New("BlackGold", "典藏黑金唱片", "典藏系列", ["#FF141414", "#FF0A0A0A"], discColor: "#FF2A2A2A",
            label: "#FFD4AF37", glow: "#FFD4AF37"),
        New("GoldDisc", "典藏黄金唱片", "典藏系列", ["#FF2A2008", "#FF141004"], discColor: "gold",
            label: "#FFF0D060", glow: "#FFE8C060"),

        // ---------- 音波系列 ----------
        New("WaveDust", "音波宇宙尘埃", "音波系列", ["#FF0B1020", "#FF141B33"], wave: WaveKind.Particles,
            waveColor: "#FF9FB8E8"),
        New("WavePlanet", "音波孤单星球", "音波系列", ["#FF0A1226", "#FF101A33"], wave: WaveKind.Planet,
            waveColor: "#FF7FA8FF"),
        New("WaveMelody", "音波跳动旋律", "音波系列", ["#FF101828", "#FF1A2440"], wave: WaveKind.Bars,
            waveColor: "#FF4FA8FF"),
        New("WaveWater", "音波迷幻水波", "音波系列", ["#FF0E2A33", "#FF1E3A55"], wave: WaveKind.Waves,
            waveColor: "#FF58D6C0"),
        New("WaveBurst", "音波爆炸粒子", "音波系列", ["#FF2A0E28", "#FF45123A"], wave: WaveKind.Particles,
            waveColor: "#FFFF7AB8", waveDense: true),
        New("WaveScale", "音波动感音阶", "音波系列", ["#FF2E1A0E", "#FF4A2A14"], wave: WaveKind.Bars,
            waveColor: "#FFFFB35C"),
        New("WaveCrystal", "音波水晶音波", "音波系列", ["#FF0E2233", "#FF16324A"], wave: WaveKind.CircleBars,
            waveColor: "#FF9FE8FF"),
        New("WaveRays", "音波音乐射线", "音波系列", ["#FF17102E", "#FF2A1A4A"], wave: WaveKind.CircleBars,
            waveColor: "#FFC9A2FF", waveDense: true),
    ];

    // ---------- 十二星座 ----------
    static readonly (string Key, string Name, string Color)[] ZodiacConstellations =
    [
        ("ConstAries", "白羊座", "#FFFF7A5A"), ("ConstTaurus", "金牛座", "#FF8FD87A"),
        ("ConstGemini", "双子座", "#FFFFD666"), ("ConstCancer", "巨蟹座", "#FF9FB8E8"),
        ("ConstLeo", "狮子座", "#FFFFA94D"), ("ConstVirgo", "处女座", "#FFB8E0A0"),
        ("ConstLibra", "天秤座", "#FF7AD8D0"), ("ConstScorpio", "天蝎座", "#FFFF5A6A"),
        ("ConstSagittarius", "射手座", "#FFC9A2FF"), ("ConstCapricorn", "摩羯座", "#FF8A9AB0"),
        ("ConstAquarius", "水瓶座", "#FF5AC8E8"), ("ConstPisces", "双鱼座", "#FF9AB0FF"),
    ];

    /// <summary>星座连线图形：点（归一化 0-1 坐标串）+ 边（索引对串）</summary>
    public static readonly Dictionary<string, (string Points, string Edges)> ConstellationShapes = new()
    {
        ["ConstAries"] = ("0.18,0.62 0.36,0.40 0.56,0.32 0.74,0.44", "0-1 1-2 2-3"),
        ["ConstTaurus"] = ("0.50,0.55 0.38,0.38 0.26,0.22 0.62,0.38 0.76,0.24", "0-1 1-2 0-3 3-4"),
        ["ConstGemini"] = ("0.30,0.20 0.35,0.45 0.40,0.72 0.62,0.18 0.65,0.45 0.70,0.72", "0-1 1-2 3-4 4-5 1-4"),
        ["ConstCancer"] = ("0.50,0.24 0.50,0.50 0.30,0.70 0.70,0.68", "0-1 1-2 1-3"),
        ["ConstLeo"] = ("0.24,0.62 0.30,0.38 0.46,0.28 0.58,0.42 0.50,0.60 0.72,0.55 0.64,0.72", "0-1 1-2 2-3 3-4 4-0 4-5 5-6"),
        ["ConstVirgo"] = ("0.20,0.40 0.34,0.28 0.50,0.44 0.66,0.32 0.78,0.54 0.60,0.62", "0-1 1-2 2-3 3-4 2-5"),
        ["ConstLibra"] = ("0.50,0.24 0.28,0.56 0.72,0.56 0.50,0.56", "0-1 0-2 1-2 2-3"),
        ["ConstScorpio"] = ("0.20,0.30 0.32,0.44 0.44,0.38 0.56,0.52 0.62,0.70 0.76,0.62", "0-1 1-2 2-3 3-4 4-5"),
        ["ConstSagittarius"] = ("0.28,0.62 0.38,0.36 0.56,0.32 0.68,0.55 0.52,0.66", "0-1 1-2 2-3 3-4 4-0"),
        ["ConstCapricorn"] = ("0.20,0.34 0.36,0.56 0.56,0.64 0.74,0.46", "0-1 1-2 2-3"),
        ["ConstAquarius"] = ("0.20,0.40 0.32,0.56 0.46,0.40 0.60,0.56 0.76,0.42", "0-1 1-2 2-3 3-4"),
        ["ConstPisces"] = ("0.20,0.34 0.36,0.52 0.62,0.46 0.78,0.62", "0-1 1-2 2-3"),
    };

    static PlayerSkin[] BuildConstellations() =>
    [.. ZodiacConstellations.Select(c => New(c.Key, c.Name, "十二星座",
        ["#FF0A1024", "#FF141B33"], discColor: null, glow: c.Color,
        stars: true, starsShape: c.Key, waveColor: c.Color))];

    // ---------- 十二生肖 ----------
    static readonly (string Name, string Glyph, string Bg1, string Bg2, string Color)[] ZodiacAnimals =
    [
        ("子鼠", "鼠", "#FF3A2A1A", "#FF1A1208", "#FFD8B45A"), ("丑牛", "牛", "#FF4A2A1A", "#FF241208", "#FFE0955A"),
        ("寅虎", "虎", "#FF3A2A0A", "#FF1E1404", "#FFFFB35C"), ("卯兔", "兔", "#FF4A2030", "#FF241018", "#FFFF9AB8"),
        ("辰龙", "龙", "#FF3A1A1A", "#FF1E0C0C", "#FFFF6A5A"), ("巳蛇", "蛇", "#FF2A3A2A", "#FF141E14", "#FF8FD87A"),
        ("午马", "马", "#FF3A2E10", "#FF1C1608", "#FFE8C060"), ("未羊", "羊", "#FF2E2A3A", "#FF16141E", "#FFC9B8E8"),
        ("申猴", "猴", "#FF3A2412", "#FF1C1208", "#FFFFA94D"), ("酉鸡", "鸡", "#FF401A2A", "#FF200C14", "#FFFF7A8A"),
        ("戌狗", "狗", "#FF2A2A3A", "#FF141420", "#FF9AB8E8"), ("亥猪", "猪", "#FF3A2038", "#FF1E101C", "#FFFF9AC8"),
    ];

    static PlayerSkin[] BuildAnimals() =>
    [.. ZodiacAnimals.Select((a, i) => New($"Animal{i}", a.Name, "十二生肖",
        [a.Bg1, a.Bg2], glyph: a.Glyph, glyphColor: a.Color, glow: a.Color))];

    /// <summary>全部皮肤（含默认，按分组展示顺序）</summary>
    public static readonly IReadOnlyList<PlayerSkin> All =
        [Default, .. AllSkins, .. BuildConstellations(), .. BuildAnimals()];

    /// <summary>按 Key 查找皮肤，未找到返回默认</summary>
    public static PlayerSkin Find(string? key) =>
        string.IsNullOrEmpty(key) ? Default : All.FirstOrDefault(s => s.Key == key) ?? Default;

    static PlayerSkin New(string key, string name, string group, string[] bg,
        string disc = "Vinyl", string? discColor = null, string? label = null, string? glow = null,
        WaveKind wave = WaveKind.None, string? waveColor = null, bool waveDense = false,
        bool stars = false, string? starsShape = null, string glyph = "", string? glyphColor = null,
        bool fullCover = false, bool light = false) =>
        new()
        {
            Key = key, Name = name, Group = group, Bg = bg, Disc = disc, DiscColor = discColor,
            Label = label, Glow = glow, Wave = wave, WaveColor = waveColor, WaveDense = waveDense,
            Stars = stars, StarsShape = starsShape, Glyph = glyph, GlyphColor = glyphColor,
            FullCover = fullCover, Light = light,
        };
}

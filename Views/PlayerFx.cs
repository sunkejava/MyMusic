using System;
using System.Windows;
using System.Windows.Media;
using MyQQMusic.Services;

namespace MyQQMusic.Views;

/// <summary>
/// 播放页星空层：随机星点 + 可选星座连线图形（归一化坐标缩放到控件区域）。
/// </summary>
public class StarFieldLayer : FrameworkElement
{
    string? _points, _edges;
    Color _color = Color.FromRgb(0xFF, 0xD6, 0x66);
    bool _hasShape;

    public void SetPattern(string? points, string? edges, Color color, bool hasShape)
    {
        _points = points; _edges = edges; _color = color; _hasShape = hasShape;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;

        // 背景随机星点（固定种子，布局稳定）
        var rnd = new Random(42);
        for (int i = 0; i < 90; i++)
        {
            double x = rnd.NextDouble() * w, y = rnd.NextDouble() * h;
            double r = 0.6 + rnd.NextDouble() * 1.4;
            byte a = (byte)(30 + rnd.NextDouble() * 90);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF)), null,
                new Point(x, y), r, r);
        }

        if (!_hasShape || _points == null) return;

        // 星座点与连线
        var pts = ParsePoints(_points, w, h);
        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, _color.R, _color.G, _color.B)), 1.2);
        linePen.Freeze();
        if (_edges != null)
        {
            foreach (var e in _edges.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var ab = e.Split('-');
                if (ab.Length != 2 || !int.TryParse(ab[0], out int a) || !int.TryParse(ab[1], out int b)) continue;
                if (a < 0 || a >= pts.Length || b < 0 || b >= pts.Length) continue;
                dc.DrawLine(linePen, pts[a], pts[b]);
            }
        }
        var starBrush = new SolidColorBrush(_color);
        starBrush.Freeze();
        var haloBrush = new SolidColorBrush(Color.FromArgb(0x33, _color.R, _color.G, _color.B));
        haloBrush.Freeze();
        foreach (var p in pts)
        {
            dc.DrawEllipse(haloBrush, null, p, 7, 7);
            dc.DrawEllipse(starBrush, null, p, 2.2, 2.2);
        }
    }

    static Point[] ParsePoints(string points, double w, double h)
    {
        // 图形绘制在中央偏上区域，避开唱片区
        var raw = points.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new Point[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            var xy = raw[i].Split(',');
            double x = double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture);
            double y = double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
            result[i] = new Point(w * (0.12 + x * 0.76), h * (0.06 + y * 0.5));
        }
        return result;
    }
}

/// <summary>
/// 播放页音波氛围层：按类型绘制随时间流动的伪频谱装饰（非真实 FFT，观感向）。
/// </summary>
public class WaveFxLayer : FrameworkElement
{
    public WaveKind Kind { get; set; }
    public Color Color { get; set; } = Color.FromRgb(0x60, 0xA0, 0xFF);
    public bool Dense { get; set; }
    public bool Active { get; set; }

    double _t;
    readonly Random _rnd = new(7);

    /// <summary>播放中由渲染回调驱动推进</summary>
    public void Tick(double dt)
    {
        _t += dt;
        InvalidateVisual();
    }

    public void Reset() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20 || Kind == WaveKind.None) return;
        switch (Kind)
        {
            case WaveKind.Bars: DrawBars(dc, w, h); break;
            case WaveKind.CircleBars: DrawCircleBars(dc, w, h); break;
            case WaveKind.Particles: DrawParticles(dc, w, h); break;
            case WaveKind.Planet: DrawPlanet(dc, w, h); break;
            case WaveKind.Waves: DrawWaves(dc, w, h); break;
        }
    }

    double Noise(int i) => Active
        ? 0.5 + 0.5 * (0.6 * Math.Sin(_t * 2.1 + i * 1.7) + 0.4 * Math.Sin(_t * 0.9 + i * 0.53))
        : 0.06;

    void DrawBars(DrawingContext dc, double w, double h)
    {
        int n = Dense ? 72 : 48;
        double bw = w / n;
        var brush = new SolidColorBrush(Color.FromArgb(0xCC, Color.R, Color.G, Color.B));
        brush.Freeze();
        for (int i = 0; i < n; i++)
        {
            double bh = Math.Max(3, Noise(i) * h * 0.55);
            var rect = new Rect(i * bw + bw * 0.18, h - bh, bw * 0.64, bh);
            dc.DrawRoundedRectangle(brush, null, rect, bw * 0.3, bw * 0.3);
        }
    }

    void DrawCircleBars(DrawingContext dc, double w, double h)
    {
        var center = new Point(w / 2, h / 2);
        double inner = Math.Min(w, h) * 0.385;
        int n = Dense ? 96 : 72;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB3, Color.R, Color.G, Color.B)), Dense ? 2 : 3)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        for (int i = 0; i < n; i++)
        {
            double ang = i * Math.PI * 2 / n;
            double len = (Dense ? 26 : 12) + Noise(i) * (Dense ? 110 : 46);
            double dx = Math.Cos(ang), dy = Math.Sin(ang);
            dc.DrawLine(pen,
                new Point(center.X + dx * inner, center.Y + dy * inner),
                new Point(center.X + dx * (inner + len), center.Y + dy * (inner + len)));
        }
    }

    void DrawParticles(DrawingContext dc, double w, double h)
    {
        int n = Dense ? 140 : 70;
        var rnd = _rnd;
        for (int i = 0; i < n; i++)
        {
            // 每个粒子固定基准位置，随时间向上缓慢漂移并循环，亮度闪烁
            double bx = (i * 0.618 % 1.0) * w;
            double by = ((i * 0.357 % 1.0) * h - _t * (8 + i % 13)) % h;
            if (by < 0) by += h;
            double tw = Active ? 0.35 + 0.65 * Math.Abs(Math.Sin(_t * 1.3 + i)) : 0.25;
            double r = 0.8 + (i % 5) * 0.5;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(0xE0 * tw), Color.R, Color.G, Color.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(bx, by), r, r);
        }
    }

    void DrawPlanet(DrawingContext dc, double w, double h)
    {
        var c = new Point(w / 2, h / 2);
        var fill = new SolidColorBrush(Color.FromArgb(0xE6, Color.R, Color.G, Color.B));
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, Color.R, Color.G, Color.B)), 1.4);
        fill.Freeze(); ringPen.Freeze();

        // 环绕音波短棒
        var barPen = new Pen(new SolidColorBrush(Color.FromArgb(0x99, Color.R, Color.G, Color.B)), 2)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        barPen.Freeze();
        for (int i = 0; i < 32; i++)
        {
            double ang = i * Math.PI / 16;
            double r0 = 118, len = 6 + Noise(i) * 34;
            double dx = Math.Cos(ang), dy = Math.Sin(ang);
            dc.DrawLine(barPen, new Point(c.X + dx * r0, c.Y + dy * r0),
                new Point(c.X + dx * (r0 + len), c.Y + dy * (r0 + len)));
        }

        // 行星球体 + 光环 + 环绕小卫星
        dc.DrawEllipse(fill, null, c, 30, 30);
        dc.PushTransform(new RotateTransform(-18, c.X, c.Y));
        dc.DrawEllipse(null, ringPen, c, 92, 30);
        double moonAng = _t * 0.9;
        dc.DrawEllipse(fill, null,
            new Point(c.X + 92 * Math.Cos(moonAng), c.Y + 30 * Math.Sin(moonAng)), 5, 5);
        dc.Pop();
    }

    void DrawWaves(DrawingContext dc, double w, double h)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            byte alpha = (byte)(0x80 - layer * 0x22);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, Color.R, Color.G, Color.B)), 2.2 - layer * 0.5);
            pen.Freeze();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                double baseY = h * (0.62 + layer * 0.13);
                double amp = 16 - layer * 4;
                double phase = _t * (1.1 + layer * 0.4) + layer * 2;
                ctx.BeginFigure(new Point(0, baseY + amp * Math.Sin(phase)), false, false);
                for (double x = 4; x <= w; x += 4)
                    ctx.LineTo(new Point(x, baseY + amp * Math.Sin(phase + x * 0.018)), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }
}

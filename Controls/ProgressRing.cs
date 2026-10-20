using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PdfLibrary.Controls;

/// <summary>
/// Кольцо хода фонового чтения в панели разделов (макет: «Фоновое чтение: 118 из 149»): серая
/// дорожка и акцентная дуга доли от верхней точки по часовой стрелке.
/// </summary>
public sealed class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Track));

    public static readonly StyledProperty<IBrush?> ArcProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(Arc));

    static ProgressRing() => AffectsRender<ProgressRing>(ValueProperty, TrackProperty, ArcProperty);

    /// <summary>Доля от 0 до 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IBrush? Arc
    {
        get => GetValue(ArcProperty);
        set => SetValue(ArcProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        const double thickness = 2.5;
        var r = size / 2 - thickness;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (Track is { } track)
            context.DrawEllipse(null, new Pen(track, thickness), c, r, r);

        var value = Math.Clamp(Value, 0, 1);
        if (Arc is not { } arc || value <= 0) return;
        if (value >= 0.999)
        {
            context.DrawEllipse(null, new Pen(arc, thickness), c, r, r);
            return;
        }

        var angle = value * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end   = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(arc, thickness, lineCap: PenLineCap.Round), geometry);
    }
}

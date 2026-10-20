using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using PdfLibrary.Core.Documents;

namespace PdfLibrary.Views.Reader;

/// <summary>
/// Лента листов документа: страницы друг под другом на сером фоне, как в макете, с подсветкой
/// найденного. Рисует себя сама, а не набором контролов на страницу: размеры всех листов
/// известны заранее, поэтому место каждого считается точно — переход к странице и к совпадению
/// ставит прокрутку ровно туда, где лист нарисован, без оценок высоты, как у списков с
/// виртуализацией. Рисуются только видимые листы; картинки заказываются у
/// <see cref="ReaderViewModel.RequestPage"/> по мере прокрутки.
/// </summary>
public sealed class PageCanvas : Control
{
    public const double Gap = 16;
    public const double Pad = 16;

    private ReaderViewModel? _vm;
    private double[] _tops = [];
    private ScrollViewer? _scroll;

    public ReaderViewModel? Model
    {
        get => _vm;
        set
        {
            if (_vm is not null) _vm.PagesChanged -= InvalidateVisual;
            _vm = value;
            if (_vm is not null) _vm.PagesChanged += InvalidateVisual;
            Relayout();
        }
    }

    /// <summary>Масштаб листа: экранных точек на пункт PDF.</summary>
    public double Scale => (_vm?.Zoom ?? 1) * ReaderViewModel.PointsToDip;

    /// <summary>Пересчитать места листов (открыт документ, сменился масштаб).</summary>
    public void Relayout()
    {
        var sizes = _vm?.Sizes ?? [];
        _tops = new double[sizes.Count];
        var y = Pad;
        for (var i = 0; i < sizes.Count; i++)
        {
            _tops[i] = y;
            y += sizes[i].Height * Scale + Gap;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Верх листа <paramref name="page"/> в координатах ленты.</summary>
    public double TopOf(int page) => page >= 0 && page < _tops.Length ? _tops[page] : 0;

    /// <summary>Лист под вертикальной координатой ленты.</summary>
    public int PageAt(double y)
    {
        if (_tops.Length == 0) return 0;
        var i = Array.BinarySearch(_tops, y);
        if (i < 0) i = ~i - 1;
        return Math.Clamp(i, 0, _tops.Length - 1);
    }

    /// <summary>Левый край листа: листы центрируются по ширине ленты.</summary>
    public double LeftOf(int page) =>
        _vm is { } vm && page < vm.Sizes.Count ? Math.Max(Pad, (Bounds.Width - vm.Sizes[page].Width * Scale) / 2) : Pad;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_vm is null || _vm.Sizes.Count == 0) return new Size(0, 0);
        var width  = _vm.Sizes.Max(s => s.Width) * Scale + 2 * Pad;
        var last   = _vm.Sizes.Count - 1;
        var height = _tops[last] + _vm.Sizes[last].Height * Scale + Pad;
        return new Size(Math.Max(width, double.IsFinite(availableSize.Width) ? availableSize.Width : 0), height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is not null) _scroll.ScrollChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_vm is not { } vm || vm.Sizes.Count == 0) return;

        // Видимая полоса ленты — по прокрутке; без нее рисуем все (так бывает только в стенде).
        var top    = _scroll?.Offset.Y ?? 0;
        var bottom = top + (_scroll?.Viewport.Height ?? Bounds.Height);
        var first  = PageAt(top);
        var scale  = Scale;
        var dpi    = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        var page   = Res.Brush("BrushPage") ?? Brushes.White;
        var hit    = Res.Brush("BrushPageHit");
        var cur    = Res.Brush("BrushPageHitCurrent");
        var line   = Res.Brush("BrushPageHitOutline");
        var curPen = line is null ? null : new Pen(line, 2);
        var shadow = new BoxShadows(new BoxShadow { OffsetY = 1, Blur = 4, Color = Color.FromArgb(46, 0, 0, 0) });

        var current = vm.CurrentHit >= 0 && vm.CurrentHit < vm.Hits.Count ? vm.Hits[vm.CurrentHit] : null;

        for (var i = first; i < vm.Sizes.Count && _tops[i] <= bottom; i++)
        {
            var size = vm.Sizes[i];
            var rect = new Rect(LeftOf(i), _tops[i], size.Width * scale, size.Height * scale);
            if (rect.Bottom < top) continue;

            context.DrawRectangle(page, null, new RoundedRect(rect), shadow);
            if (vm.PageImage(i) is { } image)
                context.DrawImage(image, new Rect(image.Size), rect);
            vm.RequestPage(i, (int)Math.Ceiling(rect.Width * dpi));

            foreach (var h in vm.Hits)
            {
                if (h.Page != i) continue;
                var isCurrent = ReferenceEquals(h, current);
                foreach (var box in h.Boxes)
                {
                    var s = DocPageGeometry.ToScreen(box, size.Height, scale, padding: 0.6);
                    var r = new Rect(rect.X + s.X, rect.Y + s.Y, s.Width, s.Height);
                    context.DrawRectangle(isCurrent ? cur : hit, isCurrent ? curPen : null, r, 1, 1);
                }
            }
        }
    }
}

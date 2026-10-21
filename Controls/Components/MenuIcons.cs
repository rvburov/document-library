using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace PdfLibrary.Controls.Components;

// Общие иконки и заголовки для контекстных меню (единый стиль во всей программе). Иконка — обводочная, в стандарте сайдбара: Viewbox 18×18 поверх сетки 24×24.
public static class MenuIcons
{
    // Геометрии иконок — из общего словаря Assets/Styles/Icons.axaml по ключу x:Key.
    private static Geometry Icon(string key) =>
        Application.Current!.TryFindResource(key, out var res) && res is Geometry g
            ? g
            : throw new KeyNotFoundException($"Icon resource '{key}' not found in Icons.axaml");

    // Размер иконки (сторона Viewbox) — из тех же токенов Icons.axaml.
    private static double IconSize(string key) =>
        Application.Current!.TryFindResource(key, out var r) && r is double d ? d : 18;

    private static IBrush Red => Brush("BrushRed") ?? new SolidColorBrush(Color.Parse("#FF3B30"));

    // Стандартный цвет текста/иконки меню (BrushPrimaryText, fallback — черный).
    private static IBrush PrimaryText => Brush("BrushPrimaryText") ?? Brushes.Black;

    // Кисть текущей темы: меню строится кодом, и кисть берется у программы по ее теме.
    private static IBrush? Brush(string key) =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out var res)
            ? res as IBrush
            : null;

    /// <summary>Заголовок пункта: обводочная иконка стандартного цвета + текст.</summary>
    public static Control Header(string iconKey, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(StrokeIcon(Icon(iconKey), PrimaryText));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static Viewbox StrokeIcon(Geometry data, IBrush stroke) => new()
    {
        Width  = IconSize("IconSizeMedium"),
        Height = IconSize("IconSizeMedium"),
        VerticalAlignment = VerticalAlignment.Center,
        Child  = new Canvas
        {
            Width  = 24,
            Height = 24,
            Children =
            {
                new Path
                {
                    Data            = data,
                    Stroke          = stroke,
                    StrokeThickness = IconSize("IconStrokeMedium"),
                    StrokeLineCap   = PenLineCap.Round,
                    StrokeJoin      = PenLineJoin.Round,
                },
            },
        },
    };

    // Пункт выбора одного варианта из набора: у выбранного — галочка, у остальных на ее месте
    // пустое место той же ширины. Без распорки тексты пунктов съезжали бы друг относительно
    // друга, и список читался бы как рваный.
    public static Control ChoiceHeader(string text, bool selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(selected
            ? StrokeIcon(Icon("IconCheck"), PrimaryText)
            : new Border { Width = IconSize("IconSizeMedium") });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>
    /// Пункт без иконки, но с ее отступом — чтобы текст стоял в одну линию с соседями по меню.
    /// </summary>
    public static Control PlainHeader(string text) => ChoiceHeader(text, selected: false);

    // Сабменю по умолчанию (тема Fluent) ставит HorizontalOffset попапа ≈ -1 (локальное
    // значение шаблона, стилем не перебить) → блок наезжает на меню. После применения
    // шаблона находим Popup сабменю и сдвигаем вправо, чтобы он стоял отдельно.
    public static void FixSubmenuGap(MenuItem item) =>
        item.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Avalonia.Controls.Primitives.Popup>("PART_Popup") is { } popup)
                popup.HorizontalOffset = 6;
        };

    // Заголовок пункта удаления: иконка корзины + красный текст.
    public static Control DeleteHeader(string text = "Удалить")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(StrokeIcon(Icon("IconTrash"), Red));
        row.Children.Add(new TextBlock
        {
            Text              = text,
            Foreground        = Red,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }
}

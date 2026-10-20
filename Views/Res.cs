using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace PdfLibrary.Views;

/// <summary>
/// Ресурсы программы для кода: темы кнопок, иконки, кисти. Ищутся у самой программы, а не у
/// контрола: контрол, еще не встроенный в окно (конструктор экрана), своих ресурсов не видит, и
/// <c>FindResource</c> вернул бы «не задано». Кисть — текущей темы оформления.
/// </summary>
public static class Res
{
    private static object? Get(string key)
    {
        if (Application.Current is not { } app) return null;
        return app.TryFindResource(key, app.ActualThemeVariant, out var value) ? value : null;
    }

    public static ControlTheme? Theme(string key) => Get(key) as ControlTheme;

    public static Geometry? Icon(string key) => Get(key) as Geometry;

    public static IBrush? Brush(string key) => Get(key) as IBrush;
}

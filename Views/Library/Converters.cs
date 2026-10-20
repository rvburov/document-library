using Avalonia;
using Avalonia.Data.Converters;

namespace PdfLibrary.Views.Library;

/// <summary>Преобразователи для разметки ленты.</summary>
public static class Converters
{
    /// <summary>Число → отступ слева (строки дерева источников разной глубины).</summary>
    public static readonly IValueConverter LeftPadding =
        new FuncValueConverter<double, Thickness>(left => new Thickness(left, 0, 0, 0));
}

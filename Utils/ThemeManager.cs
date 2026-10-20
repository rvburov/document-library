using Avalonia;
using Avalonia.Styling;

namespace PdfLibrary.Utils;

/// <summary>
/// Тема оформления: светлая, темная или как в системе. Цвета живут в словарях тем
/// (Assets/Styles/Colors.axaml), поэтому смена темы — это смена варианта у программы, и окна
/// перекрашиваются на ходу. Листы PDF остаются светлыми в любой теме — как бумага.
/// </summary>
public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Auto => ThemeVariant.Default,
            _             => ThemeVariant.Light,
        };
    }
}

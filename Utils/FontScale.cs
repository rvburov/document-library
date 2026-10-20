using Avalonia;
using Avalonia.Controls;

namespace PdfLibrary.Utils;

/// <summary>
/// Живой размер шрифта — настройка «Размер шрифта» (мелкий, стандартный, крупный). Хранит
/// стандартные размеры из Typography.axaml и пишет в ресурсы программы «стандарт + прибавка»:
/// потребители на DynamicResource обновляются мгновенно. Карточки ленты подстраивают высоту
/// под шрифт сами (см. <c>CardMetrics</c>).
/// </summary>
public static class FontScale
{
    private static readonly string[] Keys =
        ["FontSizeSmall", "FontSizeBase", "FontSizeMedium", "FontSizeLargeReader", "FontSizeLarge", "FontSizeHero"];

    private static readonly Dictionary<string, double> Standard = [];

    /// <summary>Текущая прибавка в пикселях (0 — стандарт).</summary>
    public static int Delta { get; private set; }

    /// <summary>Размер изменился.</summary>
    public static event Action? Changed;

    /// <summary>Старт: запомнить стандарт и применить сохраненную прибавку. Зовется до создания окна.</summary>
    public static void Init()
    {
        var app = Application.Current!;
        foreach (var key in Keys)
            if (!Standard.ContainsKey(key))
                Standard[key] = app.TryFindResource(key, out var v) && v is double d ? d : 13;
        Delta = AppSettings.FontDelta;
        Write();
    }

    public static void Set(int delta)
    {
        if (delta == Delta) return;
        Delta = delta;
        AppSettings.FontDelta = delta;
        Write();
        Changed?.Invoke();
    }

    /// <summary>Размер с прибавкой — для текста, который строится в коде.</summary>
    public static double Size(string key) => (Standard.TryGetValue(key, out var v) ? v : 13) + Delta;

    private static void Write()
    {
        if (Application.Current is not { } app) return;
        foreach (var (key, size) in Standard) app.Resources[key] = size + Delta;
    }
}

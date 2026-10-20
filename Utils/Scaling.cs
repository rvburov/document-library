using Avalonia;
using Avalonia.Media;

namespace PdfLibrary.Utils;

/// <summary>
/// Масштаб всего интерфейса — настройка «Масштаб интерфейса» (90, 100, 110, 125 %). Применяется
/// как LayoutTransform к корню главного окна (окно слушает <see cref="ScaleChanged"/>) и к
/// всплывающим панелям — через ресурс программы <c>AppZoomTransform</c>.
/// </summary>
public static class Scaling
{
    /// <summary>Текущий масштаб (1.0 = 100 %).</summary>
    public static double Current { get; private set; } = AppSettings.ScaleDefault;

    /// <summary>Масштаб изменился — корню окна пора применить трансформацию.</summary>
    public static event Action<double>? ScaleChanged;

    /// <summary>Старт: прочитать сохраненный масштаб. Зовется до создания главного окна.</summary>
    public static void Load()
    {
        Current = AppSettings.Scale;
        PublishTransform();
    }

    /// <summary>Установить масштаб, сохранить и применить вживую.</summary>
    public static void Set(double value)
    {
        if (Math.Abs(value - Current) < 0.001) return;
        Current = value;
        AppSettings.Scale = value;
        PublishTransform();
        ScaleChanged?.Invoke(value);
    }

    private static void PublishTransform()
    {
        if (Application.Current is { } app)
            app.Resources["AppZoomTransform"] = new ScaleTransform(Current, Current);
    }
}

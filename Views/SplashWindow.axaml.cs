using Avalonia.Controls;
using PdfLibrary.Utils;

namespace PdfLibrary.Views;

/// <summary>
/// Заставка на время старта: пока в фоне открывается library.db (миграции), кеш прочитанного и
/// список документов. Показывается первой, поэтому запуск никогда не выглядит зависшим.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = "Версия " + AppSettings.AppVersion;
    }

    /// <summary>Текущий этап старта.</summary>
    public void SetStage(string text) => StageText.Text = text;

    /// <summary>Показать ошибку запуска вместо хода.</summary>
    public void SetError(string message)
    {
        Bar.IsVisible = false;
        StageText.Text = message;
        StageText.Foreground = Res.Brush("BrushRed");
    }
}

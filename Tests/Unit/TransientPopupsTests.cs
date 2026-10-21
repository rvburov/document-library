using System.Text.RegularExpressions;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Сторож правила «показал всплывающую панель — отметь ее в реестре».
///
/// Панель, о которой окно не знает, остается висеть поверх ЧУЖОЙ программы, когда человек
/// переключается в нее (на macOS popup живет отдельным окном и сам не гаснет). Проверяем
/// исходник: поймать это на живом экране можно только переключением в другую программу, а
/// забыть строку — одной правкой.
/// </summary>
public sealed class TransientPopupsTests
{
    // Toast: всплывашка-уведомление живет секунды, не забирает фокус и гаснет по своему
    // таймеру. PopupPlate: фабрика плашки — флайаут она только СОЗДАЕТ, а отмечает его тот, кто
    // показывает, там же, где вызывается ShowAt.
    private static readonly string[] Exempt = ["Toast.cs", "PopupPlate.cs"];

    [Fact]
    public void Every_flyout_is_registered_so_the_window_can_close_it()
    {
        // Показать панель можно двумя способами: собрать флайаут самому или взять готовую
        // плашку у фабрики. Оба обязаны отметиться в реестре.
        var opens   = new Regex(@"new (Menu)?Flyout\b|PopupPlate\.Filter\(");
        var missing = RepoFiles.Sources(RepoFiles.Root())
            .Where(f => !Exempt.Contains(Path.GetFileName(f.Path)))
            .Where(f => opens.IsMatch(f.Text) && !f.Text.Contains("TransientPopups.Track", StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();

        Assert.True(missing.Count == 0,
            "Панель показывается, но не отмечена в TransientPopups — окно не сможет закрыть ее " +
            "при потере фокуса, и на macOS она зависнет поверх другой программы:\n  " +
            string.Join("\n  ", missing));
    }

    /// <summary>Окно закрывает панели через общий реестр, а не обходом дерева по конкретному типу.</summary>
    [Fact]
    public void Window_closes_popups_through_the_registry()
    {
        var main = File.ReadAllText(Path.Combine(RepoFiles.Root(), "Views", "MainWindow.axaml.cs"));
        Assert.Contains("TransientPopups.CloseAll()", main);
    }
}

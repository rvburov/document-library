using System.Text.RegularExpressions;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Сторож правила «любой вызов в PDFium — через очередь <c>PdfGate</c>»
/// (<c>Core/Documents/PdfGate.cs</c>). PDFium не потокобезопасен, а рисуют им двое — миниатюры
/// ленты и панель чтения, — и вызов мимо очереди падает из недр нативной библиотеки без единой
/// понятной строки в журнале. Тестом такую гонку не поймать: она случается раз на тысячу
/// прогонов. Поэтому правило проверяется по тексту, как <see cref="NoNetworkTests"/>.
///
/// Проверки две:
/// <list type="bullet">
/// <item>объект PDFium, полученный из <c>PdfGate.Run</c>, не закрывается <c>using</c>: закрытие —
///       тоже вызов в PDFium, а <c>using</c> делает его уже ПОСЛЕ замка;</item>
/// <item>к PDFium напрямую обращаются только известные файлы. Новое место — повод проверить,
///       что оно идет через очередь, и дописать его сюда осознанно.</item>
/// </list>
/// </summary>
public sealed class PdfGateConventionTests
{
    /// <summary>
    /// Где PDFium зовется напрямую, и все эти места идут через очередь: источник страниц панели
    /// чтения и миниатюры ленты. Сама очередь упоминает PDFium в описании. Tests/ не сканируется:
    /// стенды там зовут PDFium сами.
    /// </summary>
    private static readonly string[] PdfiumFiles =
    [
        Path.Combine("Core", "Documents", "PdfGate.cs"),
        Path.Combine("Core", "Documents", "DocPageSource.cs"),
        Path.Combine("Core", "Documents", "DocThumbnail.cs"),
    ];

    // «using var x = PdfGate.Run(…)» и «using (var x = PdfGate.Run(…))».
    private static readonly Regex ClosedAfterGate =
        new(@"using\s*\(?\s*var\s+\w+\s*=\s*PdfGate\.Run\(", RegexOptions.Compiled);

    [Fact]
    public void Nothing_taken_from_the_gate_is_closed_after_it()
    {
        var offenders = Sources(RepoRoot())
            .Where(s => ClosedAfterGate.IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Объект PDFium взят под замком PdfGate, а закрывается using уже после него — мимо очереди. " +
            "Закрывайте его внутри PdfGate.Run (using внутри лямбды) или через PdfGate.Run(x.Dispose): " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Pdfium_is_called_only_from_known_files()
    {
        var callers = Sources(RepoRoot())
            .Where(s => s.Text.Contains("DocLib.Instance", StringComparison.Ordinal))
            .Select(s => s.Path)
            .Where(p => !PdfiumFiles.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(callers.Count == 0,
            "PDFium зовется из нового места. Проверьте, что каждый вызов, включая закрытие, идет " +
            "через PdfGate, и допишите файл в PdfiumFiles: " + string.Join(", ", callers));
    }

    private static string RepoRoot() => RepoFiles.Root();

    private static IEnumerable<(string Path, string Text)> Sources(string root) => RepoFiles.Sources(root);
}

using System.IO;
using PdfLibrary.Core.Documents;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// Разбор PDF (<c>Core/Documents/DocTextExtractor.cs</c>) — то, чем питаются поиск по
/// содержимому, страницы и форматы листов в карточке и название из свойств файла.
///
/// Тесты интеграционные, а не юнит: разборщик открывает настоящие файлы настоящей библиотекой, и
/// подменять это нечем — именно на чужих файлах и ломается разбор. Главное правило проверяется
/// отдельно: исключений метод не бросает НИКОГДА, потому что очередь обходит тысячи чужих файлов и
/// любой из них может оказаться битым.
/// </summary>
public sealed class DocTextExtractorTests : IDisposable
{
    private readonly string _dir;

    public DocTextExtractorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pl-doctext-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* занят — уберет система */ }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    // ── Что узнается заодно с текстом ────────────────────────────────────────

    [Fact]
    public void Pages_and_sheet_formats_are_counted_with_the_text()
    {
        var path = HandPdf.Write(_dir, "a4.pdf",
            new HandPdf.Sheet(HandPdf.Plain(40, 780, "Nasos centrobezhnyj")),
            new HandPdf.Sheet(HandPdf.Plain(40, 780, "Rukovodstvo")),
            new HandPdf.Sheet(HandPdf.Plain(40, 780, "Pasport")));

        var result = DocTextExtractor.Extract(path);

        Assert.Equal(DocTextStatus.Ok, result.Status);
        Assert.Equal(3, result.Pages);
        Assert.Equal("A4 × 3", result.Sheets);
        Assert.Equal("A4", result.Sheet);
    }

    /// <summary>Чертеж из листов разного формата: крупный первым, на плитку — самый крупный.</summary>
    [Fact]
    public void Mixed_formats_list_the_largest_sheet_first()
    {
        var path = HandPdf.Write(_dir, "drawing.pdf",
            new HandPdf.Sheet(HandPdf.Plain(40, 100, "Obshchij vid"), Width: 2384, Height: 1684),   // A1 альбомный
            new HandPdf.Sheet(HandPdf.Plain(40, 100, "Uzel A"), Width: 1191, Height: 842),          // A3 альбомный
            new HandPdf.Sheet(HandPdf.Plain(40, 100, "Uzel B"), Width: 842, Height: 1191));         // A3 книжный

        var result = DocTextExtractor.Extract(path);

        Assert.Equal("A1 × 1, A3 × 2", result.Sheets);
        Assert.Equal("A1", result.Sheet);
    }

    [Fact]
    public void A_meaningful_title_from_the_properties_is_kept()
    {
        var path = HandPdf.Write(_dir, "ABVG.301122.001 VO.pdf", "Nasosnaya stanciya. Chertezh obshchego vida",
            new HandPdf.Sheet(HandPdf.Plain(40, 780, "tekst")));

        Assert.Equal("Nasosnaya stanciya. Chertezh obshchego vida", DocTextExtractor.Extract(path).Title);
    }

    [Fact]
    public void A_title_made_by_the_printing_program_is_dropped()
    {
        var path = HandPdf.Write(_dir, "otchet.pdf", "Microsoft Word - otchet.docx",
            new HandPdf.Sheet(HandPdf.Plain(40, 780, "tekst")));

        Assert.Equal("", DocTextExtractor.Extract(path).Title);
    }

    /// <summary>Скан без текстового слоя: страницы и формат есть, текста нет — «скан, без текста».</summary>
    [Fact]
    public void A_scan_without_text_keeps_pages_and_format()
    {
        var path = HandPdf.Write(_dir, "scan.pdf", new HandPdf.Sheet(""), new HandPdf.Sheet(""));

        var result = DocTextExtractor.Extract(path);

        Assert.Equal(DocTextStatus.NoText, result.Status);
        Assert.Equal(2, result.Pages);
        Assert.Equal("A4 × 2", result.Sheets);
    }

    /// <summary>
    /// Лист, повернутый без поправки содержимого, и надпись штампа, идущая снизу вверх, попадают
    /// в индекс целыми словами: разбор идет в порядке потока содержимого, и направление строки
    /// ему безразлично. Страница панели чтения находит то же самое — см.
    /// <c>DocPageSourceTests.Sideways_text_comes_in_whole_words</c>.
    /// </summary>
    [Fact]
    public void Turned_and_sideways_pdf_text_is_indexed_in_whole_words()
    {
        var path = HandPdf.Write(_dir, "боком.pdf",
            new HandPdf.Sheet(HandPdf.Plain(40, 782, "Dogovor postavki 21.501"), 90),
            new HandPdf.Sheet(HandPdf.Upright(270, HandPdf.Full, 40, 60, "Vedomost listov"), 270),
            new HandPdf.Sheet(HandPdf.Rising(30, 100, "Inv podl 4711")));

        var result = DocTextExtractor.Extract(path);

        Assert.Equal(DocTextStatus.Ok, result.Status);
        Assert.Contains("Dogovor postavki 21.501", result.Text, StringComparison.Ordinal);
        Assert.Contains("Vedomost listov",         result.Text, StringComparison.Ordinal);
        Assert.Contains("Inv podl 4711",           result.Text, StringComparison.Ordinal);
    }

    // ── Отказы ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_size_limit_rejects_a_file_over_200_megabytes()
    {
        var path = Path_("heavy.pdf");
        using (var stream = File.Create(path)) stream.SetLength(200L * 1024 * 1024 + 1);
        Assert.Equal(DocTextStatus.TooBig, DocTextExtractor.Extract(path).Status);
    }

    [Fact]
    public void A_file_over_the_threshold_is_skipped()
    {
        var path = Path_("big.pdf");
        File.WriteAllBytes(path, new byte[2 * 1024 * 1024]);

        Assert.Equal(DocTextStatus.TooBig, DocTextExtractor.Extract(path, maxFileMb: 1).Status);
        // Порог 0 означает «без порога» — файл разбирается (и оказывается битым).
        Assert.Equal(DocTextStatus.Failed, DocTextExtractor.Extract(path, maxFileMb: 0).Status);
    }

    [Fact]
    public void Not_a_pdf_is_reported_without_touching_the_file()
    {
        // Файла на диске нет вовсе — расширение отсекается до всякого ввода-вывода.
        Assert.Equal(DocTextStatus.Unsupported, DocTextExtractor.Extract(Path_("chertezh.dwg")).Status);
    }

    [Fact]
    public void A_missing_file_fails_without_throwing()
    {
        Assert.Equal(DocTextStatus.Failed, DocTextExtractor.Extract(Path_("нет-такого.pdf")).Status);
    }

    [Fact]
    public void An_empty_path_fails_without_throwing()
    {
        Assert.Equal(DocTextStatus.Failed, DocTextExtractor.Extract("").Status);
        Assert.Equal(DocTextStatus.Failed, DocTextExtractor.Extract("   ").Status);
    }

    /// <summary>Битый PDF — обычный исход обхода чужой папки.</summary>
    [Fact]
    public void A_broken_document_fails_without_throwing()
    {
        var path = Path_("broken.pdf");
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02]);

        Assert.Equal(DocTextStatus.Failed, DocTextExtractor.Extract(path).Status);
    }

    [Fact]
    public void Cancellation_stops_reading_instead_of_marking_the_file_broken()
    {
        var path = HandPdf.Write(_dir, "cancel.pdf", new HandPdf.Sheet(HandPdf.Plain(40, 780, "tekst")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => DocTextExtractor.Extract(path, ct: cts.Token));
    }

    // ── Нормализация ─────────────────────────────────────────────────────────

    [Fact]
    public void Whitespace_is_collapsed_into_single_spaces()
    {
        Assert.Equal("а б в", DocTextExtractor.Normalize("  а\n\n\tб    в  "));
    }

    /// <summary>
    /// Управляющие символы вычищаются — в том числе те, которыми сниппет помечает совпадения.
    /// Иначе содержимое документа могло бы подделать маркер и разъехаться с разбором.
    /// </summary>
    [Fact]
    public void Control_characters_are_stripped()
    {
        var raw = "текст" + '\u0002' + "подделка" + '\u0003' + "конец";

        var clean = DocTextExtractor.Normalize(raw);

        Assert.DoesNotContain('\u0002', clean);
        Assert.DoesNotContain('\u0003', clean);
        Assert.Equal("текст подделка конец", clean);
    }

    [Fact]
    public void Normalize_handles_empty_input()
    {
        Assert.Equal("", DocTextExtractor.Normalize(null));
        Assert.Equal("", DocTextExtractor.Normalize(""));
        Assert.Equal("", DocTextExtractor.Normalize("   "));
    }

    [Fact]
    public void Normalize_caps_the_text_length()
    {
        var huge = new string('а', DocTextExtractor.MaxChars + 1000);

        Assert.Equal(DocTextExtractor.MaxChars, DocTextExtractor.Normalize(huge).Length);
    }
}

using System.IO;
using System.Text;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PdfLibrary.Core.Documents;

/// <summary>Чем закончился разбор документа. Число пишется в кеш — порядок менять нельзя.</summary>
public enum DocTextStatus
{
    /// <summary>Текст извлечен.</summary>
    Ok = 0,
    /// <summary>PDF открылся, но текста в нем нет — обычно скан без текстового слоя.</summary>
    NoText = 1,
    /// <summary>Не PDF. В программе такие файлы не встречаются: источники отдают только PDF,
    /// значение оставлено, чтобы числа в кеше не сдвигались.</summary>
    Unsupported = 2,
    /// <summary>Файл не читается: битый, занят, защищен паролем, нет прав, пропал с диска.</summary>
    Failed = 3,
    /// <summary>Файл тяжелее порога — намеренно не разбирался.</summary>
    TooBig = 4,
}

/// <summary>
/// Результат разбора: статус, текст (пустой для всего, кроме <see cref="DocTextStatus.Ok"/>) и
/// то, что про документ узнается заодно.
///
/// Страницы, форматы листов и название считаются ЗАОДНО: документ в этот момент уже открыт, и
/// отдельного прохода они не стоят. Поэтому у прочитанных документов они есть, а у
/// непрочитанных не бывает, и карточка обещает ровно то, что знает.
/// </summary>
/// <param name="Pages">Сколько страниц в документе всего.</param>
/// <param name="Title">Название из свойств PDF, если оно осмысленное (<see cref="DocTitle.FromInfo"/>);
/// пустая строка — названия нет, карточка покажет имя файла.</param>
/// <param name="Sheets">Форматы листов: «A4 × 48», «A1 × 1, A3 × 2» (<see cref="SheetFormats"/>).</param>
/// <param name="Sheet">Самый крупный формат — для плитки карточки: «A1».</param>
public readonly record struct DocText(DocTextStatus Status, string Text, int Pages = 0,
                                      string Title = "", string Sheets = "", string Sheet = "")
{
    public static DocText Of(DocTextStatus status) => new(status, "");
}

/// <summary>
/// Извлечение текста PDF для поиска по содержимому. Чистая логика без UI и без базы: на входе
/// путь, на выходе строка. Кеширование, очередь и прогресс — дело
/// <c>Database/DocIndex/DocIndexService.cs</c>.
///
/// Разбор чужого файла — это выполнение чужого кода в парсере, поэтому формат один: PDF. OCR нет:
/// PDF-скан без текстового слоя честно получает <see cref="DocTextStatus.NoText"/> и больше не
/// тревожится.
///
/// Все ограничители — здесь, и все они про одно: один документ не должен съесть столько памяти
/// и времени, что встанет очередь. Чертеж на 2000 страниц и альбом на сто мегабайт —
/// нормальные файлы в инженерной папке, и наткнуться на них программа обязана без последствий.
/// </summary>
public static class DocTextExtractor
{
    /// <summary>Сколько страниц PDF разбираем на текст. Дальше текст для поиска все равно не нужен.</summary>
    public const int MaxPdfPages = 300;

    /// <summary>Сколько страниц обходим ради форматов листов. Размер страницы читается без ее
    /// содержимого, но у тома на десятки тысяч страниц и это — секунды.</summary>
    public const int MaxSheetPages = 5000;

    /// <summary>Потолок извлеченного текста в символах (~4 МБ) — дальше обрезаем.</summary>
    public const int MaxChars = 4_000_000;

    /// <summary>Порог размера файла по умолчанию, МБ: тяжелее — не разбираем вовсе.</summary>
    public const int DefaultMaxFileMb = 200;

    /// <summary>Тяжелее ли файл порога; 0 и меньше — порога нет.</summary>
    public static bool TooBig(long length, int maxFileMb) =>
        maxFileMb > 0 && length > (long)maxFileMb * 1024 * 1024;

    /// <summary>Расширение файла без точки в нижнем регистре; «» — расширения нет.</summary>
    public static string ExtensionOf(string path)
    {
        var name = path ?? "";
        var cut  = name.LastIndexOfAny(['/', '\\']);
        if (cut >= 0) name = name[(cut + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].ToLowerInvariant() : "";
    }

    /// <summary>PDF ли это (по расширению, файл не трогаем).</summary>
    public static bool IsPdf(string path) => ExtensionOf(path) == "pdf";

    /// <summary>
    /// Редакция разбора. Кеш помнит, какой редакцией прочитан документ, и перепись считает
    /// прочитанное прежней редакцией ИЗМЕНЕННЫМ — следующее чтение его перечитает. Без этого
    /// улучшенный разбор доходил бы только до документов, которые с тех пор правили.
    ///
    /// Поднимать — когда разбор начинает давать ДРУГОЙ результат, а не когда он просто стал
    /// быстрее: подъем стоит перечитывания всех документов.
    /// </summary>
    public static int RevisionOf(string path) => 0;

    /// <summary>
    /// Разобрать документ. Ошибки файла становятся статусом; отмена выходит исключением,
    /// чтобы очередь остановилась, а не записала отмененный разбор как испорченный файл.
    /// </summary>
    /// <param name="maxFileMb">Порог размера; 0 и меньше — порога нет.</param>
    public static DocText Extract(string path, int maxFileMb = DefaultMaxFileMb,
                                  CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return DocText.Of(DocTextStatus.Failed);
        if (!IsPdf(path)) return DocText.Of(DocTextStatus.Unsupported);

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return DocText.Of(DocTextStatus.Failed);
            if (TooBig(info.Length, maxFileMb)) return DocText.Of(DocTextStatus.TooBig);

            var pdf  = FromPdf(path, ct);
            var text = Normalize(pdf.Text);
            // Страницы, форматы и название сохраняем и у скана без текстового слоя: страницы у
            // него есть, а текста нет, и «12 стр. · A4» в карточке — правда, которая помогает
            // узнать документ.
            return pdf with { Status = text.Length == 0 ? DocTextStatus.NoText : DocTextStatus.Ok, Text = text };
        }
        catch (OperationCanceledException) { throw; }
        catch { return DocText.Of(DocTextStatus.Failed); }
    }

    // ── Разбор ────────────────────────────────────────────────────────────────

    // Число страниц — сколько их в документе ВСЕГО, не сколько разобрано: разбор упирается в
    // MaxPdfPages, а карточка показывает настоящий объем документа.
    private static DocText FromPdf(string path, CancellationToken ct)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(path);
        var total  = doc.NumberOfPages;
        var sheets = new SheetFormats();

        for (var i = 1; i <= total && i <= MaxSheetPages; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var page = doc.GetPage(i);
                var box  = page.CropBox.Bounds;
                sheets.Add(box.Width, box.Height);

                // ContentOrderTextExtractor собирает текст в порядке содержимого, а не в порядке
                // объектов страницы, иначе куски строк чертежа приезжали бы вперемешку.
                if (i <= MaxPdfPages && sb.Length < MaxChars)
                    sb.Append(ContentOrderTextExtractor.GetText(page)).Append('\n');
            }
            catch (OperationCanceledException) { throw; }
            catch { /* битая страница не должна отменять весь документ */ }
        }

        string title;
        try { title = DocTitle.FromInfo(doc.Information.Title, Path.GetFileNameWithoutExtension(path)); }
        catch { title = ""; }

        return new DocText(DocTextStatus.Ok, sb.ToString(), total, title, sheets.Summary, sheets.Largest);
    }

    /// <summary>
    /// Схлопывает пробелы и переводы строк: в индексе важны слова, а не верстка, а лишние
    /// пробелы PDF-извлечения раздували бы кеш вдвое. Заодно режет по <see cref="MaxChars"/>.
    /// Управляющие символы уходят тоже — на этом держатся маркеры подсветки сниппета
    /// (<c>DocTextIndex.HitOpen</c>), их нельзя подделать содержимым.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        var sb    = new StringBuilder(Math.Min(raw.Length, MaxChars));
        var space = true;   // true в начале — ведущие пробелы не пишем
        foreach (var ch in raw)
        {
            if (sb.Length >= MaxChars) break;
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!space) { sb.Append(' '); space = true; }
                continue;
            }
            sb.Append(ch);
            space = false;
        }
        return sb.ToString().TrimEnd();
    }
}

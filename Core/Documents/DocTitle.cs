namespace PdfLibrary.Core.Documents;

/// <summary>
/// Название документа для карточки. У инженерных PDF имя файла — обычно обозначение
/// («АБВГ.301122.001 ВО.pdf»), а человеческое название лежит в свойствах файла («Насосная
/// станция. Чертеж общего вида»). Но свойства часто заполняет не человек, а программа, которая
/// сохранила PDF: «Microsoft Word - Документ1.docx», «Untitled», имя исходного файла. Такое
/// название хуже имени файла, и карточка его не показывает.
/// </summary>
public static class DocTitle
{
    /// <summary>Длиннее этого — не название, а абзац текста, попавший в свойства.</summary>
    public const int MaxLength = 300;

    // Названия, которые программы ставят сами, — сравниваются после свертки регистра.
    private static readonly string[] Placeholders =
    [
        "untitled", "untitled document", "document", "документ", "без названия", "без имени",
        "title", "заголовок", "noname", "none", "new document", "новый документ",
    ];

    // Начала, по которым узнается название, собранное программой печати из имени исходного файла.
    private static readonly string[] MachinePrefixes =
    [
        "microsoft word -", "microsoft excel -", "microsoft powerpoint -", "microsoft visio -",
        "img_", "scan_", "скан_", "file:",
    ];

    // Расширения исходных файлов: «Отчет.docx» в свойствах — это имя файла, а не название.
    private static readonly string[] SourceExtensions =
    [
        ".doc", ".docx", ".xls", ".xlsx", ".dwg", ".dxf", ".pdf", ".rtf", ".odt", ".txt",
        ".vsd", ".vsdx", ".ppt", ".pptx", ".tif", ".tiff", ".jpg", ".png", ".cdw", ".spw", ".frw",
    ];

    /// <summary>
    /// Название из свойств PDF, если оно осмысленное; иначе пустая строка — тогда карточка
    /// показывает имя файла. <paramref name="fileName"/> — имя файла без расширения: название,
    /// которое с ним совпадает, ничего не добавляет.
    /// </summary>
    public static string FromInfo(string? infoTitle, string? fileName)
    {
        var title = Clean(infoTitle);
        if (title.Length < 4 || title.Length > MaxLength) return "";
        if (title.Count(char.IsLetter) < 3) return "";

        var folded = title.ToLowerInvariant();
        if (Array.IndexOf(Placeholders, folded) >= 0) return "";
        if (MachinePrefixes.Any(p => folded.StartsWith(p, StringComparison.Ordinal))) return "";
        if (SourceExtensions.Any(e => folded.EndsWith(e, StringComparison.Ordinal))) return "";
        if (folded.Contains('\\') || folded.Contains(":/") || folded.StartsWith('/')) return "";

        var name = Clean(fileName);
        if (name.Length > 0 && string.Equals(Simplify(title), Simplify(name), StringComparison.OrdinalIgnoreCase))
            return "";

        return title;
    }

    // Пробелы схлопнуты, управляющие символы и обрамление убраны.
    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var chars = raw.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                     .Trim('"', '\'', '«', '»', ' ');
    }

    // Для сравнения с именем файла: разделители имени файла и пробелы — одно и то же.
    private static string Simplify(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}

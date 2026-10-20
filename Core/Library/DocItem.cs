using PdfLibrary.Core.Documents;

namespace PdfLibrary.Core.Library;

/// <summary>
/// Документ в памяти — то, из чего собирается карточка ленты: строка library.db плюс то, что
/// про файл знает кеш прочитанного. Лента фильтрует и сортирует эти объекты в памяти: десятки
/// тысяч документов — это мегабайты, а ответ мгновенный, без похода в базу на каждую галочку.
///
/// Поля меняются только в UI-потоке (<see cref="LibraryService"/> переносит туда результаты
/// фоновых работ), поэтому замков нет.
/// </summary>
public sealed class DocItem
{
    public int Id { get; init; }
    public int SourceId { get; set; }

    /// <summary>Путь внутри источника через <c>/</c>: «КД/Насосы/Схема.pdf».</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Полный путь файла на диске.</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Имя файла с расширением: «ЦНС-38-44_РЭ.pdf».</summary>
    public string FileName { get; set; } = "";

    /// <summary>Папка внутри источника через <c>/</c>; пусто — файл лежит в корне источника.</summary>
    public string Folder { get; set; } = "";

    public string DocType { get; set; } = "";
    public long Size { get; set; }
    public long Mtime { get; set; }
    public DateTime FoundAt { get; set; }

    /// <summary>Файла нет в папке — карточка с пометкой «файл не найден».</summary>
    public bool Missing { get; set; }

    // ── Из кеша прочитанного ──────────────────────────────────────────────────

    public DocTextState TextState { get; set; } = DocTextState.Unknown;

    /// <summary>Прочитан прежде, но файл с тех пор изменился — «изменен · перечитать».</summary>
    public bool Changed { get; set; }

    public int Pages { get; set; }

    /// <summary>Форматы листов: «A1 × 1, A3 × 2».</summary>
    public string Sheets { get; set; } = "";

    /// <summary>Самый крупный формат: «A1».</summary>
    public string Sheet { get; set; } = "";

    /// <summary>Название из свойств PDF; пусто — нет.</summary>
    public string InfoTitle { get; set; } = "";

    /// <summary>Метки документа (Id меток).</summary>
    public HashSet<int> Tags { get; } = [];

    /// <summary>Название карточки: из свойств PDF, если оно осмысленное, иначе имя файла без расширения.</summary>
    public string Title => InfoTitle.Length > 0 ? InfoTitle : NameWithoutExtension;

    public string NameWithoutExtension =>
        FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? FileName[..^4] : FileName;

    /// <summary>Скан без текстового слоя.</summary>
    public bool IsScan => TextState == DocTextState.NoText;

    /// <summary>Не прочитан (и не менялся после чтения) — серый чип «не прочитан».</summary>
    public bool IsUnread => !Missing && TextState == DocTextState.NotRead && !Changed;
}

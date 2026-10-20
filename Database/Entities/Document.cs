namespace PdfLibrary.Database.Entities;

/// <summary>
/// Документ — PDF-файл, найденный в папке источника. Строка живет, пока файл есть в папке, и
/// какое-то время после: файл, пропавший с диска, остается в ленте с пометкой «файл не найден» —
/// так видно, что документ удалили или переименовали, и метки на нем не теряются, если файл
/// вернется на место.
///
/// Здесь только то, что известно без чтения файла. Прочитанное — текст, страницы, форматы
/// листов, название из свойств PDF — лежит в кеше <c>library-cache.db</c>
/// (<c>DocTextIndex</c>): его можно очистить и прочитать заново, а эту таблицу — нет.
/// </summary>
public class Document
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public Source? Source { get; set; }

    /// <summary>Путь внутри папки источника, разделитель — <c>/</c>: «КД/Насосы/Схема.pdf».</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Размер файла в байтах при последней проверке папки.</summary>
    public long Size { get; set; }

    /// <summary>Время правки файла при последней проверке (тики UTC).</summary>
    public long Mtime { get; set; }

    /// <summary>Тип документа (<c>DocTypes.Of</c>) — по имени файла и папке, без чтения файла.</summary>
    public string DocType { get; set; } = "";

    /// <summary>Когда файл впервые найден (UTC).</summary>
    public DateTime FoundAt { get; set; } = DateTime.UtcNow;

    /// <summary>С какого времени файла нет в папке (UTC); <c>null</c> — файл на месте.</summary>
    public DateTime? MissingSince { get; set; }

    public List<DocumentTag> Tags { get; set; } = [];
}

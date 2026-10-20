namespace PdfLibrary.Database.Entities;

/// <summary>
/// Источник — папка с PDF на диске или в сети. Подпапки входят в него, файлы остаются на своих
/// местах: библиотека хранит только путь и то, что про файлы узнала.
/// </summary>
public class Source
{
    public int Id { get; set; }

    /// <summary>Название в боковой панели. Стандартно — имя папки.</summary>
    public string Name { get; set; } = "";

    /// <summary>Полный путь к папке — локальный или сетевой (<c>\\srv-01\archive</c>).</summary>
    public string Path { get; set; } = "";

    /// <summary>Группа источников; <c>null</c> — источник стоит в боковой панели сам по себе.</summary>
    public int? GroupId { get; set; }
    public SourceGroup? Group { get; set; }

    /// <summary>Порядок в боковой панели (внутри группы или среди источников без группы).</summary>
    public int Position { get; set; }

    /// <summary>Галочка в боковой панели: документы источника показываются в ленте.</summary>
    public bool Shown { get; set; } = true;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда папка последний раз проверена целиком (UTC).</summary>
    public DateTime? CheckedAt { get; set; }

    /// <summary>С какого времени папка недоступна (UTC); <c>null</c> — доступна.</summary>
    public DateTime? UnavailableSince { get; set; }

    public List<Document> Documents { get; set; } = [];
}

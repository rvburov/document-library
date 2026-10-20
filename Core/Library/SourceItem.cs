namespace PdfLibrary.Core.Library;

/// <summary>Источник в памяти: строка боковой панели и строка списка «Источники и чтение».</summary>
public sealed class SourceItem
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int? GroupId { get; set; }
    public int Position { get; set; }

    /// <summary>Галочка в боковой панели.</summary>
    public bool Shown { get; set; } = true;

    public DateTime? CheckedAt { get; set; }

    /// <summary>С какого времени папка недоступна (UTC); <c>null</c> — доступна.</summary>
    public DateTime? UnavailableSince { get; set; }

    /// <summary>Идет первая проверка папки — документов еще нет, в панели «читаю…».</summary>
    public bool FirstScan { get; set; }

    /// <summary>Сколько PDF в источнике (без пропавших с диска).</summary>
    public int Count { get; set; }
}

/// <summary>Группа источников в памяти.</summary>
public sealed class GroupItem
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool Expanded { get; set; } = true;
}

/// <summary>Метка в памяти.</summary>
public sealed class TagItem
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5E5CE6";
    public int Position { get; set; }
}

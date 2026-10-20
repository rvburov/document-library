namespace PdfLibrary.Database.Entities;

/// <summary>
/// Метка — ярлык для документов: статус («Действующий», «На согласовании»), объект, что угодно.
/// Ставится выбранным документам из выпадающего списка с галочками; в боковой панели метка —
/// фильтр ленты.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Цвет точки — из набора окна «Новая метка», строкой <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = "#5E5CE6";

    /// <summary>Порядок в боковой панели.</summary>
    public int Position { get; set; }

    public List<DocumentTag> Documents { get; set; } = [];
}

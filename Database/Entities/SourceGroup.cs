namespace PdfLibrary.Database.Entities;

/// <summary>
/// Группа источников — несколько папок под одним названием в боковой панели. Папки на диске не
/// меняются; группа только собирает их строки и дает одну галочку на всех.
/// </summary>
public class SourceGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Порядок среди групп боковой панели.</summary>
    public int Position { get; set; }

    /// <summary>Группа развернута в боковой панели.</summary>
    public bool Expanded { get; set; } = true;

    public List<Source> Sources { get; set; } = [];
}

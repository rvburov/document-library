namespace PdfLibrary.Database.Entities;

/// <summary>Метка на документе.</summary>
public class DocumentTag
{
    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}

using PdfLibrary.Views.Library;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Строка «показано не все» панели чтения (<c>Views/Library/DocCutNote.cs</c>).
///
/// Потолок страниц панели нужен, но оборвать том молча нельзя: человек прочитал бы обрезанное
/// как целое. Здесь закрепляется, что строка говорит ровно о том, что обрезано, — не молчит при
/// обрезе и не пугает, когда показано все.
/// </summary>
public sealed class DocCutNoteTests
{
    [Fact]
    public void A_whole_document_has_no_note()
    {
        Assert.Equal("", DocCutNote.ForPages(12, 12));
        Assert.Equal("", DocCutNote.ForPages(0, 0));
    }

    [Fact]
    public void A_tome_says_which_pages_are_shown_and_where_the_rest_is()
    {
        var note = DocCutNote.ForPages(3000, 3400);

        Assert.Contains("Показаны страницы 1–3\u00a0000 из 3\u00a0400.", note, StringComparison.Ordinal);
        Assert.Contains("«Открыть»", note, StringComparison.Ordinal);
    }

    /// <summary>Разряды делит неразрывный пробел — число не переносится посередине строки.</summary>
    [Theory]
    [InlineData(7,       "7")]
    [InlineData(2000,    "2\u00a0000")]
    [InlineData(1234567, "1\u00a0234\u00a0567")]
    public void Numbers_are_grouped_with_a_non_breaking_space(int n, string expected)
    {
        Assert.Equal(expected, DocCutNote.N(n));
    }
}

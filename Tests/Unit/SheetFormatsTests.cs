using PdfLibrary.Core.Documents;

namespace PdfLibrary.Tests.Unit;

/// <summary>Форматы листов (<see cref="SheetFormats"/>): размеры страниц PDF → «A4 × 48».</summary>
public sealed class SheetFormatsTests
{
    private const double Mm = 72.0 / 25.4;

    [Theory]
    [InlineData(210, 297, "A4")]
    [InlineData(297, 210, "A4")]       // альбомный — тот же формат
    [InlineData(420, 297, "A3")]
    [InlineData(594, 420, "A2")]
    [InlineData(841, 594, "A1")]
    [InlineData(1189, 841, "A0")]
    [InlineData(148, 210, "A5")]
    [InlineData(215.9, 279.4, "A4")]   // дюймовый Letter укладывается в допуск 30 мм
    [InlineData(297, 630, "A4×3")]
    [InlineData(420, 891, "A3×3")]
    [InlineData(100, 148, "100×148")]  // нестандартный — размером в миллиметрах
    public void Page_size_gives_the_format(double w, double h, string expected) =>
        Assert.Equal(expected, SheetFormats.NameOf(w, h));

    [Fact]
    public void Summary_lists_large_formats_first_with_counts()
    {
        var f = new SheetFormats();
        f.Add(297 * Mm, 420 * Mm);
        f.Add(841 * Mm, 594 * Mm);
        f.Add(420 * Mm, 297 * Mm);

        Assert.Equal("A1 × 1, A3 × 2", f.Summary);
        Assert.Equal("A1", f.Largest);
        Assert.Equal(3, f.Pages);
    }

    [Fact]
    public void A_single_format_document()
    {
        var f = new SheetFormats();
        for (var i = 0; i < 48; i++) f.Add(595, 842);
        Assert.Equal("A4 × 48", f.Summary);
        Assert.Equal("A4", f.Largest);
    }

    [Fact]
    public void Empty_or_degenerate_pages_give_nothing()
    {
        var f = new SheetFormats();
        f.Add(0, 0);
        Assert.Equal("", f.Summary);
        Assert.Equal("", f.Largest);
        Assert.Equal(0, f.Pages);
    }
}

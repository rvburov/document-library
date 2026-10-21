using PdfLibrary.Utils;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Размер словами (<see cref="Sizes"/>): пороги, разрядность и байты у мелочи, которые
/// показывает только «Диагностика».
/// </summary>
public sealed class SizesTests
{
    /// <summary>Единица растет вместе с числом, разрядность — вместе с единицей.</summary>
    [Theory]
    [InlineData(700,                    "1 КБ")]     // мелочь в интерфейсе округляется до КБ
    [InlineData(1024,                   "1 КБ")]
    [InlineData(1024L * 700,            "700 КБ")]
    [InlineData(1024L * 1024,           "1,0 МБ")]
    [InlineData(1024L * 1024 * 3 + 500_000, "3,5 МБ")]
    [InlineData(1024L * 1024 * 1024,    "1,00 ГБ")]
    public void A_size_grows_its_unit_together_with_its_precision(long bytes, string expected) =>
        Assert.Equal(expected, Sizes.Text(bytes).Replace(' ', ' '));

    /// <summary>
    /// В интерфейсе мелочь — это «0 КБ», в «Диагностике» — «96 Б».
    /// </summary>
    [Fact]
    public void Tiny_sizes_show_bytes_only_where_bytes_matter()
    {
        Assert.Equal("0 КБ", Sizes.Text(96).Replace(' ', ' '));
        Assert.Equal("96 Б", Sizes.Text(96, tiny: true));

        // Выше килобайта обе стороны отвечают одинаково — иначе «тот же файл» весил бы
        // по-разному на двух экранах.
        Assert.Equal(Sizes.Text(1024L * 1024), Sizes.Text(1024L * 1024, tiny: true));
    }
}

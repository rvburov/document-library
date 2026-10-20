using PdfLibrary.Core.Documents;

namespace PdfLibrary.Tests.Unit;

/// <summary>Название из свойств PDF (<see cref="DocTitle.FromInfo"/>): осмысленное — да, сделанное программой — нет.</summary>
public sealed class DocTitleTests
{
    [Theory]
    [InlineData("Насосная станция. Чертеж общего вида", "АБВГ.301122.001 ВО")]
    [InlineData("  Насос  центробежный\nЦНС 38-44  ", "ЦНС-38-44_РЭ")]
    public void A_human_title_is_kept(string title, string file) =>
        Assert.Equal(string.Join(' ', title.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)),
                     DocTitle.FromInfo(title, file));

    [Theory]
    [InlineData("Microsoft Word - Отчет.docx")]
    [InlineData("Отчет.docx")]
    [InlineData("Чертеж.dwg")]
    [InlineData("Untitled")]
    [InlineData("Документ")]
    [InlineData("C:\\Temp\\print.pdf")]
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void A_machine_title_is_dropped(string? title) =>
        Assert.Equal("", DocTitle.FromInfo(title, "Файл"));

    /// <summary>Название, совпадающее с именем файла, ничего не добавляет.</summary>
    [Fact]
    public void A_title_equal_to_the_file_name_is_dropped() =>
        Assert.Equal("", DocTitle.FromInfo("ЦНС 38 44 РЭ", "ЦНС-38-44_РЭ"));

    [Fact]
    public void A_paragraph_in_the_title_field_is_dropped() =>
        Assert.Equal("", DocTitle.FromInfo(new string('а', DocTitle.MaxLength + 1), "Файл"));
}

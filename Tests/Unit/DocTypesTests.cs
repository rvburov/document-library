using PdfLibrary.Core.Documents;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Тип документа по пути файла и названию (<see cref="DocTypes"/>): код ЕСКД в имени, слова в
/// имени, слова в названии из свойств PDF, папка, обозначение без кода. Примеры — из
/// согласованного макета и из живых инженерных папок.
/// </summary>
public sealed class DocTypesTests
{
    [Theory]
    [InlineData("АБВГ.301122.001 ВО.pdf", DocTypes.Drawing)]
    [InlineData("АБВГ.301122.001 СБ.pdf", DocTypes.Drawing)]
    [InlineData("АБВГ.301122.001-01 СБ.pdf", DocTypes.Drawing)]
    [InlineData("АБВГ.301122.001ВО.pdf", DocTypes.Drawing)]
    [InlineData("АБВГ.301122.001 Г3.pdf", DocTypes.Scheme)]
    [InlineData("АБВГ.301122.001 Э3.pdf", DocTypes.Scheme)]
    [InlineData("АБВГ.301122.001 ПЭ3.pdf", DocTypes.Elements)]
    [InlineData("АБВГ.301122.001 РЭ.pdf", DocTypes.Manual)]
    [InlineData("АБВГ.301122.001 РЭ1.pdf", DocTypes.Manual)]
    [InlineData("АБВГ.301122.001 ПС.pdf", DocTypes.Passport)]
    [InlineData("АБВГ.301122.001 ФО.pdf", DocTypes.Logbook)]
    [InlineData("АБВГ.301122.001 ТУ.pdf", DocTypes.Conditions)]
    [InlineData("АБВГ.301122.001 ПЗ.pdf", DocTypes.Note)]
    [InlineData("АБВГ.301122.001 ВП.pdf", DocTypes.List)]
    public void Eskd_code_after_the_designation_sets_the_type(string file, string expected) =>
        Assert.Equal(expected, DocTypes.Of(file));

    /// <summary>Обозначение без кода — чертеж детали.</summary>
    [Fact]
    public void Designation_without_a_code_is_a_drawing() =>
        Assert.Equal(DocTypes.Drawing, DocTypes.Of("АБВГ.301122.005.pdf"));

    [Theory]
    [InlineData("ЦНС-38-44_РЭ.pdf", DocTypes.Manual)]
    [InlineData("Насос 1Д200 ПС.pdf", DocTypes.Passport)]
    [InlineData("Шкаф управления Э3.pdf", DocTypes.Scheme)]
    public void Eskd_code_as_the_last_word_sets_the_type(string file, string expected) =>
        Assert.Equal(expected, DocTypes.Of(file));

    [Theory]
    [InlineData("ГОСТ_2.104-2006.pdf", DocTypes.Gost)]
    [InlineData("ГОСТ Р 21.101-2020.pdf", DocTypes.Gost)]
    [InlineData("СП_31.13330.2021.pdf", DocTypes.Sp)]
    [InlineData("СП 31.13330.2021 Водоснабжение.pdf", DocTypes.Sp)]
    [InlineData("СНиП 2.04.02-84.pdf", DocTypes.Snip)]
    [InlineData("ТУ_3631-014.pdf", DocTypes.Conditions)]
    [InlineData("Договор_17-2026.pdf", DocTypes.Contract)]
    [InlineData("Протокол_45-26.pdf", DocTypes.Protocol)]
    [InlineData("Паспорт_ЦНС38_1187.pdf", DocTypes.Passport)]
    [InlineData("Спецификация оборудования.pdf", DocTypes.Specification)]
    [InlineData("Акт приемки.pdf", DocTypes.Act)]
    [InlineData("Счет-фактура 12.pdf", DocTypes.Invoice)]
    [InlineData("Отчёт о проверке.pdf", DocTypes.Report)]
    public void Words_in_the_file_name_set_the_type(string file, string expected) =>
        Assert.Equal(expected, DocTypes.Of(file));

    [Theory]
    [InlineData("Паспорта", DocTypes.Passport)]
    [InlineData("Проект/Протоколы испытаний", DocTypes.Protocol)]
    [InlineData("Договоры/2026", DocTypes.Contract)]
    public void Folder_sets_the_type_when_the_name_says_nothing(string folder, string expected) =>
        Assert.Equal(expected, DocTypes.Of("scan_0042.pdf", folder));

    /// <summary>Ближняя папка важнее дальней.</summary>
    [Fact]
    public void Nearest_folder_wins() =>
        Assert.Equal(DocTypes.Passport, DocTypes.Of("0042.pdf", "Договоры/Паспорта"));

    [Theory]
    [InlineData("Нормативы")]
    [InlineData("КД")]
    [InlineData("")]
    public void Neutral_folders_give_no_type(string folder) =>
        Assert.Equal("", DocTypes.Of("Без названия 1.pdf", folder));

    /// <summary>Слово, внутри которого прячется код или слово-тип, типа не дает.</summary>
    [Theory]
    [InlineData("Актуализация данных.pdf")]
    [InlineData("Спорт и отдых.pdf")]
    [InlineData("Насос ГАЗ.pdf")]
    [InlineData("Схемотехника для всех А4.pdf")]
    public void Partial_words_do_not_set_a_type(string file)
    {
        var type = DocTypes.Of(file);
        Assert.NotEqual(DocTypes.Act, type);
        Assert.NotEqual(DocTypes.Sp, type);
    }

    /// <summary>Одна буква в конце имени кодом не считается: «И» и «А» слишком часто — просто слово.</summary>
    [Fact]
    public void A_single_letter_is_not_a_code() =>
        Assert.Equal("", DocTypes.Of("Насос и привод И.pdf"));

    [Fact]
    public void Code_wins_over_words_in_the_name() =>
        Assert.Equal(DocTypes.Manual, DocTypes.Of("Паспорт и руководство АБВГ.301122.001 РЭ.pdf"));

    /// <summary>
    /// Обозначение без кода носит и чертеж детали, и спецификация: название из свойств PDF
    /// решает, что это.
    /// </summary>
    [Fact]
    public void Title_refines_a_designation_without_a_code() =>
        Assert.Equal(DocTypes.Specification,
                     DocTypes.Of("АБВГ.301122.001.pdf", "КД", "Спецификация АБВГ.301122.001"));

    [Fact]
    public void Title_without_type_words_leaves_a_designation_a_drawing() =>
        Assert.Equal(DocTypes.Drawing, DocTypes.Of("АБВГ.301122.005.pdf", null, "Вал приводной"));

    [Fact]
    public void Folder_refines_a_designation_without_a_code() =>
        Assert.Equal(DocTypes.Specification, DocTypes.Of("АБВГ.301122.001.pdf", "Спецификации"));

    [Fact]
    public void Code_wins_over_the_title() =>
        Assert.Equal(DocTypes.Drawing,
                     DocTypes.Of("АБВГ.301122.001 ВО.pdf", null, "Спецификация АБВГ.301122.001"));

    [Fact]
    public void Words_in_the_name_win_over_the_title() =>
        Assert.Equal(DocTypes.Protocol, DocTypes.Of("Протокол_45-26.pdf", null, "Акт приемки насоса"));

    [Fact]
    public void Title_wins_over_the_folder() =>
        Assert.Equal(DocTypes.Protocol, DocTypes.Of("scan_0042.pdf", "Паспорта", "Протокол испытаний № 45/26"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_title_changes_nothing(string? title) =>
        Assert.Equal(DocTypes.Passport, DocTypes.Of("scan_0042.pdf", "Паспорта", title));
}

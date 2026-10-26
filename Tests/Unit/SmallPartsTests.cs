using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Logging;
using PdfLibrary.Services;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Мелкие части: маски «Пропускать файлы», поиск по названию, CSV журнала, имя папки, отбор
/// сообщений приемника журнала для crash.log.
/// </summary>
public sealed class SmallPartsTests
{
    [Fact]
    public void Log_timestamp_has_seconds_in_local_time()
    {
        var utc = new DateTime(2026, 10, 26, 9, 51, 48, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), LogLabels.FormatTs("2026-10-26 09:51:48"));
    }

    [Theory]
    [InlineData("~$Отчет.pdf", true)]
    [InlineData("Отчет.pdf", false)]
    [InlineData("План_черновик.pdf", true)]
    [InlineData("ПЛАН_ЧЕРНОВИК.PDF", true)]
    public void Skip_masks_match_file_names(string name, bool skipped) =>
        Assert.Equal(skipped, SourceScanner.Skipped(name, SourceScanner.ParseMasks("~$*.pdf, *_черновик.pdf")));

    [Fact]
    public void Masks_split_by_comma_or_semicolon_and_drop_blanks() =>
        Assert.Equal(["~$*.pdf", "*.tmp.pdf"], SourceScanner.ParseMasks(" ~$*.pdf ;, *.tmp.pdf ,"));

    [Theory]
    [InlineData("насос центробежный", true)]
    [InlineData("насос центр", false)]
    [InlineData("ЦНС 38", true)]
    [InlineData("паспорта", true)]       // папка тоже ищется
    [InlineData("насос дизель", false)]  // все слова сразу
    [InlineData("центробежный насос", false)]
    [InlineData("насос ЦНС", false)]
    [InlineData("сос", false)]           // слово ищется с начала, не с середины
    public void Name_search_needs_the_whole_phrase(string query, bool found)
    {
        var doc = new DocItem
        {
            FileName = "ЦНС-38-44_РЭ.pdf", Folder = "Паспорта",
            InfoTitle = "Насос центробежный ЦНС 38-44. Руководство по эксплуатации",
        };
        Assert.Equal(found, LibraryService.NameMatches(doc, DocHighlight.Tokens(query)));
    }

    [Theory]
    [InlineData("просто", "просто")]
    [InlineData("а;б", "\"а;б\"")]
    [InlineData("с \"кавычкой\"", "\"с \"\"кавычкой\"\"\"")]
    [InlineData("две\nстроки", "\"две\nстроки\"")]
    public void Csv_fields_are_quoted_only_when_needed(string value, string expected) =>
        Assert.Equal(expected, LogCsv.Quote(value));

    [Theory]
    [InlineData(@"\\srv-01\archive", "archive")]
    [InlineData(@"D:\Проекты\", "Проекты")]
    [InlineData("/home/user/docs", "docs")]
    [InlineData("D:", "D:")]
    public void Folder_name_becomes_the_source_name(string path, string expected) =>
        Assert.Equal(expected, LibraryService.FolderName(path));

    /// <summary>
    /// Отчеты приемника журнала о штатной работе в crash.log не попадают, сбои — попадают: иначе
    /// пачка в одну запись заняла бы весь лимит crash.log за первые секунды сеанса.
    /// </summary>
    [Theory]
    [InlineData("2026-10-20T23:07:16.0432603Z Sending batch of 1 logs", true)]
    [InlineData("2026-10-20T23:07:16.0467275Z 0 records deleted", true)]
    [InlineData("2026-10-20T23:07:16Z Deleting log entries older than 20.09.2026 23:07:16 +00:00", true)]
    [InlineData("2026-10-20T23:07:16Z Unable to write 1 log events to the database due to following error: database is locked", false)]
    [InlineData("2026-10-20T23:07:16Z Caught exception while emitting to sink", false)]
    public void Only_sink_failures_reach_the_crash_log(string message, bool chatter) =>
        Assert.Equal(chatter, AppLog.IsSinkChatter(message));
}

using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Services;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// Библиотека целиком (<see cref="LibraryService"/>) на настоящей папке с настоящими PDF:
/// добавили источник — папка проверена, документы в памяти, текст прочитан в фоне, поиск находит
/// и по тексту, и по названию; удалили файл — «файл не найден»; положили новый — наблюдение
/// заметило сам.
///
/// UI-поток здесь — замок: все, что служба переносит «в UI-поток», выполняется под ним, и тест
/// читает состояние под тем же замком.
/// </summary>
public sealed class LibraryServiceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly TempLibrary _lib = new();
    private readonly object _ui = new();
    private readonly string _folder;
    private readonly DocIndexService _reader;
    private readonly LibraryService _service;

    public LibraryServiceTests()
    {
        _folder = Path.Combine(_lib.Dir, "Проектная документация");
        Directory.CreateDirectory(Path.Combine(_folder, "Паспорта"));
        _reader = new DocIndexService(new DocTextIndex(Path.Combine(_lib.Dir, DocTextIndex.FileName)));
        _service = new LibraryService(_lib.Store, _reader, a => { lock (_ui) a(); });
    }

    public void Dispose()
    {
        _service.Dispose();
        _reader.DisposeAsync().AsTask().Wait(Patience);
        _lib.Dispose();
    }

    private string Pdf(string relative, string text, string? title = null)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return title is null
            ? HandPdf.WriteText(Path.GetDirectoryName(path)!, Path.GetFileName(path), text)
            : HandPdf.Write(Path.GetDirectoryName(path)!, Path.GetFileName(path), title,
                            new HandPdf.Sheet(HandPdf.Plain(40, 780, HandPdf.Latin(text))));
    }

    private async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            lock (_ui) if (condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("Не дождались: " + what);
    }

    private SourceItem AddAndWait()
    {
        SourceItem? source;
        lock (_ui) source = _service.AddSource(_folder, "", null);
        Assert.NotNull(source);
        return source!;
    }

    [Fact]
    public async Task An_added_folder_is_scanned_read_and_searchable()
    {
        Pdf("Паспорта/Паспорт насоса.pdf", "Pasport nasosa centrobezhnogo CNS 38-44");
        Pdf("КД/АБВГ.301122.001 ВО.pdf", "Obshchij vid nasosnoj stancii", "Nasosnaya stanciya. Chertezh obshchego vida");

        var source = AddAndWait();
        Assert.Equal("Проектная документация", source.Name);

        await Until(() => _service.Documents.Count == 2 && !_service.Checking, "проверка папки");
        await Until(() => _service.Documents.All(d => d.TextState == DocTextState.Read), "чтение текста");

        lock (_ui)
        {
            var drawing = _service.Documents.Single(d => d.FileName.EndsWith("ВО.pdf"));
            Assert.Equal(DocTypes.Drawing, drawing.DocType);
            Assert.Equal("Nasosnaya stanciya. Chertezh obshchego vida", drawing.Title);
            Assert.Equal(1, drawing.Pages);
            Assert.Equal("A4 × 1", drawing.Sheets);
            Assert.Equal(2, source.Count);
        }

        var byText = await _service.SearchAsync("centrobezhnogo", 14);
        var hit = Assert.Single(byText!.Hits);
        Assert.True(hit.Value.InText);
        Assert.Contains(DocTextIndex.HitOpen, hit.Value.Snippet);

        var byName = await _service.SearchAsync("паспорт насоса", 14);
        Assert.True(Assert.Single(byName!.Hits).Value.ByName);
    }

    /// <summary>
    /// Тип по пути известен сразу, а прочитанное название из свойств PDF его уточняет: обозначение
    /// без кода — чертеж, пока не прочитано, что это спецификация.
    /// </summary>
    [Fact]
    public async Task The_title_refines_the_type_once_the_file_is_read()
    {
        Pdf("КД/АБВГ.301122.001.pdf", "Poz. 1 nasos centrobezhnyj", "Спецификация АБВГ.301122.001");

        AddAndWait();
        await Until(() => _service.Documents.Count == 1 && !_service.Checking, "проверка папки");
        Assert.Equal(DocTypes.Drawing, _lib.Store.Load().Documents.Single().DocType);

        await Until(() => _service.Documents[0].TextState == DocTextState.Read, "чтение текста");
        lock (_ui)
        {
            Assert.Equal("Спецификация АБВГ.301122.001", _service.Documents[0].Title);
            Assert.Equal(DocTypes.Specification, _service.Documents[0].DocType);
        }
    }

    [Fact]
    public async Task A_deleted_file_becomes_missing_on_the_next_check()
    {
        var path = Pdf("a.pdf", "Pervyj dokument");
        AddAndWait();
        await Until(() => _service.Documents.Count == 1 && !_service.Checking, "проверка папки");

        File.Delete(path);
        lock (_ui) _service.CheckAll();
        await Until(() => _service.Documents.Single().Missing, "пометка «файл не найден»");
    }

    [Fact]
    public async Task A_new_file_is_noticed_by_watching_the_folder()
    {
        Pdf("a.pdf", "Pervyj dokument");
        AddAndWait();
        await Until(() => _service.Documents.Count == 1 && !_service.Checking, "проверка папки");

        Pdf("Новые/b.pdf", "Vtoroj dokument");
        await Until(() => _service.Documents.Count == 2, "наблюдение за папкой: новый файл");
    }

    [Fact]
    public async Task A_missing_folder_is_reported_unavailable_without_losing_documents()
    {
        Pdf("a.pdf", "Pervyj dokument");
        var source = AddAndWait();
        await Until(() => _service.Documents.Count == 1 && !_service.Checking, "проверка папки");

        var moved = _folder + "-moved";
        Directory.Move(_folder, moved);
        try
        {
            lock (_ui) _service.Check(source.Id);
            await Until(() => source.UnavailableSince is not null, "пометка «недоступна»");
            lock (_ui) Assert.False(_service.Documents.Single().Missing);
        }
        finally
        {
            Directory.Move(moved, _folder);
        }
    }

    [Fact]
    public async Task Tags_and_removal_change_the_library_in_memory_and_in_the_database()
    {
        Pdf("a.pdf", "Pervyj dokument");
        var source = AddAndWait();
        await Until(() => _service.Documents.Count == 1 && !_service.Checking, "проверка папки");

        lock (_ui)
        {
            var tag = _service.AddTag("Действующий", "#34C759");
            _service.SetTag(tag.Id, [.. _service.Documents], true);
            Assert.Contains(tag.Id, _service.Documents.Single().Tags);
            Assert.Contains(tag.Id, _lib.Store.Load().Documents.Single().Tags);

            _service.RemoveSource(source.Id);
            Assert.Empty(_service.Documents);
            Assert.Empty(_lib.Store.Load().Sources);
        }
    }

    [Fact]
    public async Task Clearing_the_cache_reads_everything_again()
    {
        Pdf("a.pdf", "Pervyj dokument");
        AddAndWait();
        await Until(() => _service.Documents.Count == 1 && _service.Documents[0].TextState == DocTextState.Read, "чтение");

        await _service.ClearCacheAsync();
        await Until(() => _service.Documents[0].TextState == DocTextState.Read, "повторное чтение после очистки");
        Assert.Single((await _service.SearchAsync("pervyj", 14))!.Hits);
    }
}

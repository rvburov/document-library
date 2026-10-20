using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// База библиотеки (<see cref="LibraryStore"/>): миграция, источники, группы, метки и перенос
/// итогов проверки папок — новые, измененные, пропавшие и переехавшие файлы.
/// </summary>
public sealed class LibraryStoreTests : IDisposable
{
    private readonly TempLibrary _lib = new();
    private readonly string _folder;
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    public LibraryStoreTests()
    {
        _folder = Path.Combine(_lib.Dir, "Проект");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => _lib.Dispose();

    private LibraryStore Store => _lib.Store;

    private static ScanResult Scan(params ScannedFile[] files) => new(true, true, [.. files], null, TimeSpan.Zero);

    private SourceItem Source() => Store.AddSource("Проект", _folder, null)!;

    [Fact]
    public void A_fresh_database_is_empty_and_opens_again()
    {
        var snapshot = Store.Load();
        Assert.Empty(snapshot.Sources);
        Assert.Empty(snapshot.Documents);

        _lib.Reopen();
        Assert.Empty(Store.Load().Sources);
    }

    [Fact]
    public void Settings_keep_only_what_was_set_and_survive_a_restart()
    {
        Store.Set("theme", "dark");
        Store.Set("card.size", "large");
        Store.Remove("card.size");

        var reopened = _lib.Reopen();
        Assert.Equal("dark", reopened.Get("theme"));
        Assert.Null(reopened.Get("card.size"));
    }

    [Fact]
    public void The_same_folder_is_added_once()
    {
        Assert.NotNull(Store.AddSource("Проект", _folder, null));
        Assert.Null(Store.AddSource("Еще раз", _folder + Path.DirectorySeparatorChar, null));
    }

    [Fact]
    public void A_scan_adds_documents_with_types_from_their_names()
    {
        var source = Source();
        var changes = Store.ApplyScan(source, Scan(
            new ScannedFile("КД/АБВГ.301122.001 ВО.pdf", 100, 1),
            new ScannedFile("Паспорта/scan_01.pdf", 200, 2)), Now);

        Assert.Equal(2, changes.Added.Count);
        Assert.All(changes.Added, d => Assert.True(d.Id > 0));
        var docs = Store.Load().Documents.OrderBy(d => d.RelativePath).ToList();
        Assert.Equal(DocTypes.Drawing, docs[0].DocType);
        Assert.Equal("КД", docs[0].Folder);
        Assert.Equal("АБВГ.301122.001 ВО.pdf", docs[0].FileName);
        Assert.Equal(DocTypes.Passport, docs[1].DocType);
        Assert.Equal(Path.Combine(_folder, "Паспорта", "scan_01.pdf"), docs[1].FullPath);
    }

    [Fact]
    public void A_changed_file_is_updated_and_an_unchanged_one_is_left_alone()
    {
        var source = Source();
        Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 100, 1), new ScannedFile("b.pdf", 100, 1)), Now);

        var changes = Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 150, 2), new ScannedFile("b.pdf", 100, 1)), Now);

        var updated = Assert.Single(changes.Updated);
        Assert.Equal("a.pdf", updated.RelativePath);
        Assert.Equal(150, updated.Size);
        Assert.Empty(changes.Added);
    }

    [Fact]
    public void A_vanished_file_is_marked_missing_and_comes_back()
    {
        var source = Source();
        Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 100, 1)), Now);

        var gone = Store.ApplyScan(source, Scan(), Now);
        Assert.Single(gone.Missing);
        Assert.True(Store.Load().Documents.Single().Missing);

        var back = Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 100, 1)), Now);
        Assert.Single(back.Updated);
        Assert.False(Store.Load().Documents.Single().Missing);
    }

    /// <summary>Обрыв сети посреди обхода не дает права считать пропавшим то, до чего обход не дошел.</summary>
    [Fact]
    public void An_incomplete_scan_marks_nothing_missing()
    {
        var source = Source();
        Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 100, 1), new ScannedFile("b.pdf", 100, 1)), Now);

        var partial = new ScanResult(true, false, [new ScannedFile("a.pdf", 100, 1)], "сеть", TimeSpan.Zero);
        var changes = Store.ApplyScan(source, partial, Now);

        Assert.Empty(changes.Missing);
        Assert.DoesNotContain(Store.Load().Documents, d => d.Missing);
    }

    /// <summary>Файл переложили в другую папку: та же строка, те же метки, без «файл не найден».</summary>
    [Fact]
    public void A_moved_file_keeps_its_row_and_tags()
    {
        var source = Source();
        var added = Store.ApplyScan(source, Scan(new ScannedFile("Входящие/Протокол 45.pdf", 700, 9)), Now).Added.Single();
        var tag = Store.AddTag("На проверке", "#FF9500");
        Store.SetTag(tag.Id, [added.Id], true);

        var changes = Store.ApplyScan(source, Scan(new ScannedFile("Испытания/Протокол 45.pdf", 700, 9)), Now);

        Assert.Empty(changes.Added);
        Assert.Empty(changes.Missing);
        var moved = Assert.Single(changes.Updated);
        Assert.Equal(added.Id, moved.Id);
        var doc = Store.Load().Documents.Single();
        Assert.Equal("Испытания/Протокол 45.pdf", doc.RelativePath);
        Assert.Contains(tag.Id, doc.Tags);
    }

    /// <summary>Переименование в той же папке — тоже переезд.</summary>
    [Fact]
    public void A_renamed_file_in_the_same_folder_keeps_its_row()
    {
        var source = Source();
        var added = Store.ApplyScan(source, Scan(new ScannedFile("КД/черновик.pdf", 500, 3)), Now).Added.Single();

        var changes = Store.ApplyScan(source, Scan(new ScannedFile("КД/АБВГ.301122.001 СБ.pdf", 500, 3)), Now);

        var moved = Assert.Single(changes.Updated);
        Assert.Equal(added.Id, moved.Id);
        Assert.Equal(DocTypes.Drawing, moved.DocType);
    }

    [Fact]
    public void Long_missing_untagged_documents_are_forgotten_but_tagged_stay()
    {
        var source = Source();
        var added = Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 1, 1), new ScannedFile("b.pdf", 2, 2)), Now).Added;
        var tag = Store.AddTag("Важно", "#FF3B30");
        Store.SetTag(tag.Id, [added.Single(d => d.FileName == "b.pdf").Id], true);

        Store.ApplyScan(source, Scan(), Now);
        var later = Store.ApplyScan(source, Scan(), Now + LibraryStore.ForgetMissingAfter + TimeSpan.FromDays(1));

        Assert.Single(later.Removed);
        var left = Store.Load().Documents.Single();
        Assert.Equal("b.pdf", left.FileName);
        Assert.True(left.Missing);
    }

    [Fact]
    public void Removing_a_source_takes_its_documents_and_tag_links_but_not_the_tags()
    {
        var source = Source();
        var doc = Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 1, 1)), Now).Added.Single();
        var tag = Store.AddTag("Действующий", "#34C759");
        Store.SetTag(tag.Id, [doc.Id], true);

        Store.RemoveSource(source.Id);

        var snapshot = Store.Load();
        Assert.Empty(snapshot.Sources);
        Assert.Empty(snapshot.Documents);
        Assert.Single(snapshot.Tags);
    }

    [Fact]
    public void A_group_takes_its_sources_from_other_groups()
    {
        var a = Store.AddSource("А", Path.Combine(_lib.Dir, "a"), null)!;
        var b = Store.AddSource("Б", Path.Combine(_lib.Dir, "b"), null)!;
        var first = Store.AddGroup("Первая", [a.Id, b.Id]);
        var second = Store.AddGroup("Вторая", [b.Id]);

        var sources = Store.Load().Sources.ToDictionary(s => s.Name);
        Assert.Equal(first.Id, sources["А"].GroupId);
        Assert.Equal(second.Id, sources["Б"].GroupId);
    }

    [Fact]
    public void Tags_are_set_and_removed_for_a_set_of_documents()
    {
        var source = Source();
        var docs = Store.ApplyScan(source, Scan(new ScannedFile("a.pdf", 1, 1), new ScannedFile("b.pdf", 2, 2)), Now).Added;
        var tag = Store.AddTag("На согласовании", "#FF9500");

        Store.SetTag(tag.Id, docs.Select(d => d.Id).ToList(), true);
        Store.SetTag(tag.Id, docs.Select(d => d.Id).ToList(), true);   // повтор не дублирует
        Assert.All(Store.Load().Documents, d => Assert.Contains(tag.Id, d.Tags));

        Store.SetTag(tag.Id, [docs[0].Id], false);
        Assert.Single(Store.Load().Documents, d => d.Tags.Contains(tag.Id));
    }

    [Fact]
    public void Shown_flag_and_rename_survive_a_restart()
    {
        var source = Source();
        Store.SetSourcesShown([source.Id], false);
        Store.RenameSource(source.Id, "Архив проекта");

        var reloaded = _lib.Reopen().Load().Sources.Single();
        Assert.False(reloaded.Shown);
        Assert.Equal("Архив проекта", reloaded.Name);
    }
}

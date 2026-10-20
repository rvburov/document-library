using Microsoft.EntityFrameworkCore;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database.Entities;
using PdfLibrary.Utils;

namespace PdfLibrary.Database;

/// <summary>Что содержимое library.db дает ленте при старте.</summary>
public sealed record LibrarySnapshot(List<SourceItem> Sources, List<GroupItem> Groups, List<TagItem> Tags,
                                     List<DocItem> Documents);

/// <summary>Чем проверка папки изменила список документов источника.</summary>
/// <param name="Added">Новые документы — уже с Id из базы.</param>
/// <param name="Updated">Документы, у которых сменились размер, время правки или путь (переезд
/// внутри источника), и вернувшиеся на место.</param>
/// <param name="Missing">Пропали с диска на этой проверке.</param>
/// <param name="Removed">Убраны из базы: давно пропавшие без меток (<see cref="LibraryStore.ForgetMissingAfter"/>).</param>
public sealed record ScanChanges(List<DocItem> Added, List<DocItem> Updated, List<DocItem> Missing,
                                 List<int> Removed)
{
    public bool Any => Added.Count + Updated.Count + Missing.Count + Removed.Count > 0;
}

/// <summary>
/// Доступ к library.db: загрузка всей библиотеки при старте, правки источников, групп и меток,
/// перенос итогов проверки папок и хранилище настроек.
///
/// Записи идут по одной (<c>_write</c>): их зовут и UI-поток (галочка, новая метка), и фоновая
/// проверка папок, а SQLite пишет только одним писателем — пусть очередь держит программа, а не
/// повторы по <c>SQLITE_BUSY</c>. Каждая операция — свой короткий контекст.
/// </summary>
public sealed class LibraryStore : ISettingsStore
{
    /// <summary>
    /// Через сколько пропавший документ без меток уходит из базы. Человек успевает заметить
    /// «файл не найден» и вернуть файл, а папка, которую перекладывают год, не копит тысячи
    /// карточек-призраков. Документ с меткой остается, пока человек сам не снимет метку: метки
    /// ставили руками, и терять их молча нельзя.
    /// </summary>
    public static readonly TimeSpan ForgetMissingAfter = TimeSpan.FromDays(30);

    private readonly string _path;
    private readonly object _write = new();
    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);

    private LibraryStore(string path) => _path = path;

    /// <summary>Путь к файлу базы.</summary>
    public string Path => _path;

    /// <summary>Открыть базу: создать или догнать схему миграциями, прочитать настройки.</summary>
    public static LibraryStore Open(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var store = new LibraryStore(path);
        using (var db = store.Context())
        {
            db.Database.Migrate();
            // WAL: лента читает, пока проверка папок пишет, и блокировать одно другим незачем.
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            foreach (var s in db.Settings.AsNoTracking()) store._settings[s.Key] = s.Value;
        }
        return store;
    }

    private LibraryDbContext Context() => new(_path);

    // ── Настройки ─────────────────────────────────────────────────────────────

    public string? Get(string key)
    {
        lock (_settings) return _settings.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string value)
    {
        lock (_settings) _settings[key] = value;
        lock (_write)
        {
            using var db = Context();
            var row = db.Settings.Find(key);
            if (row is null) db.Settings.Add(new Setting { Key = key, Value = value });
            else row.Value = value;
            db.SaveChanges();
        }
    }

    public void Remove(string key)
    {
        lock (_settings) _settings.Remove(key);
        lock (_write)
        {
            using var db = Context();
            db.Settings.Where(s => s.Key == key).ExecuteDelete();
        }
    }

    // ── Загрузка ──────────────────────────────────────────────────────────────

    /// <summary>Вся библиотека для ленты. Документы — без того, что знает кеш: это добавляет
    /// <see cref="LibraryService"/> после переписи.</summary>
    public LibrarySnapshot Load()
    {
        using var db = Context();

        var sources = db.Sources.AsNoTracking().OrderBy(s => s.Position).ThenBy(s => s.Id)
            .Select(s => new SourceItem
            {
                Id = s.Id, Name = s.Name, Path = s.Path, GroupId = s.GroupId, Position = s.Position,
                Shown = s.Shown, CheckedAt = s.CheckedAt, UnavailableSince = s.UnavailableSince,
            }).ToList();

        var groups = db.SourceGroups.AsNoTracking().OrderBy(g => g.Position).ThenBy(g => g.Id)
            .Select(g => new GroupItem { Id = g.Id, Name = g.Name, Position = g.Position, Expanded = g.Expanded })
            .ToList();

        var tags = db.Tags.AsNoTracking().OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => new TagItem { Id = t.Id, Name = t.Name, Color = t.Color, Position = t.Position })
            .ToList();

        var roots = sources.ToDictionary(s => s.Id, s => s.Path);
        var docs  = new Dictionary<int, DocItem>();
        foreach (var d in db.Documents.AsNoTracking())
        {
            if (!roots.TryGetValue(d.SourceId, out var root)) continue;
            docs[d.Id] = ToItem(d, root);
        }

        foreach (var link in db.DocumentTags.AsNoTracking())
            if (docs.TryGetValue(link.DocumentId, out var doc)) doc.Tags.Add(link.TagId);

        foreach (var s in sources)
            s.Count = docs.Values.Count(d => d.SourceId == s.Id && !d.Missing);

        return new LibrarySnapshot(sources, groups, tags, [.. docs.Values]);
    }

    /// <summary>Строка базы → документ в памяти.</summary>
    public static DocItem ToItem(Document d, string root)
    {
        var item = new DocItem
        {
            Id = d.Id, SourceId = d.SourceId, DocType = d.DocType, Size = d.Size, Mtime = d.Mtime,
            FoundAt = d.FoundAt, Missing = d.MissingSince is not null,
        };
        SetPath(item, root, d.RelativePath);
        return item;
    }

    /// <summary>Разложить путь документа по полям: полный путь, имя файла, папка.</summary>
    public static void SetPath(DocItem item, string root, string relative)
    {
        item.RelativePath = relative;
        item.FullPath     = System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var cut = relative.LastIndexOf('/');
        item.FileName = cut >= 0 ? relative[(cut + 1)..] : relative;
        item.Folder   = cut >= 0 ? relative[..cut] : "";
    }

    // ── Источники ─────────────────────────────────────────────────────────────

    /// <summary>Добавить источник; <c>null</c> — такая папка уже есть в библиотеке.</summary>
    public SourceItem? AddSource(string name, string path, int? groupId)
    {
        lock (_write)
        {
            using var db = Context();
            var full = NormalizeFolder(path);
            if (db.Sources.Any(s => s.Path == full)) return null;

            var position = (db.Sources.Where(s => s.GroupId == groupId).Max(s => (int?)s.Position) ?? -1) + 1;
            var row = new Source { Name = name, Path = full, GroupId = groupId, Position = position };
            db.Sources.Add(row);
            db.SaveChanges();
            return new SourceItem { Id = row.Id, Name = name, Path = full, GroupId = groupId, Position = position };
        }
    }

    /// <summary>Путь папки в одном написании: полный, без завершающего разделителя.</summary>
    public static string NormalizeFolder(string path)
    {
        var full = System.IO.Path.GetFullPath(path.Trim());
        var root = System.IO.Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }

    public void RenameSource(int id, string name) =>
        Update(db => db.Sources.Where(s => s.Id == id).ExecuteUpdate(u => u.SetProperty(s => s.Name, name)));

    public void SetSourceShown(int id, bool shown) =>
        Update(db => db.Sources.Where(s => s.Id == id).ExecuteUpdate(u => u.SetProperty(s => s.Shown, shown)));

    public void SetSourcesShown(IReadOnlyCollection<int> ids, bool shown) =>
        Update(db => db.Sources.Where(s => ids.Contains(s.Id)).ExecuteUpdate(u => u.SetProperty(s => s.Shown, shown)));

    /// <summary>Убрать источник из библиотеки: его документы и метки на них уходят вместе с ним.
    /// Файлы на диске не трогаются.</summary>
    public void RemoveSource(int id) =>
        Update(db =>
        {
            // Каскад в SQLite работает только при включенных внешних ключах; EF включает их сам,
            // но удаление тут идет напрямую — поэтому порядок явный.
            var docs = db.Documents.Where(d => d.SourceId == id).Select(d => d.Id);
            db.DocumentTags.Where(t => docs.Contains(t.DocumentId)).ExecuteDelete();
            db.Documents.Where(d => d.SourceId == id).ExecuteDelete();
            db.Sources.Where(s => s.Id == id).ExecuteDelete();
        });

    /// <summary>Отметить результат проверки папки: когда проверена и доступна ли.</summary>
    public void SetSourceState(int id, DateTime? checkedAt, DateTime? unavailableSince) =>
        Update(db => db.Sources.Where(s => s.Id == id).ExecuteUpdate(u => u
            .SetProperty(s => s.CheckedAt, checkedAt)
            .SetProperty(s => s.UnavailableSince, unavailableSince)));

    // ── Группы ────────────────────────────────────────────────────────────────

    /// <summary>Новая группа; перечисленные источники переходят в нее из прежних групп.</summary>
    public GroupItem AddGroup(string name, IReadOnlyCollection<int> sourceIds)
    {
        lock (_write)
        {
            using var db = Context();
            var position = (db.SourceGroups.Max(g => (int?)g.Position) ?? -1) + 1;
            var row = new SourceGroup { Name = name, Position = position, Expanded = true };
            db.SourceGroups.Add(row);
            db.SaveChanges();

            var order = 0;
            foreach (var source in db.Sources.Where(s => sourceIds.Contains(s.Id)).OrderBy(s => s.Position))
            {
                source.GroupId  = row.Id;
                source.Position = order++;
            }
            db.SaveChanges();
            return new GroupItem { Id = row.Id, Name = name, Position = position, Expanded = true };
        }
    }

    public void SetSourceGroup(int sourceId, int? groupId) =>
        Update(db =>
        {
            var position = (db.Sources.Where(s => s.GroupId == groupId).Max(s => (int?)s.Position) ?? -1) + 1;
            db.Sources.Where(s => s.Id == sourceId).ExecuteUpdate(u => u
                .SetProperty(s => s.GroupId, groupId)
                .SetProperty(s => s.Position, position));
        });

    public void SetGroupExpanded(int id, bool expanded) =>
        Update(db => db.SourceGroups.Where(g => g.Id == id).ExecuteUpdate(u => u.SetProperty(g => g.Expanded, expanded)));

    // ── Метки ─────────────────────────────────────────────────────────────────

    public TagItem AddTag(string name, string color)
    {
        lock (_write)
        {
            using var db = Context();
            var position = (db.Tags.Max(t => (int?)t.Position) ?? -1) + 1;
            var row = new Tag { Name = name, Color = color, Position = position };
            db.Tags.Add(row);
            db.SaveChanges();
            return new TagItem { Id = row.Id, Name = name, Color = color, Position = position };
        }
    }

    /// <summary>Поставить или снять метку набору документов.</summary>
    public void SetTag(int tagId, IReadOnlyCollection<int> documentIds, bool on) =>
        Update(db =>
        {
            if (!on)
            {
                db.DocumentTags.Where(t => t.TagId == tagId && documentIds.Contains(t.DocumentId)).ExecuteDelete();
                return;
            }
            var have = db.DocumentTags.Where(t => t.TagId == tagId && documentIds.Contains(t.DocumentId))
                                      .Select(t => t.DocumentId).ToHashSet();
            foreach (var id in documentIds)
                if (!have.Contains(id)) db.DocumentTags.Add(new DocumentTag { DocumentId = id, TagId = tagId });
            db.SaveChanges();
        });

    // ── Итог проверки папки ───────────────────────────────────────────────────

    /// <summary>
    /// Перенести итог проверки папки в базу и сказать, что изменилось. Новый файл — новая строка
    /// с типом по имени и папке; знакомый с другим размером или временем — обновленная строка;
    /// пропавший — отметка «с какого времени нет».
    ///
    /// <b>Переезд внутри источника.</b> Файл переложили в другую папку или переименовали: старый
    /// путь пропал, новый появился, а размер, время правки и имя (или папка) те же. Такой файл —
    /// тот же документ на новом месте: строка переезжает вместе с метками, а не плодит
    /// «файл не найден» плюс новичка без меток.
    ///
    /// Пропавшими файлы считаются только после ПОЛНОГО обхода (<see cref="ScanResult.Complete"/>):
    /// обрыв сети на середине иначе пометил бы пропавшей половину библиотеки.
    /// </summary>
    public ScanChanges ApplyScan(SourceItem source, ScanResult scan, DateTime now)
    {
        var added   = new List<DocItem>();
        var updated = new List<DocItem>();
        var missing = new List<DocItem>();
        var removed = new List<int>();

        lock (_write)
        {
            using var db = Context();
            var rows   = db.Documents.Where(d => d.SourceId == source.Id).ToList();
            var byPath = rows.ToDictionary(d => d.RelativePath, StringComparer.Ordinal);
            var seen   = new HashSet<string>(StringComparer.Ordinal);
            var fresh  = new List<ScannedFile>();
            // Новые строки: Id им выдаст база при сохранении, и в документ памяти он должен
            // попасть уже настоящим.
            var newRows = new List<Document>();

            foreach (var file in scan.Files)
            {
                seen.Add(file.RelativePath);
                if (!byPath.TryGetValue(file.RelativePath, out var row)) { fresh.Add(file); continue; }

                var back = row.MissingSince is not null;
                if (row.Size == file.Size && row.Mtime == file.Mtime && !back) continue;
                row.Size = file.Size;
                row.Mtime = file.Mtime;
                row.MissingSince = null;
                updated.Add(ToItem(row, source.Path));
            }

            // Пропавшие на этой проверке — кандидаты в «переехавшие».
            var gone = scan.Complete
                ? rows.Where(r => !seen.Contains(r.RelativePath) && r.MissingSince is null).ToList()
                : [];

            foreach (var file in fresh)
            {
                var name  = FileNameOf(file.RelativePath);
                var moved = gone.FirstOrDefault(r => r.Size == file.Size && r.Mtime == file.Mtime &&
                                                     FileNameOf(r.RelativePath) == name)
                         ?? gone.FirstOrDefault(r => r.Size == file.Size && r.Mtime == file.Mtime &&
                                                     FolderOf(r.RelativePath) == FolderOf(file.RelativePath));
                if (moved is not null)
                {
                    gone.Remove(moved);
                    moved.RelativePath = file.RelativePath;
                    moved.DocType = DocTypes.Of(name, FolderOf(file.RelativePath));
                    updated.Add(ToItem(moved, source.Path));
                    continue;
                }

                var row = new Document
                {
                    SourceId = source.Id, RelativePath = file.RelativePath, Size = file.Size, Mtime = file.Mtime,
                    DocType = DocTypes.Of(name, FolderOf(file.RelativePath)), FoundAt = now,
                };
                db.Documents.Add(row);
                newRows.Add(row);
            }

            foreach (var row in gone)
            {
                row.MissingSince = now;
                missing.Add(ToItem(row, source.Path));
            }

            // Давно пропавшие без меток — из базы вон.
            if (scan.Complete)
            {
                var cutoff  = now - ForgetMissingAfter;
                var tagged  = db.DocumentTags.Select(t => t.DocumentId).Distinct().ToHashSet();
                var expired = rows.Where(r => r.MissingSince is { } since && since < cutoff &&
                                              !seen.Contains(r.RelativePath) && !tagged.Contains(r.Id)).ToList();
                foreach (var row in expired)
                {
                    db.Documents.Remove(row);
                    removed.Add(row.Id);
                }
            }

            db.SaveChanges();
            foreach (var row in newRows) added.Add(ToItem(row, source.Path));
        }

        return new ScanChanges(added, updated, missing, removed);
    }

    private static string FileNameOf(string relative)
    {
        var cut = relative.LastIndexOf('/');
        return cut >= 0 ? relative[(cut + 1)..] : relative;
    }

    private static string FolderOf(string relative)
    {
        var cut = relative.LastIndexOf('/');
        return cut >= 0 ? relative[..cut] : "";
    }

    private void Update(Action<LibraryDbContext> work)
    {
        lock (_write)
        {
            using var db = Context();
            work(db);
        }
    }
}

using System.Collections.Concurrent;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Logging;
using PdfLibrary.Utils;
using Serilog;

namespace PdfLibrary.Services;

/// <summary>Что изменилось в библиотеке — по этому лента решает, что перестроить.</summary>
[Flags]
public enum LibraryChange
{
    None      = 0,
    /// <summary>Источники, группы, метки: боковая панель и фильтры.</summary>
    Structure = 1,
    /// <summary>Состав документов: появились, пропали, переехали.</summary>
    Documents = 2,
    /// <summary>Состояния текста, страницы и форматы, метки на документах — карточки на месте.</summary>
    States    = 4,
    /// <summary>Состояние папок: проверяется, недоступна, проверена.</summary>
    Sources   = 8,
}

/// <summary>Найденное поиском у одного документа.</summary>
/// <param name="Snippet">Фрагмент текста с маркерами совпадений (<see cref="DocTextIndex.HitOpen"/>);
/// пусто — нашелся только по названию.</param>
/// <param name="Rank">Место в выдаче полнотекстового поиска (0 — лучшее); <c>int.MaxValue</c> — не по тексту.</param>
public readonly record struct SearchHit(string Snippet, int Rank, bool InText, bool ByName)
{
    /// <summary>Чем выше, тем раньше в ленте при порядке «По релевантности».</summary>
    public int Score => (ByName ? 1_000_000 : 0) + (InText ? 100_000 - Math.Min(Rank, 99_999) : 0);
}

/// <summary>Итог поиска: найденное и сломался ли поиск по тексту.</summary>
/// <param name="TextFailed">Кеш не ответил: нашлось только по названию, и строка состояния
/// должна сказать об этом, а не делать вид, что внутри документов ничего нет.</param>
public sealed record SearchOutcome(string Query, Dictionary<DocItem, SearchHit> Hits, bool TextFailed);

/// <summary>Состояние фонового чтения для строки состояния и кольца в панели разделов.</summary>
public readonly record struct ReadState(bool Running, int Done, int Total, TimeSpan? Left, string File)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>
/// Библиотека целиком: источники, группы, метки и документы в памяти, проверка папок,
/// наблюдение за ними, фоновое чтение и поиск. Одна на программу.
///
/// <b>Потоки.</b> Все поля памяти меняются только в UI-потоке: тяжелая работа (обход папок,
/// запись в базу, перепись кеша, поиск) идет в фоне, а ее итог переносится в UI-поток через
/// <c>post</c>. Поэтому лента читает документы без замков, а событие <see cref="Changed"/>
/// всегда приходит в UI-потоке. В тестах <c>post</c> выполняет действие сразу.
///
/// <b>Проверки папок идут по одной</b> (<see cref="_scanGate"/>): два обхода одной сетевой папки
/// параллельно не быстрее одного, а запись их итогов в базу все равно шла бы по очереди.
/// </summary>
public sealed class LibraryService : IDisposable
{
    /// <summary>Как часто перепроверять все папки, даже когда наблюдение молчит. Сетевые папки
    /// теряют уведомления о правках (сервер перезагрузили, сеть моргнула), и только сверка
    /// находит то, что изменилось за это время. Недоступная папка проверяется с тем же шагом.</summary>
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(10);

    /// <summary>Пауза после уведомления о правке до проверки: копирование сотни файлов — это
    /// сотни уведомлений, а проверка нужна одна, когда копирование закончилось.</summary>
    public static readonly TimeSpan WatchDelay = TimeSpan.FromSeconds(2);

    private readonly LibraryStore _store;
    private readonly DocIndexService? _reader;
    private readonly Action<Action> _post;

    private readonly Dictionary<string, DocItem> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DocItem> _byId = [];

    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _scanQueued = new();
    private readonly ConcurrentDictionary<int, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<int, Timer> _watchTimers = new();
    private readonly ConcurrentQueue<string> _readDone = new();
    private Timer? _recheck;
    private Timer? _readFlush;
    private readonly CancellationTokenSource _life = new();
    private bool _disposed;

    public LibraryService(LibraryStore store, DocIndexService? reader, Action<Action> post)
    {
        _store  = store;
        _reader = reader;
        _post   = post;

        if (_reader is not null)
        {
            _reader.Progress += OnReadProgress;
            _reader.Finished += OnReadFinished;
        }
    }

    // ── Состояние (только UI-поток) ───────────────────────────────────────────

    public List<SourceItem> Sources { get; } = [];
    public List<GroupItem> Groups { get; } = [];
    public List<TagItem> Tags { get; } = [];
    public List<DocItem> Documents { get; } = [];

    /// <summary>Кеш прочитанного; <c>null</c> — не открылся, поиск только по названию.</summary>
    public DocTextIndex? Index => _reader?.Index;

    public ReadState Reading { get; private set; }

    /// <summary>Идет проверка папок (хотя бы одной).</summary>
    public bool Checking { get; private set; }

    /// <summary>Что-то изменилось — приходит в UI-потоке.</summary>
    public event Action<LibraryChange>? Changed;

    /// <summary>Ход фонового чтения — приходит в UI-потоке.</summary>
    public event Action<ReadState>? ReadingChanged;

    /// <summary>В источнике появились новые документы (не при первой проверке нового источника):
    /// число и название источника. Для уведомления «Новые документы в источниках».</summary>
    public event Action<int, string>? NewDocumentsFound;

    /// <summary>Фоновое чтение закончилось само (не остановлено человеком): сколько прочитано.</summary>
    public event Action<int>? ReadingFinished;

    public DocItem? ById(int id) => _byId.GetValueOrDefault(id);

    public DocItem? ByPath(string path) => _byKey.GetValueOrDefault(DocTextIndex.KeyOf(path));

    public SourceItem? Source(int id) => Sources.FirstOrDefault(s => s.Id == id);

    // ── Старт ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Загрузить библиотеку из базы — в фоне, до показа окна. Документы приходят сразу, без
    /// обхода папок: лента открывается мгновенно и на выключенном сервере, а сверка с диском
    /// идет следом (<see cref="StartWork"/>).
    /// </summary>
    public void Load()
    {
        using var _ = Diag.Measure("загрузка библиотеки", Diag.UiOpen);
        var snapshot = _store.Load();
        Sources.AddRange(snapshot.Sources);
        Groups.AddRange(snapshot.Groups);
        Tags.AddRange(snapshot.Tags);
        foreach (var doc in snapshot.Documents) AddDocument(doc);

        // Состояния текста — из кеша, сразу: «не прочитан», «скан» и форматы листов известны
        // без чтения файлов. С диском кеш не сверяется — это сделает проверка папок.
        if (Index is { } index)
            try
            {
                var known = index.Known(_byKey.Keys.ToList());
                foreach (var (key, doc) in _byKey)
                    ApplyEntry(doc, known.TryGetValue(key, out var e) ? e : null, changed: false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Кеш прочитанного не ответил при загрузке библиотеки");
            }
    }

    /// <summary>Начать работу: проверить папки, включить наблюдение и сверку по расписанию.</summary>
    public void StartWork()
    {
        ApplyWatchSetting();
        CheckAll();
        _recheck = new Timer(_ => _post(() =>
        {
            if (AppSettings.Watch) CheckAll();
            else CheckUnavailable();
        }), null, RecheckEvery, RecheckEvery);
    }

    // ── Источники ─────────────────────────────────────────────────────────────

    /// <summary>Добавить папку источником и сразу проверить ее. <c>null</c> — эта папка уже в
    /// библиотеке (вернуть ее источник).</summary>
    public SourceItem? AddSource(string path, string name, int? groupId)
    {
        var folder = LibraryStore.NormalizeFolder(path);
        if (Sources.FirstOrDefault(s => string.Equals(s.Path, folder, StringComparison.Ordinal)) is { } existing)
            return existing;

        name = name.Trim();
        if (name.Length == 0) name = FolderName(folder);

        var source = _store.AddSource(name, folder, groupId);
        if (source is null) return null;
        source.FirstScan = true;
        Sources.Add(source);
        Log.Information("Источник добавлен: «{Name:l}» — {Path:l}", name, folder);
        Changed?.Invoke(LibraryChange.Structure);

        WatchSource(source);
        Check(source.Id);
        return source;
    }

    /// <summary>Название по умолчанию — имя папки (у корня диска — сам путь).</summary>
    public static string FolderName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        var name = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return name.Length > 0 ? name : path;
    }

    public void RenameSource(int id, string name)
    {
        if (Source(id) is not { } source || name.Trim().Length == 0) return;
        source.Name = name.Trim();
        _store.RenameSource(id, source.Name);
        Changed?.Invoke(LibraryChange.Structure);
    }

    /// <summary>Убрать источник из библиотеки. Файлы на диске остаются; уходят строки
    /// документов и метки на них.</summary>
    public void RemoveSource(int id)
    {
        if (Source(id) is not { } source) return;
        StopWatching(id);
        Sources.Remove(source);
        foreach (var doc in Documents.Where(d => d.SourceId == id).ToList()) RemoveDocument(doc);
        _store.RemoveSource(id);
        Log.Information("Источник убран из библиотеки: «{Name:l}» — {Path:l}", source.Name, source.Path);
        Changed?.Invoke(LibraryChange.Structure | LibraryChange.Documents);
    }

    /// <summary>Галочка источника в боковой панели.</summary>
    public void SetShown(IReadOnlyCollection<int> sourceIds, bool shown)
    {
        foreach (var source in Sources.Where(s => sourceIds.Contains(s.Id))) source.Shown = shown;
        _store.SetSourcesShown(sourceIds, shown);
        Changed?.Invoke(LibraryChange.Structure);
    }

    // ── Группы ────────────────────────────────────────────────────────────────

    public GroupItem AddGroup(string name, IReadOnlyCollection<int> sourceIds)
    {
        var group = _store.AddGroup(name.Trim(), sourceIds);
        Groups.Add(group);
        var order = 0;
        foreach (var source in Sources.Where(s => sourceIds.Contains(s.Id)).OrderBy(s => s.Position))
        {
            source.GroupId  = group.Id;
            source.Position = order++;
        }
        Changed?.Invoke(LibraryChange.Structure);
        return group;
    }

    public void SetSourceGroup(int sourceId, int? groupId)
    {
        if (Source(sourceId) is not { } source || source.GroupId == groupId) return;
        source.GroupId  = groupId;
        source.Position = Sources.Where(s => s.GroupId == groupId && s.Id != sourceId)
                                 .Select(s => s.Position).DefaultIfEmpty(-1).Max() + 1;
        _store.SetSourceGroup(sourceId, groupId);
        Changed?.Invoke(LibraryChange.Structure);
    }

    public void SetGroupExpanded(int groupId, bool expanded)
    {
        if (Groups.FirstOrDefault(g => g.Id == groupId) is not { } group || group.Expanded == expanded) return;
        group.Expanded = expanded;
        _store.SetGroupExpanded(groupId, expanded);
        Changed?.Invoke(LibraryChange.Structure);
    }

    // ── Метки ─────────────────────────────────────────────────────────────────

    public TagItem AddTag(string name, string color)
    {
        var tag = _store.AddTag(name.Trim(), color);
        Tags.Add(tag);
        Changed?.Invoke(LibraryChange.Structure);
        return tag;
    }

    /// <summary>Поставить или снять метку документам.</summary>
    public void SetTag(int tagId, IReadOnlyCollection<DocItem> docs, bool on)
    {
        if (docs.Count == 0) return;
        foreach (var doc in docs)
            if (on) doc.Tags.Add(tagId); else doc.Tags.Remove(tagId);
        _store.SetTag(tagId, docs.Select(d => d.Id).ToList(), on);
        Changed?.Invoke(LibraryChange.States);
    }

    // ── Проверка папок ────────────────────────────────────────────────────────

    /// <summary>Проверить все папки на новые, измененные и пропавшие файлы.</summary>
    public void CheckAll()
    {
        foreach (var source in Sources.ToList()) Check(source.Id);
    }

    private void CheckUnavailable()
    {
        foreach (var source in Sources.Where(s => s.UnavailableSince is not null).ToList()) Check(source.Id);
    }

    /// <summary>Поставить папку в очередь проверки. Уже стоящая в очереди второй раз не встает.</summary>
    public void Check(int sourceId)
    {
        if (_disposed || !_scanQueued.TryAdd(sourceId, 0)) return;
        if (!Checking)
        {
            Checking = true;
            Changed?.Invoke(LibraryChange.Sources);
        }

        var masks = SourceScanner.ParseMasks(AppSettings.SkipMasks);
        var ct    = _life.Token;
        _ = Task.Run(async () =>
        {
            await _scanGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _scanQueued.TryRemove(sourceId, out _);
                await ScanAsync(sourceId, masks, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "Проверка папки источника сорвалась (источник {Id})", sourceId);
            }
            finally
            {
                _scanGate.Release();
                if (_scanQueued.IsEmpty)
                    _post(() =>
                    {
                        if (!_scanQueued.IsEmpty || !Checking) return;
                        Checking = false;
                        Changed?.Invoke(LibraryChange.Sources);
                        // Сироты кеша убираются, когда диск уже ответил про папки — см. SweepOnceAsync.
                        _ = _reader?.SweepOnceAsync();
                    });
            }
        }, ct);
    }

    private async Task ScanAsync(int sourceId, IReadOnlyList<string> masks, CancellationToken ct)
    {
        // Снимок источника — из UI-потока: поля меняются только там.
        var source = await OnUi(() => Source(sourceId)).ConfigureAwait(false);
        if (source is null) return;

        var scan = SourceScanner.Scan(source.Path, masks, ct);
        ct.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;

        if (!scan.Available)
        {
            var since = source.UnavailableSince ?? now;
            _store.SetSourceState(sourceId, source.CheckedAt, since);
            if (source.UnavailableSince is null)
                Log.Error("Папка недоступна: {Path:l} ({Reason:l}), повтор через {Minutes} минут",
                          source.Path, scan.Error ?? "нет ответа", (int)RecheckEvery.TotalMinutes);
            _post(() =>
            {
                source.UnavailableSince = since;
                source.FirstScan = false;
                Changed?.Invoke(LibraryChange.Sources | LibraryChange.Structure);
            });
            return;
        }

        var changes = _store.ApplyScan(source, scan, now);
        _store.SetSourceState(sourceId, now, null);

        var wasUnavailable = source.UnavailableSince is not null;
        if (changes.Any || wasUnavailable)
            Log.Information("Проверка «{Name:l}»: новых {Added}, измененных {Updated}, пропавших {Missing}, " +
                            "{Count} PDF, {Seconds:0.#} с{Tail:l}",
                            source.Name, changes.Added.Count, changes.Updated.Count, changes.Missing.Count,
                            scan.Files.Count, scan.Elapsed.TotalSeconds,
                            wasUnavailable ? " — папка снова доступна" : "");
        if (!scan.Complete)
            Log.Warning("Проверка «{Name:l}» прошла не до конца: {Reason:l}", source.Name, scan.Error ?? "");

        // Что из найденного прочитано: перепись кеша по всем файлам источника. Идет здесь, в
        // фоне, — она спрашивает и кеш, и диск.
        var paths = scan.Files.Select(f => Path.Combine(source.Path, f.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
                              .ToList();
        DocSurvey? survey = null;
        Dictionary<string, DocIndexEntry>? known = null;
        if (Index is { } index && paths.Count > 0)
            try
            {
                survey = index.Survey(paths, AppSettings.MaxFileMb);
                known  = index.Known(survey.Files.Keys.ToList());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Кеш прочитанного не ответил при проверке «{Name:l}»", source.Name);
            }

        var first = source.FirstScan;
        _post(() =>
        {
            ApplyChanges(source, changes);
            source.CheckedAt = now;
            source.UnavailableSince = null;
            source.FirstScan = false;
            if (survey is not null)
                foreach (var (key, state) in survey.Files)
                    if (_byKey.TryGetValue(key, out var doc))
                        ApplyEntry(doc, known!.TryGetValue(key, out var e) ? e : null,
                                   changed: state.State == DocTextState.NotRead && known.ContainsKey(key));
            source.Count = Documents.Count(d => d.SourceId == source.Id && !d.Missing);
            Changed?.Invoke(LibraryChange.Documents | LibraryChange.States | LibraryChange.Sources | LibraryChange.Structure);

            if (!first && changes.Added.Count > 0) NewDocumentsFound?.Invoke(changes.Added.Count, source.Name);
        });

        // Новое и измененное — в очередь чтения, если читать сразу.
        if (survey is { Pending.Count: > 0 } && AppSettings.ReadAuto && _reader is not null)
            _reader.StartPending(survey.Pending);
    }

    private void ApplyChanges(SourceItem source, ScanChanges changes)
    {
        foreach (var doc in changes.Added)
            if (!_byId.ContainsKey(doc.Id)) AddDocument(doc);

        foreach (var fresh in changes.Updated)
        {
            if (!_byId.TryGetValue(fresh.Id, out var doc)) { AddDocument(fresh); continue; }
            var oldKey = DocTextIndex.KeyOf(doc.FullPath);
            LibraryStore.SetPath(doc, source.Path, fresh.RelativePath);
            doc.Size = fresh.Size;
            doc.Mtime = fresh.Mtime;
            doc.Missing = false;
            doc.DocType = fresh.DocType;
            var newKey = DocTextIndex.KeyOf(doc.FullPath);
            if (oldKey != newKey)
            {
                _byKey.Remove(oldKey);
                _byKey[newKey] = doc;
            }
        }

        foreach (var gone in changes.Missing)
            if (_byId.TryGetValue(gone.Id, out var doc)) doc.Missing = true;

        foreach (var id in changes.Removed)
            if (_byId.TryGetValue(id, out var doc)) RemoveDocument(doc);
    }

    private void AddDocument(DocItem doc)
    {
        Documents.Add(doc);
        _byId[doc.Id] = doc;
        _byKey[DocTextIndex.KeyOf(doc.FullPath)] = doc;
    }

    private void RemoveDocument(DocItem doc)
    {
        Documents.Remove(doc);
        _byId.Remove(doc.Id);
        _byKey.Remove(DocTextIndex.KeyOf(doc.FullPath));
    }

    // Что кеш знает про документ → поля карточки. changed — файл изменился после чтения: прежние
    // страницы и форматы остаются (они почти наверняка те же), а чип зовет перечитать.
    private static void ApplyEntry(DocItem doc, DocIndexEntry? entry, bool changed)
    {
        // Название из свойств PDF уточняет тип: «АБВГ.301122.001» с названием «Спецификация …» —
        // спецификация, а не чертеж детали.
        doc.DocType = DocTypes.Of(doc.FileName, doc.Folder, entry?.Title);
        if (entry is not { } e)
        {
            doc.TextState = DocTextState.NotRead;
            doc.Changed = false;
            return;
        }

        doc.Changed   = changed;
        doc.TextState = changed ? DocTextState.NotRead : e.Status switch
        {
            DocTextStatus.Ok          => DocTextState.Read,
            DocTextStatus.NoText      => DocTextState.NoText,
            DocTextStatus.Unsupported => DocTextState.Unsupported,
            _                         => DocTextState.Failed,
        };
        doc.Pages     = e.Pages;
        doc.Sheets    = e.Sheets;
        doc.Sheet     = e.Sheet;
        doc.InfoTitle = e.Title;
    }

    // ── Наблюдение за папками ─────────────────────────────────────────────────

    /// <summary>Включить или выключить наблюдение по настройке «Следить за папками».</summary>
    public void ApplyWatchSetting()
    {
        if (!AppSettings.Watch)
        {
            foreach (var id in _watchers.Keys.ToList()) StopWatching(id);
            return;
        }
        foreach (var source in Sources) WatchSource(source);
    }

    private void WatchSource(SourceItem source)
    {
        if (!AppSettings.Watch || _watchers.ContainsKey(source.Id)) return;
        try
        {
            if (!Directory.Exists(source.Path)) return;   // недоступную подхватит сверка по расписанию
            var watcher = new FileSystemWatcher(source.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                // Буфер больше стандартного: копирование папки на тысячу файлов переполняет 8 КБ.
                InternalBufferSize = 64 * 1024,
            };
            var id = source.Id;
            FileSystemEventHandler changed = (_, e) => { if (Relevant(e.FullPath)) Poke(id); };
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Changed += changed;
            watcher.Renamed += (_, e) => { if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) Poke(id); };
            // Переполнение буфера или обрыв сети: что именно изменилось — неизвестно, проверяем всю папку.
            watcher.Error += (_, e) =>
            {
                if (Diag.Throttle("watch-error-" + id, TimeSpan.FromMinutes(10)))
                    Log.Warning(e.GetException(), "Наблюдение за папкой прервалось: {Path:l}", source.Path);
                Poke(id);
            };
            watcher.EnableRaisingEvents = true;
            _watchers[id] = watcher;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Наблюдение за папкой не включилось: {Path:l} — останется сверка раз в {Minutes} минут",
                        source.Path, (int)RecheckEvery.TotalMinutes);
        }
    }

    // Уведомление касается PDF или папки (переименовали папку — поменялись пути всех ее файлов).
    private static bool Relevant(string path) =>
        DocTextExtractor.IsPdf(path) || string.IsNullOrEmpty(Path.GetExtension(path));

    // Отложенная проверка: серия уведомлений дает одну проверку после паузы.
    private void Poke(int sourceId)
    {
        var timer = _watchTimers.GetOrAdd(sourceId, id => new Timer(_ => _post(() => Check(id))));
        timer.Change(WatchDelay, Timeout.InfiniteTimeSpan);
    }

    private void StopWatching(int sourceId)
    {
        if (_watchers.TryRemove(sourceId, out var watcher))
            try { watcher.Dispose(); } catch { }
        if (_watchTimers.TryRemove(sourceId, out var timer)) timer.Dispose();
    }

    // ── Чтение ────────────────────────────────────────────────────────────────

    /// <summary>Прочитать все непрочитанные и измененные документы показанных источников.</summary>
    public void ReadNow(IEnumerable<DocItem>? docs = null)
    {
        if (_reader is null) return;
        var paths = (docs ?? Documents).Where(d => !d.Missing && (d.TextState == DocTextState.NotRead || d.Changed))
                                       .Select(d => d.FullPath).ToList();
        if (paths.Count > 0) _ = _reader.StartAsync(paths);
    }

    /// <summary>Остановить фоновое чтение. Прочитанное остается в кеше.</summary>
    public void StopReading() => _reader?.Stop();

    private void OnReadProgress(object? sender, DocIndexProgress p)
    {
        _readDone.Enqueue(p.Path);
        var state = new ReadState(true, p.Done, p.Total, p.Left, p.File);
        // Состояния карточек обновляются пачкой раз в полсекунды: перерисовывать ленту на
        // каждый документ при чтении десятка в секунду незачем.
        if (Volatile.Read(ref _readFlush) is null)
        {
            var timer = new Timer(_ => FlushRead());
            if (Interlocked.CompareExchange(ref _readFlush, timer, null) is null)
                timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
            else
                timer.Dispose();
        }
        _post(() =>
        {
            Reading = state;
            ReadingChanged?.Invoke(state);
        });
    }

    private void FlushRead()
    {
        var paths = new List<string>();
        while (_readDone.TryDequeue(out var p)) paths.Add(DocTextIndex.KeyOf(p));
        var timer = Interlocked.Exchange(ref _readFlush, null);
        timer?.Dispose();
        if (paths.Count == 0 || Index is not { } index) return;

        Dictionary<string, DocIndexEntry> known;
        try { known = index.Known(paths); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Кеш прочитанного не ответил после чтения");
            return;
        }

        _post(() =>
        {
            foreach (var key in paths)
                if (_byKey.TryGetValue(key, out var doc) && known.TryGetValue(key, out var e))
                    ApplyEntry(doc, e, changed: false);
            Changed?.Invoke(LibraryChange.States);
        });
    }

    private void OnReadFinished(object? sender, DocIndexRunEnd end)
    {
        FlushRead();
        if (end.Done > 0 && !end.Failed)
            Log.Information("Прочитано {Done} документов", end.Done);
        _post(() =>
        {
            Reading = new ReadState(false, end.Done, end.Total, null, "");
            ReadingChanged?.Invoke(Reading);
            if (end.Done > 0 && end.Done >= end.Total && !end.Failed) ReadingFinished?.Invoke(end.Done);
        });
    }

    /// <summary>
    /// Очистить кеш прочитанного и миниатюры. Чтение останавливается и дожидается выхода — иначе
    /// документ, разобранный до остановки, лег бы в кеш уже после очистки. Потом, если читать
    /// сразу, все документы встают в очередь заново.
    /// </summary>
    public async Task ClearCacheAsync()
    {
        if (_reader is { } reader)
        {
            await reader.StopAsync().ConfigureAwait(false);
            await Task.Run(() =>
            {
                reader.Index.Clear();
                DocThumbnail.Clear(AppSettings.ThumbsDir);
            }).ConfigureAwait(false);
            Log.Information("Кеш документов очищен");
        }

        await OnUi(() =>
        {
            foreach (var doc in Documents) ApplyEntry(doc, null, changed: false);
            Changed?.Invoke(LibraryChange.States);
            if (AppSettings.ReadAuto) ReadNow();
            return true;
        }).ConfigureAwait(false);
    }

    // ── Поиск ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Найти документы: по тексту внутри (кеш, FTS5) и по названию и имени файла (в памяти).
    /// Правила совпадения у обоих одни — <see cref="DocHighlight"/>: слова целиком, последнее
    /// по началу, все слова сразу.
    /// </summary>
    public async Task<SearchOutcome?> SearchAsync(string query, int snippetWords, CancellationToken ct = default)
    {
        var tokens = DocHighlight.Tokens(query);
        if (tokens.Count == 0) return null;

        List<DocHit> hits = [];
        var failed = false;
        if (Index is { } index)
            try
            {
                using var _ = Diag.Measure("поиск в тексте документов", Diag.UiQuery, query);
                hits = await index.SearchAsync(query, DocLibraryLimits.SearchResults, snippetWords).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failed = true;
                if (Diag.Throttle("search-failed", TimeSpan.FromMinutes(5)))
                    Log.Error(ex, "Поиск по тексту документов не сработал: «{Query:l}»", query);
            }
        else failed = true;
        ct.ThrowIfCancellationRequested();

        return await OnUi(() =>
        {
            var found = new Dictionary<DocItem, SearchHit>();
            for (var i = 0; i < hits.Count; i++)
                if (_byKey.TryGetValue(hits[i].Path, out var doc) && !found.ContainsKey(doc))
                    found[doc] = new SearchHit(hits[i].Snippet, i, true, false);

            foreach (var doc in Documents)
            {
                if (!NameMatches(doc, tokens)) continue;
                found[doc] = found.TryGetValue(doc, out var hit)
                    ? hit with { ByName = true }
                    : new SearchHit("", int.MaxValue, false, true);
            }
            return new SearchOutcome(query, found, failed);
        }).ConfigureAwait(false);
    }

    /// <summary>Все слова запроса есть в названии, имени файла или папке документа.</summary>
    public static bool NameMatches(DocItem doc, IReadOnlyList<string> tokens)
    {
        var text = doc.Title + " " + doc.FileName + " " + doc.Folder;
        foreach (var token in tokens)
            if (DocHighlight.Find(text, [token], lastIsPrefix: true).Count == 0) return false;
        return true;
    }

    // ── Служебное ─────────────────────────────────────────────────────────────

    // Выполнить в UI-потоке и дождаться результата.
    private Task<T> OnUi<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _life.Cancel();
        _recheck?.Dispose();
        _readFlush?.Dispose();
        foreach (var id in _watchers.Keys.ToList()) StopWatching(id);
        if (_reader is not null)
        {
            _reader.Progress -= OnReadProgress;
            _reader.Finished -= OnReadFinished;
        }
    }
}

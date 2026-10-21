using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Logging;
using PdfLibrary.Services;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Library;

/// <summary>Строка дерева источников в боковой панели: группа или источник.</summary>
public sealed partial class SourceRow : ObservableObject
{
    public int Id { get; init; }
    public bool IsGroup { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Уровень в дереве: источник в группе — 1, группа и источник сам по себе — 0.</summary>
    public int Depth { get; init; }
    public bool Expanded { get; init; }
    /// <summary>Строка статуса под именем: «читаю…», «недоступна»; пусто — статуса нет.</summary>
    public string Status { get; init; } = "";
    /// <summary>Папка недоступна — статус красный.</summary>
    public bool Unavailable { get; init; }

    [ObservableProperty] private bool _isChecked;
}

/// <summary>Строка метки в боковой панели — она же фильтр ленты.</summary>
public sealed record TagRow(int Id, string Name, IBrush Color, string CountText, bool IsActive);

/// <summary>Пункт фильтра «Тип документа».</summary>
public sealed partial class TypeRow : ObservableObject
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public string CountText => Count.ToString("N0");
    [ObservableProperty] private bool _isOn = true;
}

/// <summary>Пункт списка меток выбранных документов (панель «Метка» внизу ленты).</summary>
public sealed partial class TagPick : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public IBrush Color { get; init; } = Brushes.Gray;
    [ObservableProperty] private bool _isOn;
}

/// <summary>
/// Лента и боковая панель: дерево источников с галочками, метки, тип документа, порядок,
/// поиск, карточки, выбор нескольких документов. Список панели чтения — та же лента.
///
/// Лента пересобирается целиком при каждом изменении фильтров: документы в памяти, и сборка
/// на десятках тысяч — миллисекунды. Карточки при этом не создаются заново — они живут в
/// словаре по документу, поэтому галочки выбора и загруженные миниатюры переживают пересборку.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly Dictionary<DocItem, CardViewModel> _cardByDoc = [];
    private readonly HashSet<DocItem> _selected = [];
    private readonly HashSet<string> _hiddenTypes = new(StringComparer.Ordinal);
    private SearchOutcome? _search;
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;

    public LibraryViewModel(LibraryService library)
    {
        _library = library;
        _sort = AppSettings.Sort;
        ReadMetrics();
        _library.Changed += OnLibraryChanged;
        _library.ReadingChanged += _ => UpdateStatus();
        AppSettings.Changed += OnSettingChanged;
        // Шрифт крупнее — карточки собираются заново: высота карточки зависит от шрифта.
        FontScale.Changed += () => { OnPropertyChanged(nameof(Shape)); CardsRebuildRequested?.Invoke(); };
        RebuildSidebar();
        RebuildFeed();
    }

    /// <summary>
    /// Модель для живого примера карточки на экране настроек: без подписок на библиотеку и
    /// настройки — пример строится заново при каждой смене настроек карточки.
    /// </summary>
    public LibraryViewModel(LibraryService library, bool preview)
    {
        _library = library;
        _sort = AppSettings.Sort;
        ReadMetrics();
    }

    /// <summary>Название источника в примере карточки.</summary>
    public string? PreviewSource { get; set; }

    public LibraryService Library => _library;

    // ── Боковая панель ────────────────────────────────────────────────────────

    public ObservableCollection<SourceRow> SourceRows { get; } = [];
    public ObservableCollection<TagRow> TagRows { get; } = [];

    [ObservableProperty] private int? _tagFilter;

    public bool AllGroupsExpanded => _library.Groups.Count > 0 && _library.Groups.All(g => g.Expanded);

    public string SourceName(int id) => PreviewSource ?? _library.Source(id)?.Name ?? "";

    private void RebuildSidebar()
    {
        SourceRows.Clear();
        foreach (var group in _library.Groups.OrderBy(g => g.Position).ThenBy(g => g.Id))
        {
            var members = _library.Sources.Where(s => s.GroupId == group.Id).OrderBy(s => s.Position).ToList();
            SourceRows.Add(new SourceRow
            {
                Id = group.Id, IsGroup = true, Name = group.Name, Expanded = group.Expanded,
                IsChecked = members.Count > 0 && members.All(s => s.Shown),
            });
            if (group.Expanded)
                foreach (var source in members) SourceRows.Add(RowOf(source, 1));
        }
        foreach (var source in _library.Sources.Where(s => s.GroupId is null || _library.Groups.All(g => g.Id != s.GroupId))
                                               .OrderBy(s => s.Position).ThenBy(s => s.Id))
            SourceRows.Add(RowOf(source, 0));

        TagRows.Clear();
        foreach (var tag in _library.Tags.OrderBy(t => t.Position).ThenBy(t => t.Id))
        {
            var count = _library.Documents.Count(d => d.Tags.Contains(tag.Id));
            TagRows.Add(new TagRow(tag.Id, tag.Name, BrushOf(tag.Color), count.ToString("N0"), TagFilter == tag.Id));
        }

    }

    private static SourceRow RowOf(SourceItem source, int depth) => new()
    {
        Id = source.Id, Name = source.Name, Depth = depth, IsChecked = source.Shown,
        Unavailable = source.UnavailableSince is not null,
        Status = source.UnavailableSince is not null ? "недоступна — документы сохранены"
               : source.FirstScan ? "читаю папку…"
               : "",
    };

    /// <summary>Галочка строки дерева: источник — сам, группа — все ее источники.</summary>
    public void ToggleRow(SourceRow row)
    {
        var ids = row.IsGroup
            ? _library.Sources.Where(s => s.GroupId == row.Id).Select(s => s.Id).ToList()
            : [row.Id];
        if (ids.Count == 0) return;
        _library.SetShown(ids, row.IsChecked);
    }

    public void FlipGroup(SourceRow row)
    {
        if (row.IsGroup) _library.SetGroupExpanded(row.Id, !row.Expanded);
    }

    public void ToggleAllGroups()
    {
        var expand = !AllGroupsExpanded;
        foreach (var group in _library.Groups.ToList()) _library.SetGroupExpanded(group.Id, expand);
    }

    /// <summary>Все источники отмечены — кнопка «Включить все» выключает.</summary>
    public bool AllSourcesShown => _library.Sources.Count > 0 && _library.Sources.All(s => s.Shown);

    /// <summary>«Включить все» / «Выключить все» в шапке боковой панели.</summary>
    public void ToggleAllShown()
    {
        if (_library.Sources.Count == 0) return;
        _library.SetShown(_library.Sources.Select(s => s.Id).ToList(), !AllSourcesShown);
    }

    public void PickTag(TagRow row)
    {
        TagFilter = TagFilter == row.Id ? null : row.Id;
        RebuildSidebar();
        RebuildFeed();
    }

    public static IBrush BrushOf(string color) =>
        Color.TryParse(color, out var c) ? new SolidColorBrush(c) : Brushes.Gray;

    private readonly Dictionary<int, TagChip> _chipCache = [];

    public IReadOnlyList<TagChip> TagChips(DocItem doc) =>
        _library.Tags.Where(t => doc.Tags.Contains(t.Id))
                     .OrderBy(t => t.Position)
                     .Select(t => _chipCache.TryGetValue(t.Id, out var c) && c.Name == t.Name
                         ? c
                         : _chipCache[t.Id] = new TagChip(t.Name, BrushOf(t.Color)))
                     .ToList();

    // ── Порядок и тип документа ───────────────────────────────────────────────

    [ObservableProperty] private string _sort;

    partial void OnSortChanged(string value)
    {
        AppSettings.Sort = value;
        RebuildFeed();
    }

    public ObservableCollection<TypeRow> TypeRows { get; } = [];

    /// <summary>«Тип документа» или «Тип документа: 3», когда часть типов снята.</summary>
    public string TypeLabel => _hiddenTypes.Count == 0
        ? "Тип документа"
        : $"Тип документа: {TypeRows.Count(t => t.IsOn)}";

    /// <summary>Отмеченные типы для окна фильтра; null — фильтр снят (все типы).</summary>
    public IReadOnlySet<string>? ChosenTypes => _hiddenTypes.Count == 0
        ? null
        : TypeRows.Where(t => !_hiddenTypes.Contains(t.Name)).Select(t => t.Name).ToHashSet();

    /// <summary>Типы со счетчиками для окна фильтра — по ленте без учета самого типа.</summary>
    public List<(string Value, int Count)> TypeCounts() => TypeRows.Select(t => (t.Name, t.Count)).ToList();

    /// <summary>
    /// Итог окна «Тип документа»: <paramref name="chosen"/> — отмеченные типы, null — все.
    /// Хранится снятое, а не отмеченное: новый тип, появившийся в ленте позже, виден сразу.
    /// </summary>
    public void SetTypes(IReadOnlySet<string>? chosen)
    {
        _hiddenTypes.Clear();
        if (chosen is not null)
            foreach (var t in TypeRows)
                if (!chosen.Contains(t.Name)) _hiddenTypes.Add(t.Name);
        RebuildFeed();
    }

    // ── Поиск ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _query = "";

    /// <summary>Запрос, по которому собрана лента; пусто — поиска нет.</summary>
    [ObservableProperty] private string _appliedQuery = "";

    public bool Searching => _search is not null;

    /// <summary>Найти: по тексту внутри PDF и по названию. Пустой запрос — сброс поиска.</summary>
    public void ApplySearch() => Search(Query.Trim());

    private async void Search(string query)
    {
        var version = ++_searchVersion;
        _searchCts?.Cancel();

        if (DocHighlight.Tokens(query).Count == 0)
        {
            _search = null;
            AppliedQuery = "";
            RebuildFeed();
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            var outcome = await _library.SearchAsync(query, Shape.SnippetWords, cts.Token);
            if (version != _searchVersion) return;
            _search = outcome;
            AppliedQuery = query;
            RebuildFeed();
        }
        catch (OperationCanceledException) { }
    }

    public void ClearSearch()
    {
        Query = "";
        ApplySearch();
    }

    // ── Лента ─────────────────────────────────────────────────────────────────

    /// <summary>Карточки ленты в порядке показа.</summary>
    [ObservableProperty] private IReadOnlyList<CardViewModel> _cards = [];

    partial void OnCardsChanged(IReadOnlyList<CardViewModel> value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    public bool IsEmpty => Cards.Count == 0;

    public string EmptyText => Searching
        ? "Ничего не нашлось. Сканы без текстового слоя ищутся только по названию и имени файла."
        : _library.Sources.Count == 0
            ? "Источников пока нет — добавьте папку с PDF кнопкой «+»."
            : "В отмеченных источниках нет документов.";

    private FeedQuery CurrentQuery(bool withHits = true)
    {
        Dictionary<DocItem, FeedHit>? hits = null;
        if (withHits && _search is { } s)
            hits = s.Hits.ToDictionary(kv => kv.Key, kv =>
                new FeedHit(kv.Value.Snippet, kv.Value.InText, kv.Value.ByName, kv.Value.Score));
        return new FeedQuery(
            _library.Sources.Where(x => x.Shown).Select(x => x.Id).ToHashSet(),
            _hiddenTypes, TagFilter, AppSettings.FeedMissing, AppSettings.FeedScans, Sort, hits);
    }

    /// <summary>Пересобрать ленту, числа фильтра типов и строку состояния.</summary>
    public void RebuildFeed()
    {
        using var _ = Diag.Measure("сборка ленты", Diag.UiQuery, $"{_library.Documents.Count} документов");
        var query = CurrentQuery();
        var docs  = FeedFilter.Apply(_library.Documents, query);

        var cards = new List<CardViewModel>(docs.Count);
        foreach (var doc in docs)
        {
            if (!_cardByDoc.TryGetValue(doc, out var card)) _cardByDoc[doc] = card = new CardViewModel(doc, this);
            card.Snippet = query.Hits is { } hits && hits.TryGetValue(doc, out var hit) ? hit.Snippet : "";
            cards.Add(card);
        }
        Cards = cards;

        // Пропавшие из библиотеки документы — из словаря карточек и из выбора.
        if (_cardByDoc.Count > _library.Documents.Count)
        {
            var alive = _library.Documents.ToHashSet();
            foreach (var gone in _cardByDoc.Keys.Where(d => !alive.Contains(d)).ToList()) _cardByDoc.Remove(gone);
            _selected.RemoveWhere(d => !alive.Contains(d));
        }

        RebuildTypes();
        UpdateStatus();
        UpdateSelection();
    }

    private void RebuildTypes()
    {
        var counts = FeedFilter.TypeCounts(_library.Documents, CurrentQuery());
        // Снятый тип, которого больше нет в ленте, остается в списке с нулем — иначе его галочку
        // нечем было бы вернуть.
        foreach (var hidden in _hiddenTypes)
            if (counts.All(c => c.Type != hidden)) counts.Add((hidden, 0));

        TypeRows.Clear();
        foreach (var (type, count) in counts)
            TypeRows.Add(new TypeRow { Name = type, Count = count, IsOn = !_hiddenTypes.Contains(type) });
        OnPropertyChanged(nameof(TypeLabel));
    }

    // ── Строка состояния ──────────────────────────────────────────────────────

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusAction = "";
    [ObservableProperty] private double _readFraction;
    [ObservableProperty] private bool _readingNow;

    private void UpdateStatus()
    {
        var reading = _library.Reading;
        ReadingNow = reading.Running;
        ReadFraction = reading.Fraction;

        var n = Cards.Count;
        string text;
        if (_search is { } s)
        {
            if (n == 0) text = "Ничего не найдено";
            else
            {
                var inText = Cards.Count(c => s.Hits.TryGetValue(c.Doc, out var h) && h.InText);
                text = $"Найдено {n:N0}: {inText:N0} внутри текста, {n - inText:N0} по названию и имени файла";
            }
            if (s.TextFailed)
                text += _library.Index is null
                    ? " · поиск внутри документов недоступен: кеш не открылся"
                    : " · поиск внутри документов не сработал, подробности в журнале";
            else if (reading.Running)
                text += " · пока читаю новые, список обновится сам";
        }
        else
        {
            text = $"{n:N0} {Plural.Documents(n)} в ленте";
            if (reading.Running)
                text += $" · читаю новые документы: {reading.Done:N0} из {reading.Total:N0}" +
                        (reading.Left is { } left ? " · " + EtaEstimator.Format(left.TotalSeconds) : "");
        }

        var unread = UnreadCount();
        if (!reading.Running && unread > 0)
            text += $" · не прочитано {unread:N0}";

        StatusText = text;
        StatusAction = reading.Running ? "Остановить чтение" : unread > 0 && _library.Index is not null ? "Прочитать" : "";
    }

    private int UnreadCount() =>
        _library.Documents.Count(d => !d.Missing && (d.TextState == DocTextState.NotRead || d.Changed) &&
                                      _library.Source(d.SourceId)?.Shown == true);

    /// <summary>Ссылка справа в строке состояния: остановить чтение или прочитать непрочитанное.</summary>
    public void RunStatusAction()
    {
        if (_library.Reading.Running) _library.StopReading();
        else _library.ReadNow(_library.Documents.Where(d => _library.Source(d.SourceId)?.Shown == true));
    }

    // ── Выбор нескольких документов ───────────────────────────────────────────

    [ObservableProperty] private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;
    public string SelectedText => $"Выбрано {SelectedCount}";

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedText));
    }

    public IReadOnlyCollection<DocItem> Selected => _selected;

    internal void OnCardSelectionChanged(CardViewModel card, bool on)
    {
        if (on) _selected.Add(card.Doc); else _selected.Remove(card.Doc);
        SelectedCount = _selected.Count;
    }

    /// <summary>Клик по карточке без клавиш: выбрана только она.</summary>
    public void SelectOnly(CardViewModel card)
    {
        foreach (var doc in _selected.ToList())
            if (doc != card.Doc && _cardByDoc.TryGetValue(doc, out var other)) other.IsSelected = false;
        _selected.RemoveWhere(d => d != card.Doc);
        card.IsSelected = true;
        SelectedCount = _selected.Count;
    }

    /// <summary>Ctrl+Shift+клик: выбрать карточки ленты от <paramref name="anchor"/> до <paramref name="card"/>.</summary>
    public void SelectRange(CardViewModel anchor, CardViewModel card)
    {
        var a = Cards.ToList().IndexOf(anchor);
        var b = Cards.ToList().IndexOf(card);
        if (a < 0 || b < 0) { card.IsSelected = !card.IsSelected; return; }
        for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) Cards[i].IsSelected = true;
    }

    public void ClearSelection()
    {
        foreach (var doc in _selected.ToList())
            if (_cardByDoc.TryGetValue(doc, out var card)) card.IsSelected = false;
        _selected.Clear();
        SelectedCount = 0;
    }

    private void UpdateSelection() => SelectedCount = _selected.Count;

    /// <summary>Метки для панели «Метка»: галочка стоит, если метка есть у всех выбранных.</summary>
    public List<TagPick> TagPicks() =>
        _library.Tags.OrderBy(t => t.Position).Select(t => new TagPick
        {
            Id = t.Id, Name = t.Name, Color = BrushOf(t.Color),
            IsOn = _selected.Count > 0 && _selected.All(d => d.Tags.Contains(t.Id)),
        }).ToList();


    public void SetTagOnSelected(int tagId, bool on) => _library.SetTag(tagId, _selected.ToList(), on);

    // ── Настройки карточек ────────────────────────────────────────────────────

    /// <summary>Геометрия карточки по настройке «Размер карточки» — таблица <see cref="DocTiles"/>.</summary>
    public DocTileShape Shape { get; private set; } = DocTiles.Of(CardSize.Medium);

    /// <summary>Карточки надо собрать заново: сменился размер, шрифт, миниатюры или тема.</summary>
    public event Action? CardsRebuildRequested;
    public bool ShowThumbnails { get; private set; } = true;
    public bool ShowPath { get; private set; } = true;
    public bool ShowMeta { get; private set; } = true;
    public bool ShowType { get; private set; } = true;

    private void ReadMetrics()
    {
        Shape          = DocTiles.Of(AppSettings.CardSize);
        ShowThumbnails = AppSettings.CardThumbnail;
        ShowPath       = AppSettings.CardPath;
        ShowMeta       = AppSettings.CardMeta;
        ShowType       = AppSettings.CardType;
    }

    private void OnSettingChanged(string key)
    {
        switch (key)
        {
            case AppSettings.KeyCardSize:
            case AppSettings.KeyCardThumb:
            case AppSettings.KeyCardPath:
            case AppSettings.KeyCardMeta:
            case AppSettings.KeyCardType:
                ReadMetrics();
                OnPropertyChanged(nameof(Shape));
                foreach (var card in _cardByDoc.Values) card.Refresh();
                CardsRebuildRequested?.Invoke();
                // Сниппет другой длины — поиск заново.
                if (key == AppSettings.KeyCardSize && _search is not null) Search(AppliedQuery);
                break;
            case AppSettings.KeyFeedMissing:
            case AppSettings.KeyFeedScans:
                RebuildFeed();
                break;
            case AppSettings.KeyWatch:
                _library.ApplyWatchSetting();
                break;
        }
    }

    // ── Изменения библиотеки ──────────────────────────────────────────────────

    private void OnLibraryChanged(LibraryChange change)
    {
        if (change.HasFlag(LibraryChange.Structure) || change.HasFlag(LibraryChange.Sources) ||
            change.HasFlag(LibraryChange.States))
            RebuildSidebar();

        if (change.HasFlag(LibraryChange.States))
            foreach (var card in _cardByDoc.Values) card.Refresh();

        if (change.HasFlag(LibraryChange.Documents) || change.HasFlag(LibraryChange.Structure) ||
            change.HasFlag(LibraryChange.States))
        {
            // Поиск по новому составу: документ, который только что прочитан, мог начать
            // находиться по тексту.
            if (_search is not null && change.HasFlag(LibraryChange.States) && _library.Reading.Running)
                ScheduleResearch();
            RebuildFeed();
        }
        else
            UpdateStatus();
    }

    private DateTime _lastResearch;

    // Во время чтения поиск обновляется не чаще раза в несколько секунд: «список обновится сам».
    private void ScheduleResearch()
    {
        if (DateTime.UtcNow - _lastResearch < TimeSpan.FromSeconds(5)) return;
        _lastResearch = DateTime.UtcNow;
        Search(AppliedQuery);
    }
}

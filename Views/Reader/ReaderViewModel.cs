using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Logging;
using PdfLibrary.Utils;
using Serilog;

namespace PdfLibrary.Views.Reader;

/// <summary>Совпадение на странице: страница и прямоугольники подсвеченных слов в пунктах.</summary>
public sealed record PageHit(int Page, IReadOnlyList<DocPageBox> Boxes);

/// <summary>Миниатюра страницы на рельсе слева: номер, рамка текущей, число совпадений.</summary>
public sealed partial class PageThumb : ObservableObject
{
    public int Index { get; init; }
    public string Number => (Index + 1).ToString();
    public double Width { get; init; }
    public double Height { get; init; }
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private int _hits;
    public bool HasHits => Hits > 0;
    partial void OnHitsChanged(int value) => OnPropertyChanged(nameof(HasHits));
}

/// <summary>
/// Панель чтения: открытый документ, его страницы, поиск по документу и масштаб.
///
/// Документ открывается в фоне (<see cref="DocPageSource.Open"/>): размеры страниц нужны сразу —
/// по ним строится лента листов, — а картинки рисуются по мере прокрутки. Совпадения ищутся
/// по словам страниц в фоне, страница за страницей, и счетчик растет по ходу: на томе в сотни
/// страниц ждать полного счета перед первой подсветкой незачем.
/// </summary>
public sealed partial class ReaderViewModel : ObservableObject, IDisposable
{
    /// <summary>Шаги масштаба. 100 % — натуральный размер листа (1 пункт = 1/72 дюйма).</summary>
    public static readonly double[] ZoomSteps = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0];

    /// <summary>Пунктов в экранной точке при 100 %: экран 96 точек на дюйм, PDF — 72 пункта.</summary>
    public const double PointsToDip = 96.0 / 72.0;

    private DocPageSource? _source;
    private CancellationTokenSource? _openCts;
    private CancellationTokenSource? _findCts;
    private readonly Dictionary<int, (int Width, Bitmap Bitmap)> _pages = [];
    private readonly LinkedList<int> _pageOrder = new();
    private readonly HashSet<(int Page, int Width)> _rendering = [];
    private readonly List<PageHit> _hits = [];

    [ObservableProperty] private DocItem? _doc;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private int _currentPage;
    [ObservableProperty] private int _currentHit = -1;
    [ObservableProperty] private string _findQuery = "";
    [ObservableProperty] private bool _counting;

    public IReadOnlyList<DocPageSize> Sizes { get; private set; } = [];
    public List<PageThumb> Thumbs { get; private set; } = [];
    public IReadOnlyList<PageHit> Hits => _hits;
    public int PageCount => Sizes.Count;
    public int TotalPages { get; private set; }

    /// <summary>Перерисовать листы: пришла картинка страницы, сменились совпадения.</summary>
    public event Action? PagesChanged;

    /// <summary>Документ открыт и размеры известны — перестроить ленту листов.</summary>
    public event Action? Opened;

    /// <summary>Перейти к совпадению: прокрутить к нему ленту листов.</summary>
    public event Action<PageHit>? HitFocused;

    public string ZoomText => $"{Zoom * 100:0}%";

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomText));
        PagesChanged?.Invoke();
    }

    partial void OnCurrentPageChanged(int value)
    {
        for (var i = 0; i < Thumbs.Count; i++) Thumbs[i].IsCurrent = i == value;
        OnPropertyChanged(nameof(StatusLeft));
        if (Doc is { } doc) AppSettings.LastDocument = (doc.FullPath, value);
    }

    partial void OnCurrentHitChanged(int value)
    {
        OnPropertyChanged(nameof(Counter));
        OnPropertyChanged(nameof(StatusLeft));
        PagesChanged?.Invoke();
    }

    partial void OnCountingChanged(bool value) => OnPropertyChanged(nameof(Counter));

    /// <summary>«3 из 17» или «нет совпадений»; пока страницы считаются — «3 из 17…».</summary>
    public string Counter
    {
        get
        {
            if (DocHighlight.Tokens(FindQuery).Count == 0) return "";
            if (_hits.Count == 0) return Counting ? "ищу…" : "нет совпадений";
            var current = CurrentHit >= 0 ? CurrentHit + 1 : 0;
            return $"{current} из {_hits.Count}{(Counting ? "…" : "")}";
        }
    }

    /// <summary>«Совпадение 3 из 17 · страница 4 из 48» или «Страница 4 из 48».</summary>
    public string StatusLeft
    {
        get
        {
            if (!IsOpen) return "";
            var page = $"страница {CurrentPage + 1} из {TotalPages}";
            if (TotalPages > PageCount) page += $" (показаны первые {PageCount})";
            return _hits.Count > 0 && CurrentHit >= 0
                ? $"Совпадение {CurrentHit + 1} из {_hits.Count} · {page}"
                : char.ToUpperInvariant(page[0]) + page[1..];
        }
    }

    /// <summary>«Текстовый слой есть · A4 × 48» или «Скан без текстового слоя · A4 × 12».</summary>
    public string StatusRight
    {
        get
        {
            if (Doc is not { } doc) return "";
            var layer = doc.TextState switch
            {
                DocTextState.Read     => "Текстовый слой есть",
                DocTextState.NoText   => "Скан без текстового слоя",
                DocTextState.Failed   => "Текст не прочитался",
                _                     => "Текст еще не прочитан",
            };
            return doc.Sheets.Length > 0 ? $"{layer} · {doc.Sheets}" : layer;
        }
    }

    // ── Открытие ──────────────────────────────────────────────────────────────

    /// <summary>Открыть документ; <paramref name="query"/> — слова поиска ленты для подсветки.</summary>
    public async void Open(DocItem doc, string sourceName, string query, int page = 0)
    {
        if (ReferenceEquals(Doc, doc) && IsOpen)
        {
            if (query != FindQuery) { FindQuery = query; Find(); }
            return;
        }

        Close();
        Doc = doc;
        Title = doc.Title;
        Subtitle = SubtitleOf(doc, sourceName);
        FindQuery = query;
        OnPropertyChanged(nameof(StatusRight));

        if (doc.Missing)
        {
            Error = "Файла нет на диске — документ удалили или переименовали.";
            return;
        }

        var cts = _openCts = new CancellationTokenSource();
        DocPageSource? source;
        try
        {
            using var _ = Diag.Measure("открытие документа", Diag.UiOpen, doc.FileName);
            source = await Task.Run(() => DocPageSource.Open(doc.FullPath, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) { source?.Dispose(); return; }

        if (source is null)
        {
            Error = "Документ не открылся: файл поврежден, защищен паролем или занят другой программой.";
            Log.Warning("Панель чтения: документ не открылся: {Path:l}", doc.FullPath);
            return;
        }

        _source = source;
        Sizes = Enumerable.Range(0, source.PageCount).Select(source.Size).ToList();
        TotalPages = source.TotalPages;
        Thumbs = Sizes.Select((s, i) => new PageThumb
        {
            Index = i, Width = 84, Height = s.Width > 0 ? Math.Clamp(84 * s.Height / s.Width, 40, 160) : 119,
        }).ToList();
        IsOpen = true;
        CurrentPage = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        OnPropertyChanged(nameof(Thumbs));
        OnPropertyChanged(nameof(PageCount));
        Opened?.Invoke();
        Find();
    }

    private static string SubtitleOf(DocItem doc, string sourceName)
    {
        var parts = new List<string> { sourceName };
        if (doc.Folder.Length > 0) parts.Add(doc.Folder.Replace("/", " / "));
        parts.Add(doc.FileName);
        if (doc.Pages > 0) parts.Add($"{doc.Pages} стр.");
        parts.Add(Utils.Sizes.Text(doc.Size));
        return string.Join(" · ", parts);
    }

    public void Close()
    {
        _openCts?.Cancel();
        _findCts?.Cancel();
        var source = _source;
        _source = null;
        if (source is not null) _ = Task.Run(source.Dispose);
        _pages.Clear();
        _pageOrder.Clear();
        _rendering.Clear();
        _hits.Clear();
        Sizes = [];
        Thumbs = [];
        IsOpen = false;
        Error = "";
        CurrentHit = -1;
        Doc = null;
        OnPropertyChanged(nameof(Thumbs));
        OnPropertyChanged(nameof(Counter));
    }

    // ── Картинки страниц ──────────────────────────────────────────────────────

    /// <summary>Готовая картинка страницы (любой ширины — пока рисуется нужная, лист показывает прежнюю).</summary>
    public Bitmap? PageImage(int page) => _pages.TryGetValue(page, out var p) ? p.Bitmap : null;

    /// <summary>Нарисовать страницу шириной <paramref name="pixelWidth"/>, если такой картинки еще нет.</summary>
    public void RequestPage(int page, int pixelWidth)
    {
        if (_source is not { } source || page < 0 || page >= PageCount) return;
        pixelWidth = Math.Clamp(pixelWidth, 120, 2400);
        if (_pages.TryGetValue(page, out var have) && Math.Abs(have.Width - pixelWidth) < 8) return;
        if (!_rendering.Add((page, pixelWidth))) return;

        var token = _openCts?.Token ?? CancellationToken.None;
        _ = Task.Run(() =>
        {
            try
            {
                var png = source.Render(page, pixelWidth, token);
                if (png is null) return;
                var bitmap = new Bitmap(new MemoryStream(png));
                Dispatcher.UIThread.Post(() =>
                {
                    _rendering.Remove((page, pixelWidth));
                    if (!ReferenceEquals(_source, source)) return;
                    _pages[page] = (pixelWidth, bitmap);
                    _pageOrder.Remove(page);
                    _pageOrder.AddFirst(page);
                    // Держим в памяти два десятка листов: остальные перерисуются при возврате.
                    while (_pageOrder.Count > 24 && _pageOrder.Last is { } last)
                    {
                        _pages.Remove(last.Value);
                        _pageOrder.RemoveLast();
                    }
                    PagesChanged?.Invoke();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (Diag.Throttle("page-render", TimeSpan.FromMinutes(1)))
                    Log.Warning(ex, "Страница не нарисовалась: {Page}", page + 1);
            }
        }, token);
    }

    /// <summary>Нарисовать миниатюру страницы для рельса.</summary>
    public void RequestThumb(PageThumb thumb, int pixelWidth)
    {
        if (thumb.Image is not null || _source is not { } source) return;
        var token = _openCts?.Token ?? CancellationToken.None;
        _ = Task.Run(() =>
        {
            try
            {
                var png = source.Render(thumb.Index, pixelWidth, token);
                if (png is null) return;
                var bitmap = new Bitmap(new MemoryStream(png));
                Dispatcher.UIThread.Post(() => thumb.Image = bitmap);
            }
            catch { /* миниатюра не нарисовалась — останется пустой лист с номером */ }
        }, token);
    }

    // ── Поиск по документу ────────────────────────────────────────────────────

    /// <summary>
    /// Строка, по которой найдены нынешние совпадения. Совпадает со строкой поиска — Enter и F3
    /// идут к следующему совпадению; строку поменяли — сначала новый поиск.
    /// </summary>
    public string SearchedQuery { get; private set; } = "";

    /// <summary>Найти слова поиска на страницах: в фоне, страница за страницей.</summary>
    public void Find()
    {
        _findCts?.Cancel();
        SearchedQuery = FindQuery;
        _hits.Clear();
        foreach (var t in Thumbs) t.Hits = 0;
        CurrentHit = -1;
        OnPropertyChanged(nameof(Counter));
        PagesChanged?.Invoke();

        var tokens = DocHighlight.Tokens(FindQuery);
        if (tokens.Count == 0 || _source is not { } source) { Counting = false; return; }

        var cts = _findCts = new CancellationTokenSource();
        var count = PageCount;
        Counting = true;
        var first = true;
        _ = Task.Run(() =>
        {
            var batch = new List<PageHit>();
            for (var page = 0; page < count && !cts.IsCancellationRequested; page++)
            {
                var words = source.Words(page);
                if (words.Count > 0)
                {
                    var texts = words.Select(w => w.Text).ToList();
                    foreach (var (a, b) in DocHighlight.FindInWords(texts, tokens))
                        batch.Add(new PageHit(page, words.Skip(a).Take(b - a + 1).Select(w => w.Box).ToList()));
                }
                // Пачками: на томе в сотни страниц обновлять экран на каждую страницу незачем.
                if (batch.Count > 0 && (page % 8 == 7 || page == count - 1))
                {
                    var ready = batch;
                    batch = [];
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cts.IsCancellationRequested) return;
                        _hits.AddRange(ready);
                        foreach (var hit in ready) Thumbs[hit.Page].Hits++;
                        if (first)
                        {
                            first = false;
                            // Первое совпадение — не раньше текущей страницы: человек открыл
                            // документ на месте, где закончил читать.
                            var at = _hits.FindIndex(h => h.Page >= CurrentPage);
                            GoToHit(at >= 0 ? at : 0);
                        }
                        OnPropertyChanged(nameof(Counter));
                        PagesChanged?.Invoke();
                    });
                }
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested) return;
                Counting = false;
                OnPropertyChanged(nameof(StatusLeft));
            });
        }, cts.Token);
    }

    public void NextHit() { if (_hits.Count > 0) GoToHit((CurrentHit + 1) % _hits.Count); }

    public void PrevHit() { if (_hits.Count > 0) GoToHit((CurrentHit - 1 + _hits.Count) % _hits.Count); }

    private void GoToHit(int index)
    {
        if (index < 0 || index >= _hits.Count) return;
        CurrentHit = index;
        HitFocused?.Invoke(_hits[index]);
    }

    // ── Масштаб ───────────────────────────────────────────────────────────────

    public void ZoomIn()
    {
        var next = ZoomSteps.FirstOrDefault(z => z > Zoom + 0.001);
        if (next > 0) Zoom = next;
    }

    public void ZoomOut()
    {
        var prev = ZoomSteps.LastOrDefault(z => z < Zoom - 0.001);
        if (prev > 0) Zoom = prev;
    }

    /// <summary>Масштаб при открытии: самый крупный шаг, при котором самый широкий лист влезает
    /// в ширину, но не больше 100 % — чертеж A1 иначе открылся бы огромным.</summary>
    public void FitWidth(double available)
    {
        if (Sizes.Count == 0 || available <= 0) return;
        var widest = Sizes.Max(s => s.Width) * PointsToDip;
        var fit = widest > 0 ? available / widest : 1;
        Zoom = ZoomSteps.Where(z => z <= Math.Min(1.0, fit) + 0.001).DefaultIfEmpty(ZoomSteps[0]).Max();
    }

    public void Dispose() => Close();
}

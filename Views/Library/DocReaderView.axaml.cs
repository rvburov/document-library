using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using PdfLibrary.Controls.Behaviors;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Панель чтения документа: настоящие страницы PDF с подсветкой найденного прямо на листах.
/// Документ читают таким, какой он есть, — отдельного текстового режима нет.
///
/// Панель живет в правой колонке главного окна, у ее левого края разделитель и ручка ширины
/// (см. MainWindow.ApplyColumns). Документ здесь только читают, поэтому нет ни одного органа,
/// меняющего файл.
///
/// <b>Почему ListBox.</b> Страниц бывает сотни: листы лежат списком с виртуализацией, картинки
/// рисуются только у видимых, а переход к совпадению — это <c>ScrollIntoView</c> по номеру
/// страницы и подвод слова к глазам внутри нее. Выделения у списка нет: выбирать тут нечего,
/// текущее совпадение видно по цвету подсветки.
/// </summary>
public partial class DocReaderView : UserControl
{
    /// <summary>Закрыть панель — команда клавиатуры.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Показать список рядом с документом или раскрыть документ на всю ширину.</summary>
    public event EventHandler<bool>? ListPanelToggled;

    /// <summary>Документ, открытый сейчас; null — панель пуста.</summary>
    public DocItem? Document { get; private set; }

    // ── Для стенда экранов: что панель показывает сейчас ──
    public string CounterText  => CounterLabel.Text ?? "";
    public int    CurrentMatch => _current;
    public int    PageCount    => _pages.Count;
    public bool   PagesScanned => _pagesScanned;
    public string StateText    => StateLabel.IsVisible ? StateLabel.Text ?? "" : "";
    public string NoteText     => CutLabel.IsVisible ? CutLabel.Text ?? "" : "";

    /// <summary>Страницы документа.</summary>
    private readonly List<PageVm> _pages = [];

    /// <summary>Совпадения: номер страницы и номер слова на ней.</summary>
    private readonly List<(int Page, int Index)> _pageHits = [];

    /// <summary>Открытый документ для отрисовки страниц; null — не открылся или еще открывается.</summary>
    private DocPageSource? _source;

    /// <summary>
    /// Запрос, по которому посчитаны совпадения. TextChanged у Avalonia приходит ОТЛОЖЕННО — уже
    /// после того, как Show или SetQuery сами вписали запрос, — и погасить его флагом нельзя:
    /// обработчик сверяет текст с этим значением и свое же вписанное узнает. Раньше он запускал
    /// по нему второй пересчет, и тот через паузу отбрасывал человека, уже шагавшего по
    /// совпадениям, к первому.
    /// </summary>
    private string _query = "";

    /// <summary>Прокрутка ленты страниц — по ней совпадение подвозится к глазам.</summary>
    private ScrollViewer? _pagesScroll;

    /// <summary>
    /// Поколение обхода страниц, ОТДЕЛЬНОЕ от поколения документа: запрос в панели меняют, не
    /// меняя документа, и прежний обход продолжал бы писать в общий список совпадений рядом с
    /// новым. Счетчик от этого разъезжался, а стрелки шагали по дублям.
    /// </summary>
    private int _scanGen;

    /// <summary>
    /// Обход страниц по текущему запросу дошел до конца. До этого «нет» в счетчике было бы
    /// неправдой: совпадение может стоять на странице, до которой обход еще не добрался.
    /// </summary>
    private bool _pagesScanned;

    /// <summary>
    /// Ширина страницы в пикселях, под которую нарисованы картинки. Панель тянут за ручку, и
    /// страница, нарисованная под прежнюю ширину, растянулась бы и расплылась.
    /// </summary>
    private int _pageWidth;

    /// <summary>
    /// Страницы, чьи картинки сейчас в памяти, в порядке обращения. Страница A4 при ширине
    /// панели — это мегабайты, и держать их все у документа на три сотни страниц нельзя.
    /// </summary>
    private readonly List<int> _rendered = [];

    private const int RenderedCap = 10;

    private int _current = -1;

    /// <summary>Поколение открытия: ответ старого документа не должен перерисовать новый.</summary>
    private int _gen;

    /// <summary>
    /// Отмена фоновой работы по документу: открытия страниц, отрисовки и обхода совпадений.
    /// Открыли другой документ или закрыли панель — прежняя работа бросается на ближайшей
    /// проверке, а не доделывается впустую: раньше том на триста страниц открывался и обходился
    /// до конца, даже когда его уже никто не читал. Поколение при этом остается: то, что прервать
    /// нельзя (страницу PDFium рисует целиком), оно не пускает на экран.
    /// </summary>
    private CancellationTokenSource _docCts = new();

    /// <summary>
    /// Отмена отрисовки страниц под ТЕКУЩУЮ ширину, связанная с отменой документа. Ширина
    /// сменилась — картинки под прежнюю больше не нужны, в том числе те, что еще ждут очереди.
    /// </summary>
    private CancellationTokenSource _renderCts = new();

    /// <summary>
    /// Дебаунс правки строки поиска: обход страниц заново на каждое нажатие гонять незачем.
    /// </summary>
    private readonly DispatcherTimer _findDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>Дребезг смены ширины панели: страницы перерисовываются под новую ширину.</summary>
    private readonly DispatcherTimer _widthDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public DocReaderView()
    {
        InitializeComponent();

        PagesList.ItemTemplate = new FuncDataTemplate<PageVm>((_, _) => new PageItemView(this), supportsRecycling: false);
        PagesList.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is not { } sv) return;
            ScrollBarLink.SetSource(PagesBar, sv);
            _pagesScroll = sv;
        };

        // Сменилась ширина панели — страницы перерисовываются под новую. С дребезгом: ручку
        // тянут плавно, и рисовать страницу на каждый пиксель движения незачем.
        PagesList.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty || Document is null) return;
            _widthDebounce.Stop();
            _widthDebounce.Start();
        };
        _widthDebounce.Tick += (_, _) => { _widthDebounce.Stop(); ReflowPages(); };

        SetListPanelOpen(true);
        ListPanelBtn.Click += (_, _) =>
        {
            var open = ListPanelBtn.IsChecked == true;
            SetListPanelOpen(open);
            ListPanelToggled?.Invoke(this, open);
        };
        PrevBtn.Click  += (_, _) => Step(-1);
        NextBtn.Click  += (_, _) => Step(+1);

        FindBox.TextChanged += (_, _) =>
        {
            _findDebounce.Stop();
            if (Document is null) return;
            // Свое же вписанное (Show, SetQuery) второй раз не пересчитываем — см. _query.
            if ((FindBox.Text ?? "") != _query) _findDebounce.Start();
        };
        AddHandler(KeyDownEvent, OnReaderKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _findDebounce.Tick += (_, _) => { _findDebounce.Stop(); Rehighlight(); };
    }

    public void SetListPanelOpen(bool open)
    {
        ListPanelBtn.IsChecked = open;
        ListOpenIcon.IsVisible = !open;
        ListCloseIcon.IsVisible = open;
        ToolTip.SetTip(ListPanelBtn, open ? "Развернуть документ" : "Показать список документов");
    }

    // Клавиши панели: Ctrl+F (на macOS Cmd+F) — к строке поиска, F3 и Enter в строке поиска —
    // следующее совпадение, с Shift — предыдущее, Esc — закрыть панель.
    private void OnReaderKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var back    = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (command && e.Key == Key.F) FocusFind();
        else if (e.Key == Key.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
        else if (e.Key == Key.F3 || (e.Key == Key.Enter && FindBox.IsFocused)) FindNext(back);
        else return;
        e.Handled = true;
    }

    /// <summary>Поставить курсор в строку поиска по документу.</summary>
    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    /// <summary>Следующее совпадение (F3), с <paramref name="back"/> — предыдущее (Shift+F3).</summary>
    public void FindNext(bool back = false)
    {
        if (_findDebounce.IsEnabled) { _findDebounce.Stop(); Rehighlight(); }
        else Step(back ? -1 : 1);
    }

    // ── Открытие и закрытие ───────────────────────────────────────────────────

    /// <summary>
    /// Показать документ. <paramref name="query"/> — запрос поиска раздела: панель открывается
    /// сразу на первом совпадении, потому что человек нажал «Прочитать» именно ради него.
    /// </summary>
    public void Show(DocItem doc, string query)
    {
        Document = doc;
        TitleLabel.Text    = doc.Title;
        FindBox.Text       = query ?? "";
        _query             = FindBox.Text ?? "";
        _findDebounce.Stop();
        _widthDebounce.Stop();

        var token = Restart();
        Reset();
        ShowState("Открываю документ…");
        _ = OpenPagesAsync(doc, _gen, token);
    }

    /// <summary>
    /// Сменить запрос у уже открытого документа — раздел перечитали с другим словом. Документ
    /// заново не открывается: совпадения ищутся по уже открытым страницам.
    /// </summary>
    public void SetQuery(string query)
    {
        if (Document is null || FindBox.Text == query) return;

        FindBox.Text = query ?? "";
        _findDebounce.Stop();
        Rehighlight();
    }

    /// <summary>Закрыть панель и отпустить документ — картинки страниц в памяти не держим.</summary>
    public void Clear()
    {
        _findDebounce.Stop();
        _widthDebounce.Stop();
        Restart();
        Document = null;
        Reset();
        TitleLabel.Text    = "Документ";
        ShowState("");
    }

    /// <summary>
    /// Новый документ или пустая панель: новое поколение и новая отмена, прежняя работа
    /// бросается (см. <see cref="_docCts"/>).
    /// </summary>
    private CancellationToken Restart()
    {
        _gen++;
        _docCts.Cancel();
        _renderCts.Cancel();
        _renderCts.Dispose();
        _docCts.Dispose();
        _docCts    = new CancellationTokenSource();
        _renderCts = CancellationTokenSource.CreateLinkedTokenSource(_docCts.Token);
        return _docCts.Token;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void Reset()
    {
        foreach (var page in _pages) page.SetBitmap(null);
        _pages.Clear();
        _pageHits.Clear();
        _rendered.Clear();
        CutLabel.Text      = "";
        CutLabel.IsVisible = false;
        _current       = -1;
        _pageWidth     = 0;
        _pagesScanned  = false;

        // Документ PDFium отпускаем сразу: он держит открытый файл, а файл могут захотеть
        // переименовать или удалить прямо сейчас. Отпускаем в фоне: если страница в эту минуту
        // рисуется, закрытие ждет ее конца — PDFium страницу не прерывает, — и UI-потоку ждать
        // этого незачем, новый документ уже открывается.
        if (_source is { } closing) _ = Task.Run(closing.Dispose);
        _source = null;

        PagesList.ItemsSource = null;
        UpdateCounter();
    }

    // ── Страницы ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Надпись на месте страниц: «Открываю документ…», «не открылся», «файла нет». Пусто —
    /// страницы на экране.
    /// </summary>
    private void ShowState(string text)
    {
        var empty = text.Length > 0;
        StateLabel.Text      = text;
        StateLabel.IsVisible = empty;
        PagesList.IsVisible  = !empty;
        // Полоса прокрутки принадлежит списку и прячется вместе с ним; обратно ее вернет сама
        // связка, как только список встанет на место.
        PagesBar.IsVisible   = PagesBar.IsVisible && PagesList.IsVisible;
        UpdateCutNote();
    }

    /// <summary>
    /// Плашка внизу панели: что показано не все (<see cref="DocCutNote"/>) и что у скана без
    /// текстового слоя искать на листах нечего — иначе пустой счетчик «нет» выглядел бы
    /// поломкой поиска. Пока на месте страниц стоит надпись, строки нет.
    /// </summary>
    private void UpdateCutNote()
    {
        var notes = new List<string>();
        if (Document is { } doc && !StateLabel.IsVisible)
        {
            if (doc.TextState == DocTextState.NoText)
                notes.Add("Скан без текстового слоя — найденное на листах не подсвечивается.");
            if (_source is { } source && DocCutNote.ForPages(source.PageCount, source.TotalPages) is { Length: > 0 } cut)
                notes.Add(cut);
        }
        var note = string.Join(" ", notes);
        CutLabel.Text      = note;
        CutLabel.IsVisible = note.Length > 0;
    }

    /// <summary>
    /// Открыть документ: размеры всех листов сразу (по ним считается место каждой страницы),
    /// картинки — по мере прокрутки. Совпадения ищутся сразу после открытия.
    /// </summary>
    private async Task OpenPagesAsync(DocItem doc, int gen, CancellationToken token)
    {
        DocPageSource? source;
        try { source = DocTextExtractor.IsPdf(doc.FullPath)
                ? await Task.Run(() => DocPageSource.Open(doc.FullPath, token), token)
                : null; }
        catch (OperationCanceledException) { return; }

        if (gen != _gen || source is null)
        {
            source?.Dispose();
            if (gen == _gen)
                ShowState(File.Exists(doc.FullPath)
                    ? "Документ не открылся: файл занят, поврежден или защищен паролем. Сам файл " +
                      "открывается кнопкой «Открыть» на карточке."
                    : "Файла нет на диске — возможно, его переместили или удалили.");
            return;
        }

        _source = source;
        _pages.Clear();
        for (var i = 0; i < source.PageCount; i++) _pages.Add(new PageVm(i, source.Size(i)));

        PagesList.ItemsSource = _pages;
        ShowState("");
        ReflowPages();
        _ = ScanPagesAsync(gen);
    }

    /// <summary>
    /// Пересчитать ширину страницы под текущую ширину панели и перерисовать то, что видно.
    /// Картинки прежней ширины выбрасываются: растянутая страница расплывается, а
    /// сжатая — впустую держит память.
    /// </summary>
    private void ReflowPages()
    {
        if (_source is null || _pages.Count == 0) return;

        // Считаем в НАСТОЯЩИХ пикселях экрана, а их три множителя: раскладка, плотность
        // монитора и зум интерфейса. Зум — LayoutTransform поверх корня окна, поэтому Bounds
        // тут доzumные: при 150 % панель разложена в полтора раза уже, а показана в полтора
        // раза крупнее. Считай мы без него — страница рисовалась бы в полтора раза мельче
        // нужного и растягивалась при показе, то есть мутнела ровно там, где человек увеличил
        // интерфейс, чтобы РАЗГЛЯДЕТЬ.
        var width = (int)Math.Round(Math.Max(0, PagesList.Bounds.Width - 32) *
                                    (VisualRoot?.RenderScaling ?? 1) * Scaling.Current);
        if (width < 120) return;

        // Мелкое изменение ширины перерисовкой не отрабатываем: разница в пару процентов не
        // видна, а перерисовка сотни страниц заметна.
        if (_pageWidth > 0 && Math.Abs(width - _pageWidth) < _pageWidth * 0.04) return;

        _pageWidth = width;
        // Картинки под прежнюю ширину не нужны — и те, что еще только ждут очереди: их бросаем.
        _renderCts.Cancel();
        _renderCts.Dispose();
        _renderCts = CancellationTokenSource.CreateLinkedTokenSource(_docCts.Token);
        foreach (var page in _rendered.ToList()) _pages[page].SetBitmap(null);
        _rendered.Clear();
        foreach (var page in _pages) page.Refresh();
    }

    /// <summary>
    /// Попросить картинку страницы. Зовется самой страницей из отрисовки, когда та появляется
    /// на экране: рисовать весь документ вперед незачем, а у трехсот страниц и невозможно.
    ///
    /// Работа ОТКЛАДЫВАЕТСЯ в очередь, и это не украшение: зовут нас изнутри прохода отрисовки,
    /// а любое обращение к раскладке оттуда (а подготовка ширины ее трогает) роняет Avalonia
    /// с «Visual was invalidated during the render pass».
    /// </summary>
    private void RequestPage(PageVm page)
    {
        if (_source is null || page.Bitmap is not null || page.Loading) return;

        // Признак ставим сразу: отрисовка успеет позвать нас еще раз, пока задание ждет очереди.
        // Поколение берем СЕЙЧАС, а не когда задание дойдет: за это время могли открыть другой
        // документ, и страница прежнего нарисовалась бы из нового источника.
        page.Loading = true;
        var gen = _gen;
        Dispatcher.UIThread.Post(() => _ = RenderPageAsync(page, gen), DispatcherPriority.Background);
    }

    private async Task RenderPageAsync(PageVm page, int gen)
    {
        if (gen != _gen || _source is not { } source) { page.Loading = false; return; }
        if (_pageWidth <= 0) { ReflowPages(); if (_pageWidth <= 0) { page.Loading = false; return; } }

        var width = _pageWidth;
        var token = _renderCts.Token;
        var again = false;

        try
        {
            var png = await Task.Run(() => source.Render(page.Index, width, token), token);
            if (gen != _gen || png is null) return;

            // Пока страница рисовалась, панель сменила ширину: картинка под прежнюю растянулась
            // бы и расплылась, а места новой не уступила бы — страница, которая «уже рисуется»,
            // второй раз не просится. Просим ее заново, под новую ширину.
            if (width != _pageWidth) { again = true; return; }

            using var stream = new MemoryStream(png);
            page.SetBitmap(new Avalonia.Media.Imaging.Bitmap(stream));
            Remember(page.Index);
        }
        catch (OperationCanceledException)
        {
            // Отменили из-за смены ширины — страница нужна под новую; из-за смены документа — нет.
            again = gen == _gen;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Библиотека: страница {Page} не нарисовалась", page.Index + 1);
        }
        finally
        {
            page.Loading = false;
            if (again && gen == _gen) page.Refresh();
        }
    }

    /// <summary>Держим в памяти только последние страницы — остальные рисуются заново.</summary>
    private void Remember(int page)
    {
        _rendered.Remove(page);
        _rendered.Add(page);
        while (_rendered.Count > RenderedCap)
        {
            var oldest = _rendered[0];
            _rendered.RemoveAt(0);
            if (oldest >= 0 && oldest < _pages.Count) _pages[oldest].SetBitmap(null);
        }
    }

    /// <summary>
    /// Обойти страницы и найти на них слова запроса. Идет в фоне и страница за страницей:
    /// разбор трехсот страниц — это секунды, и держать ради них панель нельзя. Счетчик растет
    /// по ходу дела, поэтому видно, что работа идет.
    /// </summary>
    private async Task ScanPagesAsync(int gen)
    {
        if (_source is not { } source) return;

        // Обход начинается заново — прежний обязан замолчать на первой же своей проверке.
        var scan  = ++_scanGen;
        var token = _docCts.Token;

        var tokens = DocHighlight.Tokens(FindBox.Text);
        _pageHits.Clear();
        _pagesScanned = false;

        // Следы прежнего запроса снимаем со ВСЕХ страниц сразу, а не по мере обхода: иначе
        // очищенный запрос оставлял бы подсветку на уже показанных страницах (обход при пустом
        // запросе заканчивается прямо здесь), а при смене запроса страницы до места обхода
        // держали бы номера совпадений, которых больше нет.
        foreach (var stale in _pages)
            if (stale.Hits.Count > 0) { stale.Hits.Clear(); stale.Refresh(); }

        // Пустой запрос искать нечего — обход закончен. Иначе прежнее «3 из 17» уходит из
        // счетчика сразу, а не тогда, когда обход дойдет до первого совпадения.
        _pagesScanned = tokens.Count == 0;
        UpdateCounter();
        if (tokens.Count == 0) return;

        for (var i = 0; i < _pages.Count; i++)
        {
            if (gen != _gen || scan != _scanGen) return;

            var index = i;
            (IReadOnlyList<DocWord> Words, List<(int First, int Last)> Hits) found;
            try
            {
                found = await Task.Run(() =>
                {
                    // Слова страницы ищутся вместе, а не поодиночке: фраза запроса переходит через
                    // границу слов. Сумма «30 772,48» на странице — два слова, и запрос «30772»
                    // обязан подсветить оба, раз поиск нашел по нему документ.
                    var words = source.Words(index);
                    return (Words: words, Hits: DocHighlight.FindInWords([.. words.Select(w => w.Text)], tokens));
                }, token);
            }
            catch (OperationCanceledException) { return; }

            if (gen != _gen || scan != _scanGen) return;

            var page = _pages[index];
            page.SetWords(found.Words);
            page.Hits.Clear();
            foreach (var (first, last) in found.Hits)
            {
                // Совпадение из нескольких слов — одно: у всех его слов один сквозной номер, и
                // текущим оно становится целиком.
                for (var w = first; w <= last; w++) page.Hits.Add((w, _pageHits.Count));
                _pageHits.Add((index, first));
            }

            if (found.Hits.Count > 0)
            {
                page.Refresh();
                // Первое найденное совпадение сразу становится текущим: человек открыл документ
                // ради найденного слова.
                if (_current < 0) { _current = 0; ApplyCurrent(scroll: true); }
                else UpdateCounter();
            }
        }

        if (gen != _gen || scan != _scanGen) return;
        _pagesScanned = true;
        UpdateCounter();
    }

    // ── Подсветка и переходы ──────────────────────────────────────────────────

    /// <summary>
    /// Пересчитать совпадения по всему документу — запрос сменился. Правила ровно те же, по
    /// которым ищет раздел (<see cref="DocHighlight"/>), — иначе получилось бы «поиск нашел, а
    /// панель не подсветила».
    /// </summary>
    private void Rehighlight()
    {
        _query = FindBox.Text ?? "";

        // Текущее совпадение назначит обход: совпадения по новому запросу еще не сосчитаны.
        _current = -1;
        ApplyCurrent(scroll: false);
        if (_source is not null) _ = ScanPagesAsync(_gen);
    }

    /// <summary>Сколько совпадений — по ним ходят стрелки и счетчик.</summary>
    private int HitCount => _pageHits.Count;

    private void Step(int delta)
    {
        var count = HitCount;
        if (count == 0) return;

        var previous = _current;
        _current = ((_current + delta) % count + count) % count;
        ApplyCurrent(scroll: true, previous);
    }

    private void ApplyCurrent(bool scroll, int previous = -1)
    {
        UpdateCounter();

        // Перерисовываем ДВЕ страницы, а не весь список: у тома в сотни страниц перерисовка
        // всего по каждому шагу «дальше» была бы видна глазом.
        if (previous >= 0 && previous < _pageHits.Count) TouchPage(_pageHits[previous].Page);
        if (_current  >= 0 && _current  < _pageHits.Count) TouchPage(_pageHits[_current].Page);

        if (!scroll || _current < 0 || _current >= _pageHits.Count) return;
        ShowPageHit();
    }

    /// <summary>Отступ, на который найденное слово не доезжает до края окна.</summary>
    private const double HitEdge = 8;

    /// <summary>
    /// Подвезти текущее совпадение к глазам ВНУТРИ страницы. Довезти список до страницы мало:
    /// страница при ширине панели занимает несколько экранов, и слово из ее подвала оставалось
    /// за краем окна — человек видел пустой верх листа и счетчик «3 из 17». Два совпадения на
    /// одной странице при этом не двигали вид вовсе, и стрелка выглядела сломанной.
    ///
    /// Вбок не двигаем: страница вписана в ширину ленты, горизонтальной прокрутки у списка нет
    /// вовсе.
    /// </summary>
    private void ShowPageHit()
    {
        if (_current < 0 || _current >= _pageHits.Count) return;

        PagesList.ScrollIntoView(_pageHits[_current].Page);

        // Следом за раскладкой, а не прямо сейчас: контейнер страницы в эту минуту только что
        // подъехал, его размеров еще нет, и считать по ним было бы нечего.
        Dispatcher.UIThread.Post(ShowPageHitNow, DispatcherPriority.Background);
    }

    private void ShowPageHitNow()
    {
        if (_pagesScroll is not { } scroll) return;
        if (_current < 0 || _current >= _pageHits.Count) return;

        var (index, word) = _pageHits[_current];
        if (index < 0 || index >= _pages.Count) return;
        if (PagesList.ContainerFromIndex(index) is not Control container) return;
        if (container.Bounds.Width <= 0) return;

        var page = _pages[index];
        if (word < 0 || word >= page.Words.Count) return;

        var box = PageWordBox(page, container.Bounds.Width, word);
        if (container.TranslatePoint(new Point(0, box.Y), scroll) is not { } point) return;

        var window = scroll.Viewport.Height;
        if (window <= 0) return;
        if (point.Y >= 0 && point.Y + box.Height <= window) return;   // уже на виду

        // Двигаемся ровно настолько, чтобы слово влезло, и не больше: лишний сдвиг стоит
        // человеку места, на котором он стоял.
        var shift = box.Height > window || point.Y < 0
                  ? point.Y - HitEdge
                  : point.Y + box.Height - window + HitEdge;

        var span = Math.Max(0, scroll.Extent.Height - window);
        scroll.Offset = scroll.Offset.WithY(Math.Clamp(scroll.Offset.Y + shift, 0, span));
    }

    /// <summary>
    /// Место слова на показанной странице. Одна функция на отрисовку подсветки и на перевод
    /// слова к глазам: разойдись они — желтый прямоугольник стоял бы в одном месте, а
    /// прокрутка везла бы в другое, и понять такое человек может только как поломку панели.
    /// </summary>
    private static DocScreenBox PageWordBox(PageVm page, double areaWidth, int word) =>
        DocPageGeometry.ToScreen(page.Words[word].Box, page.Size.Height,
                                 DocPageGeometry.ScaleFor(areaWidth, page.Size.Width),
                                 padding: 0.5);

    private void TouchPage(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < _pages.Count) _pages[pageIndex].Refresh();
    }

    private void UpdateCounter()
    {
        var asked = DocHighlight.Tokens(FindBox.Text).Count > 0;
        var count = HitCount;
        // «Нет» — только когда искать больше негде: у страниц обход идет в фоне, и пока
        // он не кончился, совпадение может найтись дальше.
        var ready = _pagesScanned;

        CounterLabel.Text = count > 0
            ? $"{_current + 1} из {count}"
            : asked && ready ? "нет" : "";

        PrevBtn.IsEnabled = count > 0;
        NextBtn.IsEnabled = count > 0;
    }

    /// <summary>Сквозной номер текущего совпадения — страница спрашивает его при отрисовке.</summary>
    private int CurrentHit => _current;

    private IBrush Brush(string key, string fallback) =>
        this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallback));

    /// <summary>
    /// Страница документа: размер в пунктах, нарисованная картинка и совпадения на ней.
    /// Живет столько же, сколько открытый документ; картинку у нее отбирают, когда страница
    /// уезжает далеко от экрана.
    /// </summary>
    private sealed class PageVm(int index, DocPageSize size)
    {
        public int Index { get; } = index;
        public DocPageSize Size { get; } = size;

        public Avalonia.Media.Imaging.Bitmap? Bitmap { get; private set; }

        /// <summary>Страница уже рисуется — второй раз просить не надо.</summary>
        public bool Loading { get; set; }

        /// <summary>Слова страницы с их местами; пусто, пока страницу не разбирали.</summary>
        public IReadOnlyList<DocWord> Words { get; private set; } = [];

        /// <summary>Совпадения: номер слова на странице и сквозной номер по документу.</summary>
        public List<(int Word, int Id)> Hits { get; } = [];

        public event Action? Changed;

        public void SetBitmap(Avalonia.Media.Imaging.Bitmap? bitmap)
        {
            // Старую картинку отпускаем явно: это несколько мегабайт неуправляемой памяти, и
            // ждать сборщика ради них незачем.
            var old = Bitmap;
            Bitmap  = bitmap;
            if (!ReferenceEquals(old, bitmap)) old?.Dispose();
            Refresh();
        }

        public void SetWords(IReadOnlyList<DocWord> words) => Words = words;

        public void Refresh() => Changed?.Invoke();
    }

    /// <summary>
    /// Страница на экране. Рисует себя целиком сама — картинку и прямоугольники подсветки
    /// поверх нее.
    ///
    /// Своя отрисовка, а не Image с Canvas и фигурами: места слов приходят в пунктах страницы,
    /// и переводить их в координаты контрола все равно приходится вручную (ось Y у PDF идет
    /// снизу вверх). С готовыми фигурами к этому добавилась бы раскладка сотни прямоугольников
    /// на каждую перерисовку — а их надо пересчитывать при любой смене ширины панели.
    /// </summary>
    private sealed class PageItemView : Control
    {
        private readonly DocReaderView _owner;
        private PageVm? _page;

        public PageItemView(DocReaderView owner) => _owner = owner;

        /// <summary>
        /// Зазор ПОД листом. Листы, стоящие впритык, читались одной длинной лентой: границу
        /// между ними держала только серая линия в пиксель, и конец страницы был неотличим от
        /// разрыва колонки внутри нее. Зазор живет в высоте самого листа, а не в полях
        /// контейнера: высота строки списка обязана быть известна до отрисовки, иначе ползунок
        /// «дышит» на всю длину документа.
        /// </summary>
        private const double PageGap = 12;

        protected override Size MeasureOverride(Size availableSize)
        {
            // Высота известна ДО того, как страница нарисована: она считается по пропорции
            // страницы. Иначе список менял бы высоту под каждой подъехавшей картинкой, а
            // ползунок «дышал» бы на всю длину документа.
            var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
            var size  = _page?.Size ?? new DocPageSize(595, 842);
            var ratio = size.Width > 0 ? size.Height / size.Width : 1.414;
            return new Size(width, Math.Max(1, width * ratio) + PageGap);
        }

        public override void Render(DrawingContext context)
        {
            if (_page is not { } page) return;

            // Сам лист занимает высоту без зазора — он ниже, под листом, и остается фоном.
            var area = new Rect(0, 0, Bounds.Width, Math.Max(0, Bounds.Height - PageGap));

            // Лист всегда белый, даже пока картинки нет: серая дыра в ленте страниц читалась бы
            // как «тут ничего нет», а не «сейчас нарисуется».
            context.FillRectangle(Brushes.White, area);

            if (page.Bitmap is { } bitmap)
                context.DrawImage(bitmap, new Rect(bitmap.Size), area);
            else
                _owner.RequestPage(page);

            var border = _owner.Brush("BrushSeparator", "#D1D1D6");
            context.DrawRectangle(null, new Pen(border, 1), area);

            if (page.Hits.Count == 0 || page.Words.Count == 0) return;

            var plain   = _owner.Brush("BrushHighlight", "#FFF0B3");
            var current = _owner.Brush("BrushAccent",    "#007AFF");

            foreach (var (word, id) in page.Hits)
            {
                if (word < 0 || word >= page.Words.Count) continue;

                var box  = PageWordBox(page, area.Width, word);
                var rect = new Rect(box.X, box.Y, box.Width, box.Height);
                var now  = id == _owner.CurrentHit;

                // Подсветка ПРОЗРАЧНАЯ: под ней лежит сама страница, и закрасить слово наглухо
                // значило бы спрятать то, что человек искал.
                context.FillRectangle(new SolidColorBrush(((ISolidColorBrush)(now ? current : plain)).Color,
                                                          now ? 0.45 : 0.40), rect, 2);
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Unhook();
            _page = DataContext as PageVm;
            Hook();
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Hook();
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Unhook();
        }

        private void Hook()
        {
            if (_page is null) return;
            _page.Changed -= Repaint;
            _page.Changed += Repaint;
        }

        private void Unhook()
        {
            if (_page is not null) _page.Changed -= Repaint;
        }

        private void Repaint()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
}

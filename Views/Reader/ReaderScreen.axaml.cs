using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PdfLibrary.Views.Library;

namespace PdfLibrary.Views.Reader;

/// <summary>Экран панели чтения: узкая лента найденного и документ.</summary>
public partial class ReaderScreen : UserControl
{
    private ReaderViewModel? _reader;
    private LibraryViewModel? _library;
    private CardViewModel? _current;
    private bool _expanded;
    private bool _listHidden;
    private bool _fitPending;

    public ReaderScreen()
    {
        InitializeComponent();
        Pages.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && _fitPending) FitOnOpen();
        };
    }

    /// <summary>Закрыть панель чтения — вернуться к ленте.</summary>
    public event Action? CloseRequested;

    public ReaderViewModel? Reader => _reader;

    public void Attach(LibraryViewModel library, ReaderViewModel reader)
    {
        _library = library;
        _reader = reader;
        ListPanel.DataContext = library;
        ReaderPanel.DataContext = reader;
        Pages.Model = reader;
        reader.Opened += OnOpened;
        reader.HitFocused += ScrollToHit;
        reader.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReaderViewModel.Zoom)) KeepPlaceOnZoom();
            if (e.PropertyName == nameof(ReaderViewModel.CurrentPage)) ScrollRailTo(reader.CurrentPage);
            if (e.PropertyName is nameof(ReaderViewModel.IsOpen) or nameof(ReaderViewModel.Error))
                EmptyText.IsVisible = !reader.IsOpen && reader.Error.Length == 0;
        };
        library.FeedRebuilt += MarkCurrent;
    }

    /// <summary>Открыть документ карточки; null — первый в ленте.</summary>
    public void Open(CardViewModel? card, int page = 0)
    {
        if (_library is null || _reader is null) return;
        card ??= _library.Cards.FirstOrDefault();
        if (card is null) return;
        _current = card;
        MarkCurrent();
        _reader.Open(card.Doc, _library.SourceName(card.Doc.SourceId), _library.AppliedQuery, page);
    }

    /// <summary>Скрыть или показать узкую ленту (кнопка панели разделов).</summary>
    public void SetListVisible(bool visible)
    {
        _listHidden = !visible;
        ApplyLayout();
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    /// <summary>
    /// F3 и Shift+F3 из любого места панели: к следующему или предыдущему совпадению. Поиск по
    /// новой строке еще не запускали — запускается, как по Enter в строке поиска.
    /// </summary>
    public void FindNext(bool back)
    {
        if (_reader is null) return;
        if (_reader.Hits.Count > 0 && _reader.SearchedQuery == _reader.FindQuery)
        {
            if (back) _reader.PrevHit();
            else _reader.NextHit();
        }
        else _reader.Find();
    }

    private void ApplyLayout()
    {
        var show = !_expanded && !_listHidden;
        ListPanel.IsVisible = show;
        Layout.ColumnDefinitions[0].Width = show ? new GridLength(340) : new GridLength(0);
        ExpandButton.Classes.Set("on", _expanded);
        ToolTip.SetTip(ExpandButton, _expanded ? "Показать ленту" : "Развернуть документ");
    }

    private void MarkCurrent()
    {
        if (_library is null) return;
        foreach (var card in _library.Cards) card.IsCurrent = ReferenceEquals(card, _current);
    }

    private void OnOpened()
    {
        Pages.Relayout();
        _fitPending = true;
        Dispatcher.UIThread.Post(FitOnOpen, DispatcherPriority.Loaded);
    }

    // Масштаб при открытии — по ширине области листов, затем переход на страницу открытия.
    private void FitOnOpen()
    {
        if (_reader is null || PageScroll.Bounds.Width <= 0) return;
        _fitPending = false;
        _reader.FitWidth(PageScroll.Bounds.Width - 2 * PageCanvas.Pad - 16);
        Pages.Relayout();
        Dispatcher.UIThread.Post(() =>
        {
            PageScroll.Offset = new Vector(PageScroll.Offset.X, Pages.TopOf(_reader.CurrentPage) - PageCanvas.Pad);
            Pages.InvalidateVisual();
        }, DispatcherPriority.Loaded);
    }

    private void KeepPlaceOnZoom()
    {
        if (_reader is null) return;
        var page = _reader.CurrentPage;
        Pages.Relayout();
        Dispatcher.UIThread.Post(() =>
            PageScroll.Offset = new Vector(PageScroll.Offset.X, Pages.TopOf(page) - PageCanvas.Pad),
            DispatcherPriority.Loaded);
    }

    private void ScrollToHit(PageHit hit)
    {
        if (_reader is null || hit.Boxes.Count == 0) return;
        var size = _reader.Sizes[hit.Page];
        var box  = Core.Documents.DocPageGeometry.ToScreen(hit.Boxes[0], size.Height, Pages.Scale);
        var y    = Pages.TopOf(hit.Page) + box.Y;
        var view = PageScroll.Viewport.Height;
        // Совпадение уже на экране — прокрутка не нужна: глаз не должен терять место.
        if (y >= PageScroll.Offset.Y + 24 && y + box.Height <= PageScroll.Offset.Y + view - 24)
        {
            Pages.InvalidateVisual();
            return;
        }
        PageScroll.Offset = new Vector(PageScroll.Offset.X, Math.Max(0, y - view / 3));
    }

    private void OnPagesScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_reader is null || !_reader.IsOpen) return;
        // Текущая страница — та, что пересекает верхнюю треть окна.
        var page = Pages.PageAt(PageScroll.Offset.Y + PageScroll.Viewport.Height / 3);
        if (page != _reader.CurrentPage) _reader.CurrentPage = page;
    }

    private void ScrollRailTo(int page)
    {
        if (_reader is null || page < 0 || page >= _reader.Thumbs.Count) return;
        Rail.ScrollIntoView(page);
    }

    // ── Лента слева ───────────────────────────────────────────────────────────

    private void OnListPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is CardViewModel card) card.EnsureThumbnail();
    }

    private void OnListPick(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CardViewModel card) Open(card);
    }

    private void OnListQueryKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _library?.ApplySearch(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _library?.ClearSearch(); e.Handled = true; }
    }

    private void OnListClear(object? sender, RoutedEventArgs e) => _library?.ClearSearch();

    private void OnCloseReader(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnExpand(object? sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        ApplyLayout();
    }

    // ── Документ ──────────────────────────────────────────────────────────────

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (_reader is null) return;
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _reader.PrevHit();
            else if (_reader.Hits.Count > 0 && _reader.SearchedQuery == _reader.FindQuery) _reader.NextHit();
            else _reader.Find();
            e.Handled = true;
        }
    }

    private void OnPrevHit(object? sender, RoutedEventArgs e) => _reader?.PrevHit();
    private void OnNextHit(object? sender, RoutedEventArgs e) => _reader?.NextHit();
    private void OnZoomIn(object? sender, RoutedEventArgs e) => _reader?.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => _reader?.ZoomOut();

    private void OnRailPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is PageThumb thumb && _reader is not null)
        {
            var dpi = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            _reader.RequestThumb(thumb, (int)Math.Ceiling(thumb.Width * dpi));
        }
    }

    private void OnThumbClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PageThumb thumb || _reader is null) return;
        _reader.CurrentPage = thumb.Index;
        PageScroll.Offset = new Vector(PageScroll.Offset.X, Pages.TopOf(thumb.Index) - PageCanvas.Pad);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PdfLibrary.Logging;
using PdfLibrary.Services;
using PdfLibrary.Utils;
using PdfLibrary.Views.Dialogs;
using PdfLibrary.Views.Library;
using PdfLibrary.Views.Reader;

namespace PdfLibrary.Views;

/// <summary>
/// Главное окно: панель разделов и экраны — лента, панель чтения, настройки, первый запуск.
/// Экран выбирается здесь; сами экраны о соседях не знают и зовут окно событиями.
/// </summary>
public partial class MainWindow : Window
{
    private enum Screen { Feed, Reader, Settings }

    private readonly LibraryService? _library;
    private readonly LibraryViewModel? _vm;
    private readonly ReaderViewModel _reader = new();
    private Screen _screen = Screen.Feed;
    private bool _welcome;
    private bool _readerListHidden;
    private bool _settingsNavHidden;
    private IDisposable? _noticeTimer;
    private UiWatchdog? _watchdog;

    // Для дизайнера разметки: окно без библиотеки.
    public MainWindow() => InitializeComponent();

    public MainWindow(LibraryService library)
    {
        InitializeComponent();
        _library = library;
        _vm = new LibraryViewModel(library);

        Feed.DataContext = _vm;
        Feed.ReadRequested += OpenReader;
        Reader.Attach(_vm, _reader);
        Reader.CloseRequested += () => Show(Screen.Feed);
        Settings.Attach(library);
        Welcome.Attach(library);
        Welcome.OpenLibraryRequested += () =>
        {
            _welcome = false;
            Show(Screen.Feed);
        };

        _welcome = library.Sources.Count == 0;
        library.Changed += change =>
        {
            // Убрали последний источник — снова экран первого запуска.
            if (library.Sources.Count == 0 && !_welcome)
            {
                _welcome = true;
                Show(_screen);
            }
        };
        library.ReadingChanged += UpdateReading;

        Feed.SetSidebarVisible(!AppSettings.SidebarHidden);
        ApplyScale(Scaling.Current);
        Scaling.ScaleChanged += ApplyScale;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnWindowKey;
        Closing += OnClosing;
        Opened += (_, _) =>
        {
            _watchdog = new UiWatchdog();
            ReopenLast();
        };

        Show(Screen.Feed);
    }

    // ── Помощники для экранов ─────────────────────────────────────────────────

    /// <summary>Слой диалогов окна, в котором живет контрол.</summary>
    public static DialogHost? DialogsOf(Control control) =>
        (TopLevel.GetTopLevel(control) as MainWindow)?.DialogLayer;

    /// <summary>Уведомление внизу окна, в котором живет контрол.</summary>
    public static void NoticeOf(Control control, string text, bool isError = false) =>
        (TopLevel.GetTopLevel(control) as MainWindow)?.ShowNotice(text, isError);

    /// <summary>Показать уведомление на несколько секунд.</summary>
    public void ShowNotice(string text, bool isError = false)
    {
        NoticeText.Text = text;
        NoticeText.Foreground = isError ? Res.Brush("BrushRed")
                                        : Res.Brush("BrushPrimaryText");
        NoticeBar.IsVisible = true;
        _noticeTimer?.Dispose();
        _noticeTimer = DispatcherTimer.RunOnce(() => NoticeBar.IsVisible = false,
                                               TimeSpan.FromSeconds(isError ? 10 : 5));
    }

    private void OnCloseNotice(object? sender, RoutedEventArgs e) => NoticeBar.IsVisible = false;

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ── Экраны ────────────────────────────────────────────────────────────────

    private void Show(Screen screen)
    {
        _screen = screen;
        var welcome = _welcome && screen != Screen.Settings;
        Welcome.IsVisible = welcome;
        Feed.IsVisible = !welcome && screen == Screen.Feed;
        Reader.IsVisible = !welcome && screen == Screen.Reader;
        Settings.IsVisible = screen == Screen.Settings;

        LibraryButton.Classes.Set("on", screen != Screen.Settings);
        SettingsButton.Classes.Set("on", screen == Screen.Settings);
        SidebarButton.IsEnabled = !welcome;
        UpdateSidebarButton();

        if (screen == Screen.Settings) Settings.Refresh();
        if (screen != Screen.Reader) AppSettings.LastDocument = ("", 0);
    }

    private void OpenReader(CardViewModel? card)
    {
        if (_vm is null) return;
        if (card is null && _reader.Doc is { } open && _vm.Cards.FirstOrDefault(c => ReferenceEquals(c.Doc, open)) is { } same)
            card = same;
        Show(Screen.Reader);
        Reader.Open(card);
    }

    // Последний документ при запуске — по настройке «Открывать при запуске последний документ».
    private void ReopenLast()
    {
        if (!AppSettings.ReopenLast || _library is null || _vm is null || _welcome) return;
        var (path, page) = AppSettings.LastDocument;
        if (path.Length == 0 || _library.ByPath(path) is not { Missing: false } doc) return;
        var card = _vm.Cards.FirstOrDefault(c => ReferenceEquals(c.Doc, doc));
        if (card is null) return;
        Show(Screen.Reader);
        Reader.Open(card, page);
    }

    private void OnLibrary(object? sender, RoutedEventArgs e) =>
        Show(_screen == Screen.Settings ? (_reader.IsOpen ? Screen.Reader : Screen.Feed) : Screen.Feed);

    private void OnSettings(object? sender, RoutedEventArgs e) => Show(Screen.Settings);

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        switch (_screen)
        {
            case Screen.Feed:
                AppSettings.SidebarHidden = !AppSettings.SidebarHidden;
                Feed.SetSidebarVisible(!AppSettings.SidebarHidden);
                break;
            case Screen.Reader:
                _readerListHidden = !_readerListHidden;
                Reader.SetListVisible(!_readerListHidden);
                break;
            case Screen.Settings:
                _settingsNavHidden = !_settingsNavHidden;
                Settings.SetNavVisible(!_settingsNavHidden);
                break;
        }
        UpdateSidebarButton();
    }

    private void UpdateSidebarButton()
    {
        var hidden = _screen switch
        {
            Screen.Feed     => AppSettings.SidebarHidden,
            Screen.Reader   => _readerListHidden,
            _               => _settingsNavHidden,
        };
        SidebarIcon.Data = Res.Icon(hidden ? "IconSidebarOpen" : "IconSidebarClose");
        ToolTip.SetTip(SidebarButton, hidden ? "Показать боковую панель" : "Скрыть боковую панель");
    }

    // ── Фоновое чтение ────────────────────────────────────────────────────────

    private void UpdateReading(ReadState state)
    {
        ReadingButton.IsVisible = state.Running;
        ReadingRing.Value = state.Fraction;
        ToolTip.SetTip(ReadingButton, $"Читаю документы: {state.Done:N0} из {state.Total:N0}");
    }

    private void OnReadingClick(object? sender, RoutedEventArgs e)
    {
        _welcome = _welcome && _library?.Sources.Count == 0;
        Show(Screen.Feed);
    }

    // ── Масштаб, клавиши, перетаскивание ──────────────────────────────────────

    private void ApplyScale(double scale) =>
        ZoomRoot.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);

    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.F)
        {
            if (_screen == Screen.Reader) Reader.FocusFind();
            else if (_screen == Screen.Feed && !_welcome) Feed.FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && _screen == Screen.Reader)
        {
            Reader.FindNext(back: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null && Directory.Exists(p));

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DroppedFolder(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DroppedFolder(e) is not { } folder) return;
        e.Handled = true;
        if (_welcome) Welcome.AddFolder(folder);
        else await Feed.AddSourceAsync(folder);
    }

    // ── Закрытие ──────────────────────────────────────────────────────────────

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Фоновый режим: крестик прячет окно в значок у часов, наблюдение и чтение продолжаются.
        // Выход — меню значка или завершение работы системы.
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic &&
            BackgroundBehavior.ShouldHideOnClose(AppSettings.Background, explicitQuit: false, canReturn: App.TrayShown))
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _watchdog?.Dispose();
        _reader.Dispose();
    }
}

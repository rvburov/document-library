using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PdfLibrary.Controls.Behaviors;
using PdfLibrary.Controls.Components;
using PdfLibrary.Controls.Widgets;
using PdfLibrary.Logging;
using PdfLibrary.Services;
using PdfLibrary.Utils;
using PdfLibrary.Views.Dialogs;
using PdfLibrary.Views.Library;
using PdfLibrary.Views.Settings;

namespace PdfLibrary.Views;

/// <summary>
/// Главное окно: панель разделов, боковая панель, раздел и
/// правая панель чтения. Раскладку колонок и обе ручки ширины держит <see cref="ApplyColumns"/>;
/// разделы о соседях не знают и зовут окно событиями.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LibraryService? _library;
    private readonly LibraryViewModel? _vm;

    /// <summary>Открытый раздел: "library" или "settings".</summary>
    private string _activeModule = "library";

    /// <summary>Источников нет — вместо ленты экран первого запуска.</summary>
    private bool _welcome;

    private UiWatchdog? _watchdog;

    // Для дизайнера разметки: окно без библиотеки.
    public MainWindow() => InitializeComponent();

    public MainWindow(LibraryService library)
    {
        InitializeComponent();
        _library = library;
        _vm = new LibraryViewModel(library);

        AppNotifications.Install(this);

        // ── Панель разделов ──
        ActivityBarControl.SidebarToggled += (_, isOpen) => OnSidebarToggled(isOpen);
        ActivityBarControl.ModuleChanged  += (_, module) => ShowModule(module);
        ActivityBarControl.WorkClicked    += (_, _) =>
        {
            ActivityBarControl.SetModuleActive("library");
            ShowModule("library");
        };

        // ── Лента ──
        LibraryModule.Attach(_vm);
        LibrarySidebar.Attach(_vm);
        LibrarySidebar.CreateRequested += kind => _ = LibraryModule.CreateAsync(kind);
        LibrarySidebar.SourceMenuRequested += (id, anchor) => SourceMenu.Build(this, library, id).Open(anchor);
        LibraryModule.ReadRequested += OpenLibraryReader;
        LibraryModule.ReaderToggled += OnReaderToggled;

        // ── Панель чтения ──
        LibraryReader.CloseRequested   += (_, _) => CloseLibraryReader();
        LibraryReader.ListPanelToggled += (_, open) =>
        {
            _libraryContentOpen = open;
            ApplyColumns();
        };
        _readerWidth = AppSettings.ReaderWidth;
        // Сменился запрос поиска ленты — у открытого документа подсвечивается новое слово.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.AppliedQuery) && _readerOpen)
                LibraryReader.SetQuery(_vm.AppliedQuery);
        };

        // ── Настройки ──
        SettingsSidebar.Title = "Настройки";
        SettingsSidebar.SetItems(SettingsView.Categories.Select(c => new SidebarListItem(c.Key, c.Title, c.Icon)));
        SettingsSidebar.CategorySelected += (_, key) => SettingsModule.ShowCategory(key);
        SettingsModule.Attach(library);

        // ── Первый запуск ──
        WelcomeModule.Attach(library);
        WelcomeModule.OpenLibraryRequested += () =>
        {
            _welcome = false;
            ShowModule("library");
        };
        _welcome = library.Sources.Count == 0;
        library.Changed += _ =>
        {
            // Убрали последний источник — снова экран первого запуска.
            if (library.Sources.Count == 0 && !_welcome)
            {
                _welcome = true;
                if (_readerOpen) CloseLibraryReader();
                ShowModule(_activeModule);
            }
        };
        library.ReadingChanged += UpdateReading;

        // Сменилась тема — карточки ленты и строки боковой панели перекрашиваются: кисти у них
        // взяты кодом при постройке, а не ресурсами разметки.
        ActualThemeVariantChanged += (_, _) =>
        {
            LibraryModule.RebuildCards();
            LibrarySidebar.Refresh();
        };

        _sidebarOpen = !AppSettings.SidebarHidden;
        ActivityBarControl.SetSidebarOpen(_sidebarOpen);
        ActivityBarControl.SetModuleActive("library");
        ShowModule("library");

        // Уровень масштабирования: применяем сохраненный и реагируем на изменения из настроек.
        ApplyScale(Scaling.Current);
        Scaling.ScaleChanged += ApplyScale;
        Closed += (_, _) => Scaling.ScaleChanged -= ApplyScale;

        // При потере фокуса окном закрываем открытые выпадашки — иначе на macOS они «зависают»
        // поверх других программ.
        Deactivated += (_, _) => TransientPopups.CloseAll();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnWindowKey;
        Closing += OnClosing;
        Opened += (_, _) =>
        {
            _watchdog = new UiWatchdog();
            ReopenLast();
        };
    }

    // ── Помощники для разделов ────────────────────────────────────────────────

    /// <summary>Слой диалогов окна, в котором живет контрол.</summary>
    public static DialogHost? DialogsOf(Control control) =>
        (TopLevel.GetTopLevel(control) as MainWindow)?.DialogLayer;

    /// <summary>Уведомление в правом верхнем углу окна, в котором живет контрол.</summary>
    public static void NoticeOf(Control control, string text, bool isError = false) =>
        (TopLevel.GetTopLevel(control) as MainWindow)?.ShowNotice(text, isError);

    /// <summary>Показать уведомление — плашкой в правом верхнем углу.</summary>
    public void ShowNotice(string text, bool isError = false)
    {
        if (isError) AppNotifications.Error(text);
        else AppNotifications.Info(text);
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ── Разделы ───────────────────────────────────────────────────────────────

    private void ShowModule(string module)
    {
        _activeModule = module;
        var settings = module == "settings";
        var welcome  = _welcome && !settings;

        LibraryModule.IsVisible  = !settings && !welcome;
        WelcomeModule.IsVisible  = welcome;
        SettingsModule.IsVisible = settings;

        LibrarySidebar.IsVisible  = _sidebarOpen && !settings;
        SettingsSidebar.IsVisible = _sidebarOpen && settings;

        // У первого запуска сайдбара нет: источников, которые он показывает, еще нет.
        ActivityBarControl.SetSidebarEnabled(!welcome);
        if (settings) SettingsModule.Refresh();

        ApplyColumns();
    }

    // ── Колонки и ручки ───────────────────────────────────────────────────────

    private bool _sidebarOpen = true;

    // Запоминаем последнюю ширину сайдбара, чтобы вернуть ее при повторном открытии.
    private double _lastSidebarWidth = 220;

    private const double SidebarMin = 180;
    private const double SidebarMax = 500;

    private void OnSidebarToggled(bool isOpen)
    {
        _sidebarOpen = isOpen;
        AppSettings.SidebarHidden = !isOpen;
        ShowModule(_activeModule);
    }

    // Единая точка раскладки колонок (сайдбар | контент | панель чтения) и обеих ручек ширины.
    // Вызывается при любом изменении состояния панелей.
    //
    //   сайдбар (col2)   — пиксельная ширина при открытии, 0 при закрытии
    //   контент  (col3)  — Star (растягивается), либо 0 когда лента свернута
    //   разделитель(col4)— виден только когда лента и панель чтения открыты одновременно
    //   панель   (col5)  — пиксельная при открытой ленте; Star когда лента свернута; 0 когда закрыта
    private void ApplyColumns()
    {
        var cols      = RootGrid.ColumnDefinitions;
        var inLibrary = _activeModule == "library" && !_welcome;
        var sidebar   = _sidebarOpen && !(_welcome && _activeModule == "library");

        var reader     = inLibrary && _readerOpen;
        // Ленту можно свернуть стрелкой в шапке документа — документ во всю ширину.
        var collection = !reader || _libraryContentOpen;

        // ── Сайдбар (col 2) ──
        if (sidebar)
        {
            cols[2].MinWidth = SidebarMin;
            cols[2].MaxWidth = SidebarMax;
            cols[2].Width    = new GridLength(_lastSidebarWidth, GridUnitType.Pixel);
        }
        else
        {
            cols[2].MinWidth = 0;
            cols[2].MaxWidth = 0;
            cols[2].Width    = new GridLength(0);
        }
        SidebarSeparator.IsVisible = sidebar;

        // ── Контент (col 3) | разделитель (col 4) | панель чтения (col 5) ──
        if (collection && reader)            // обе панели открыты
        {
            cols[3].Width    = new GridLength(1, GridUnitType.Star);
            cols[4].Width    = GridLength.Auto;
            cols[5].MinWidth = AppSettings.ReaderWidthMin;
            cols[5].MaxWidth = AppSettings.ReaderWidthMax;
            cols[5].Width    = new GridLength(_readerWidth, GridUnitType.Pixel);
        }
        else if (collection)                 // только лента → во всю ширину
        {
            cols[3].Width    = new GridLength(1, GridUnitType.Star);
            cols[4].Width    = new GridLength(0);
            cols[5].MinWidth = 0;
            cols[5].MaxWidth = double.PositiveInfinity;
            cols[5].Width    = new GridLength(0);
        }
        else                                 // только документ (лента свернута) → во всю ширину
        {
            cols[3].Width    = new GridLength(0);
            cols[4].Width    = new GridLength(0);
            cols[5].MinWidth = 0;
            cols[5].MaxWidth = double.PositiveInfinity;
            cols[5].Width    = new GridLength(1, GridUnitType.Star);
        }

        // ── Видимость блоков ──
        // Лента сохраняет всю ширину рабочей области: панель чтения закрывает часть списка, а не
        // сжимает карточки и поиск.
        Grid.SetColumnSpan(ModulesHost, inLibrary && collection && reader ? 3 : 1);
        LibraryModule.IsVisible  = inLibrary && collection;
        LibraryReader.SetListPanelOpen(_libraryContentOpen);
        LibraryReader.IsVisible  = reader;
        ReaderSeparator.IsVisible = collection && reader;

        // ── Ручки ширины ──
        ResizeGrip.IsVisible       = sidebar;
        ReaderResizeGrip.IsVisible = collection && reader;
    }

    // ── Перетаскивание ручки ширины сайдбара ─────────────────────────────────

    private bool   _gripDragging;
    private double _gripStartX;
    private double _gripStartWidth;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        var col = RootGrid.ColumnDefinitions[2];
        _gripDragging   = true;
        _gripStartX     = e.GetPosition(RootGrid).X;
        _gripStartWidth = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_gripDragging) return;

        var dx       = e.GetPosition(RootGrid).X - _gripStartX;
        var newWidth = Math.Clamp(_gripStartWidth + dx, SidebarMin, SidebarMax);

        RootGrid.ColumnDefinitions[2].Width = new GridLength(newWidth, GridUnitType.Pixel);
        _lastSidebarWidth = newWidth;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _gripDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ── Перетаскивание ручки панели чтения ───────────────────────────────────

    private bool   _readerGripDragging;
    private double _readerGripStartX;
    private double _readerGripStartWidth;

    private void OnReaderGripPressed(object? sender, PointerPressedEventArgs e)
    {
        var col = RootGrid.ColumnDefinitions[5];
        _readerGripDragging   = true;
        _readerGripStartX     = e.GetPosition(RootGrid).X;
        _readerGripStartWidth = col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;
        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnReaderGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_readerGripDragging) return;

        // Тянем за левый край — движение мыши вправо уменьшает ширину панели.
        var dx       = e.GetPosition(RootGrid).X - _readerGripStartX;
        var newWidth = Math.Clamp(_readerGripStartWidth - dx, AppSettings.ReaderWidthMin, AppSettings.ReaderWidthMax);

        RootGrid.ColumnDefinitions[5].Width = new GridLength(newWidth, GridUnitType.Pixel);
        _readerWidth = (int)newWidth;
    }

    private void OnReaderGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_readerGripDragging) AppSettings.ReaderWidth = _readerWidth;
        _readerGripDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ── Панель чтения ─────────────────────────────────────────────────────────

    private int  _readerWidth = AppSettings.ReaderWidthDefault;
    private bool _libraryContentOpen = true;
    private bool _readerOpen;

    /// <summary>Последний читанный документ — его возвращает кнопка навбара.</summary>
    private CardViewModel? _lastRead;

    private void OpenLibraryReader(CardViewModel card)
    {
        if (_vm is null || card.Doc.Missing) return;

        _lastRead   = card;
        _readerOpen = true;
        LibraryReader.Show(card.Doc, _vm.AppliedQuery, _vm.SourceName(card.Doc.SourceId));
        LibraryModule.SetReaderOpen(true);
        AppSettings.LastDocument = (card.Doc.FullPath, 0);
        ApplyColumns();
    }

    private void CloseLibraryReader()
    {
        _readerOpen = false;
        _libraryContentOpen = true;
        LibraryReader.Clear();
        LibraryModule.SetReaderOpen(false);
        AppSettings.LastDocument = ("", 0);
        ApplyColumns();
    }

    // Кнопка навбара. Присваивание IsChecked из SetReaderOpen поднимает это же событие — эхо
    // гасится сверкой с состоянием самой панели.
    private void OnReaderToggled(bool wantOpen)
    {
        if (wantOpen == _readerOpen) return;
        if (!wantOpen) { CloseLibraryReader(); return; }

        // Открывать пустую панель нечем: кнопка возвращает последний читанный документ, а если
        // его нет — первый в ленте; пустая лента — кнопка откатывается обратно.
        var card = _lastRead is { Doc.Missing: false } last && _vm?.Cards.Contains(last) == true
            ? last
            : _vm?.Cards.FirstOrDefault(c => !c.Doc.Missing);
        if (card is null) { Dispatcher.UIThread.Post(() => LibraryModule.SetReaderOpen(false)); return; }
        OpenLibraryReader(card);
    }

    // Последний документ при запуске — по настройке «Открывать при запуске последний документ».
    private void ReopenLast()
    {
        if (!AppSettings.ReopenLast || _library is null || _vm is null || _welcome) return;
        var (path, _) = AppSettings.LastDocument;
        if (path.Length == 0 || _library.ByPath(path) is not { Missing: false } doc) return;
        if (_vm.Cards.FirstOrDefault(c => ReferenceEquals(c.Doc, doc)) is { } card) OpenLibraryReader(card);
    }

    // ── Фоновое чтение ────────────────────────────────────────────────────────

    private void UpdateReading(ReadState state)
    {
        var tip = state.Running ? $"Читаю документы: {state.Done:N0} из {state.Total:N0}" : "";
        ActivityBarControl.UpdateWork(state.Running ? state.Done : 0, state.Running ? state.Total : 0, false, tip);
    }

    // ── Масштаб, клавиши, перетаскивание ──────────────────────────────────────

    // Масштаб всего интерфейса: LayoutTransform на корне — содержимое перекомпоновывается в
    // логических пикселях и заполняет окно. 100 % → без трансформации (быстрый общий путь).
    private void ApplyScale(double scale) =>
        ScaleHost.LayoutTransform = Math.Abs(scale - 1.0) < 0.001 ? null : new ScaleTransform(scale, scale);

    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.F && _activeModule == "library" && !_welcome)
        {
            // Ctrl+F ищет там, где человек работает: в документе, если фокус в панели чтения.
            if (_readerOpen && LibraryReader.IsKeyboardFocusWithin) LibraryReader.FocusFind();
            else LibraryModule.FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && _readerOpen)
        {
            LibraryReader.FindNext(back: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
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
        if (_welcome) WelcomeModule.AddFolder(folder);
        else await LibraryModule.AddSourceAsync(folder);
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
    }
}

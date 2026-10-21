using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Logging;
using PdfLibrary.Services;
using PdfLibrary.Setup;
using PdfLibrary.Utils;
using PdfLibrary.Views.Dialogs;
using PdfLibrary.Views.Library;

namespace PdfLibrary.Views.Settings;

/// <summary>Строка списка папок «Источники и чтение».</summary>
public sealed record FolderRow(int Id, string Name, string Path, string CountText, string Status,
                               IBrush? StatusBrush, IBrush? Dot, int Rank);

/// <summary>Строка журнала на экране «Диагностика».</summary>
public sealed partial class LogRowView : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public LogReader.LogRow Row { get; init; } = null!;
    public string Time => LogLabels.FormatTs(Row.Timestamp);
    public string Level => LogLabels.LevelLabel(Row.Level);
    public string Message => Row.Message;
    public string Detail => Row.Exception ?? "";
    public bool HasDetail => Detail.Length > 0;
    public bool IsError => Row.Level is "Error" or "Fatal";
    public bool IsWarning => Row.Level == "Warning";
    public bool IsInfo => !IsError && !IsWarning;
    /// <summary>Цвет уровня: ошибка красная, предупреждение оранжевое, прочее серое.</summary>
    public IBrush LevelBrush => Res.Brush(IsError ? "BrushRed" : IsWarning ? "BrushOrange" : "BrushSecondaryText")
                                ?? Brushes.Gray;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isOpen;
}

/// <summary>Экран «Настройки»: разметка в SettingsView.axaml, здесь — разделы и их действия.</summary>
public partial class SettingsView : UserControl
{
    private enum Section { General, Sources, Cards, Look, Diagnostics, About }

    private static readonly (Section Id, string Key, string Title, string Icon)[] Sections =
    [
        (Section.General,     "general",     "Общее",               "IconSliders"),
        (Section.Sources,     "sources",     "Источники и чтение",  "IconFolderTree"),
        (Section.Cards,       "cards",       "Лента и карточки",    "IconBook"),
        (Section.Look,        "look",        "Оформление",          "IconSun"),
        (Section.Diagnostics, "diagnostics", "Диагностика",         "IconCode"),
        (Section.About,       "about",       "О программе",         "IconInfo"),
    ];

    /// <summary>Разделы настроек для списка в боковой панели окна: ключ, подпись, иконка.</summary>
    public static IEnumerable<(string Key, string Title, string Icon)> Categories =>
        Sections.Select(c => (c.Key, c.Title, c.Icon));

    private readonly SettingsViewModel _vm = new();
    private LibraryService? _library;
    private Section _section = Section.General;
    private string _folderFilter = "all";
    private string _levelFilter = "all";
    private LogReader.LogQuery _logQuery = new();
    private readonly List<LogRowView> _logRows = [];
    private UpdateInfo? _update;
    private bool _syncing;

    private const int LogPage = 200;

    public SettingsView()
    {
        InitializeComponent();
        DataContext = _vm;
        // Живой пример карточки следует за переключателями «Лента и карточки».
        AppSettings.Changed += key =>
        {
            if (_section == Section.Cards && key.StartsWith("card.", StringComparison.Ordinal))
                Avalonia.Threading.Dispatcher.UIThread.Post(BuildPreview);
        };
    }

    public void Attach(LibraryService library)
    {
        _library = library;
        library.Changed += _ => { if (IsVisible && _section == Section.Sources) RefreshFolders(); };
        library.ReadingChanged += _ => { if (IsVisible && _section == Section.Sources) RefreshFolders(); };
    }

    /// <summary>Экран открыли — перечитать значения раздела.</summary>
    public void Refresh()
    {
        _vm.Load();
        ShowSection(_section);
    }

    /// <summary>Пункт списка разделов в боковой панели окна.</summary>
    public void ShowCategory(string key)
    {
        var section = Sections.FirstOrDefault(c => c.Key == key);
        if (section.Key is not null) ShowSection(section.Id);
    }

    // ── Разделы ───────────────────────────────────────────────────────────────

    private void ShowSection(Section section)
    {
        _section = section;
        SectionTitle.Text = Sections.First(s => s.Id == section).Title;
        General.IsVisible = section == Section.General;
        Sources.IsVisible = section == Section.Sources;
        Cards.IsVisible = section == Section.Cards;
        Look.IsVisible = section == Section.Look;
        Diagnostics.IsVisible = section == Section.Diagnostics;
        About.IsVisible = section == Section.About;
        DiagActions.IsVisible = section == Section.Diagnostics;
        Scroll.Offset = default;

        switch (section)
        {
            case Section.General: FillOpenWith(); break;
            case Section.Sources: FillMaxFile(); RefreshFolders(); _ = RefreshCacheAsync(); break;
            case Section.Cards: BuildCardControls(); break;
            case Section.Look: BuildLookControls(); break;
            case Section.Diagnostics: BuildLevelFilter(); _ = LoadLogAsync(reset: true); break;
            case Section.About: FillAbout(); break;
        }
    }

    // ── Общее ─────────────────────────────────────────────────────────────────

    private void FillOpenWith()
    {
        _syncing = true;
        var program = AppSettings.OpenWith;
        var items = new List<string> { "Как в системе" };
        if (program.Length > 0) items.Add(Path.GetFileNameWithoutExtension(program));
        items.Add("Выбрать программу…");
        OpenWithBox.ItemsSource = items;
        OpenWithBox.SelectedIndex = program.Length > 0 ? 1 : 0;
        _syncing = false;
    }

    private async void OnOpenWithChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || OpenWithBox.ItemsSource is not List<string> items) return;
        var index = OpenWithBox.SelectedIndex;
        if (index == 0) { AppSettings.OpenWith = ""; FillOpenWith(); return; }
        if (index != items.Count - 1) return;

        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Программа для PDF", AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) AppSettings.OpenWith = path;
        FillOpenWith();
    }

    // ── Источники и чтение ────────────────────────────────────────────────────

    private void FillMaxFile()
    {
        _syncing = true;
        MaxFileBox.ItemsSource = AppSettings.MaxFileMbOptions.Select(SettingsViewModel.MaxFileText).ToList();
        MaxFileBox.SelectedIndex = Array.IndexOf(AppSettings.MaxFileMbOptions, AppSettings.MaxFileMb);
        _syncing = false;
    }

    private void OnMaxFileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || MaxFileBox.SelectedIndex < 0) return;
        _vm.SetMaxFileMb(AppSettings.MaxFileMbOptions[MaxFileBox.SelectedIndex]);
    }

    private void OnMasksLostFocus(object? sender, RoutedEventArgs e) => _vm.SaveMasks();

    private void OnMasksKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.SaveMasks();
    }

    private void OnFolderQuery(object? sender, TextChangedEventArgs e) => RefreshFolders();

    private void RefreshFolders()
    {
        if (_library is not { } library) return;
        var reading = library.Reading.Running;
        var red    = Res.Brush("BrushRed");
        var orange = Res.Brush("BrushOrange");
        var muted  = Res.Brush("BrushSecondaryText");

        var all = library.Sources.Select(s =>
        {
            var unread = library.Documents.Count(d => d.SourceId == s.Id && !d.Missing &&
                                                      (d.TextState == DocTextState.NotRead || d.Changed));
            if (s.UnavailableSince is { } since)
                return new FolderRow(s.Id, s.Name, s.Path, s.Count.ToString("N0"),
                                     "недоступна с " + since.ToLocalTime().ToString(since.ToLocalTime().Date == DateTime.Today ? "HH:mm" : "dd.MM HH:mm"),
                                     red, red, 0);
            if (s.FirstScan)
                return new FolderRow(s.Id, s.Name, s.Path, "—", "проверяю…", orange,
                                     new SolidColorBrush(Color.Parse("#FF9500")), 1);
            if (reading && unread > 0)
                return new FolderRow(s.Id, s.Name, s.Path, s.Count.ToString("N0"), $"читаю: осталось {unread:N0}",
                                     orange, new SolidColorBrush(Color.Parse("#FF9500")), 2);
            var status = AppSettings.Watch
                ? "слежу"
                : s.CheckedAt is { } at ? "проверена в " + at.ToLocalTime().ToString("HH:mm") : "не проверялась";
            return new FolderRow(s.Id, s.Name, s.Path, s.Count.ToString("N0"), status, muted,
                                 new SolidColorBrush(Color.Parse("#34C759")), 3);
        }).ToList();

        var problems = all.Count(r => r.Rank == 0);
        var readingCount = all.Count(r => r.Rank is 1 or 2);
        BuildFolderFilters(all.Count, problems, readingCount);

        var query = (FolderQuery.Text ?? "").Trim();
        var rows = all.Where(r => _folderFilter switch
                       {
                           "problems" => r.Rank == 0,
                           "read"     => r.Rank is 1 or 2,
                           _          => true,
                       })
                      .Where(r => query.Length == 0 ||
                                  r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                  r.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                      .OrderBy(r => r.Rank).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();
        FolderRows.ItemsSource = rows;
        NoFolders.IsVisible = rows.Count == 0;

        var total = library.Sources.Sum(s => s.Count);
        var summary = $"{all.Count} {Plural.Of(all.Count, "папка", "папки", "папок")} · {total:N0} PDF";
        if (problems > 0) summary += $" · недоступна {problems}";
        if (rows.Count != all.Count) summary += $" · показано {rows.Count}";
        FolderSummary.Text = summary;
    }

    private void BuildFolderFilters(int all, int problems, int reading)
    {
        FolderFilters.Children.Clear();
        foreach (var (id, label) in new[] { ("all", $"Все {all}"), ("problems", $"С проблемами {problems}"), ("read", $"Читаются {reading}") })
        {
            FolderFilters.Children.Add(Segment(label, id == _folderFilter, () => { _folderFilter = id; RefreshFolders(); }));
        }
    }

    private void OnCheckAll(object? sender, RoutedEventArgs e) => _library?.CheckAll();

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (_library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new AddSourceDialog(library.Groups, null)) is NewSource result)
            library.AddSource(result.Path, result.Name, result.GroupId);
        RefreshFolders();
    }

    private void OnFolderMenu(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FolderRow row || _library is null) return;
        SourceMenu.Build(this, _library, row.Id).Open(sender as Control);
    }

    private async Task RefreshCacheAsync()
    {
        if (_library?.Index is not { } index)
        {
            CacheRead.Text = "кеш не открылся — поиск внутри документов недоступен";
            CacheScans.Text = CacheFailed.Text = CacheSize.Text = "—";
            CacheBig.IsVisible = CacheBigLabel.IsVisible = false;
            ClearCacheButton.IsEnabled = false;
            return;
        }
        var (stats, thumbs) = await Task.Run(() => (index.Stats(), DocThumbnail.Size(AppSettings.ThumbsDir)));
        CacheRead.Text   = $"{stats.Indexed:N0} {Plural.Documents(stats.Indexed)}";
        CacheScans.Text  = $"{stats.NoText:N0} — сканы, ищутся по названию";
        CacheFailed.Text = $"{stats.Failed + stats.Unsupported:N0} — повреждены или защищены паролем";
        CacheBig.Text    = $"{stats.TooBig:N0} — ищутся по названию; порог можно поднять выше";
        CacheBig.IsVisible = CacheBigLabel.IsVisible = stats.TooBig > 0;
        CacheSize.Text   = $"{Sizes.Text(stats.Bytes + thumbs)} · library-cache.db и миниатюры";
        ClearCacheButton.IsEnabled = true;
    }

    private async void OnClearCache(object? sender, RoutedEventArgs e)
    {
        if (_library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        var message = "Прочитанный текст и миниатюры удалятся. Поиск внутри документов заработает снова, когда " +
                      "программа прочитает их заново. Сами PDF не меняются.";
        if (await dialogs.ShowAsync(new ConfirmPanel("Очистить кеш документов", message, "Очистить")) is not true) return;
        ClearCacheButton.IsEnabled = false;
        await library.ClearCacheAsync();
        ThumbnailLoader.Forget();
        await RefreshCacheAsync();
    }

    // ── Лента и карточки ──────────────────────────────────────────────────────

    private void BuildCardControls()
    {
        SizeSegment.Children.Clear();
        foreach (var (label, size) in new[] { ("Маленькая", CardSize.Small), ("Средняя", CardSize.Medium), ("Большая", CardSize.Large) })
            SizeSegment.Children.Add(Segment(label, size == AppSettings.CardSize, () =>
            {
                AppSettings.CardSize = size;
                BuildCardControls();
            }));
        BuildPreview();
    }

    // Живой пример карточки — по тем же настройкам, что и лента. Подсвечено то, что подсветил
    // бы поиск: слова целиком, без других словоформ.
    private void BuildPreview()
    {
        if (_library is null) return;
        var doc = new DocItem
        {
            Id = -1, FileName = "ЦНС-38-44_РЭ.pdf", Folder = "Паспорта", DocType = "Руководство",
            Size = 3_250_000, Pages = 48, Sheet = "A4", Sheets = "A4 × 48", TextState = DocTextState.Read,
            InfoTitle = "Насос центробежный ЦНС 38-44. Руководство по эксплуатации",
        };
        var owner = new LibraryViewModel(_library, preview: true);
        var card = new CardViewModel(doc, owner)
        {
            Snippet = $"{DocTextIndex.HitOpen}Насос центробежный{DocTextIndex.HitClose} секционный ЦНС 38-44 предназначен " +
                      "для перекачивания воды с водородным показателем pH 7–8,5 и температурой до 45 °C. Работа " +
                      "центробежного насоса при закрытой задвижке на напорном " +
                      "трубопроводе более 2 минут не допускается. " +
                      $"{DocTextIndex.HitOpen}Насос{DocTextIndex.HitClose} применяется в системах водоснабжения промышленных " +
                      "предприятий, в установках повышения давления и для откачки воды из резервуаров. Перед пуском " +
                      "насоса корпус заполняется водой, воздух удаляется через " +
                      "пробку в крышке нагнетания; пуск всухую не допускается.",
        };
        owner.PreviewSource = "Проектная документация";
        Preview.Content = DocListView.PreviewCard(card, owner);
    }

    // ── Оформление ────────────────────────────────────────────────────────────

    // «Масштаб интерфейса» и «Размер шрифта» — пункты сегментов, как в макете.
    private static readonly double[] ScaleOptions = [0.9, 1.0, 1.1, 1.25];
    private static readonly (string Label, int Delta)[] FontOptions = [("Мелкий", -1), ("Стандартный", 0), ("Крупный", 2)];

    private void BuildLookControls()
    {
        ThemeTiles.Children.Clear();
        foreach (var (theme, title, bar, side, main, sep) in new[]
                 {
                     (AppTheme.Light, "Светлая", "#F2F2F7", "#FFFFFF", "#FFFFFF", "#E5E5EA"),
                     (AppTheme.Dark, "Темная", "#262629", "#1C1C1E", "#1C1C1E", "#38383C"),
                     (AppTheme.Auto, "Как в системе", "#F2F2F7", "#FFFFFF", "#1C1C1E", "#8E8E93"),
                 })
        {
            var on = AppSettings.Theme == theme;
            var preview = new Border
            {
                Width = 150, Height = 92, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                BorderThickness = new Thickness(on ? 2 : 1),
                BorderBrush = Res.Brush(on ? "BrushAccent" : "BrushSeparator"),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("18,42,*"),
                    Children =
                    {
                        new Border { Background = Brush.Parse(bar) },
                        Column(new Border { Background = Brush.Parse(side), BorderBrush = Brush.Parse(sep), BorderThickness = new Thickness(1, 0) }, 1),
                        Column(new Border { Background = Brush.Parse(main) }, 2),
                    },
                },
            };
            var label = new TextBlock
            {
                Text = title, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = Res.Brush(on ? "BrushAccent" : "BrushPrimaryText"),
            };
            // Плитка — не кнопка: у кнопки Fluent свой фон под мышью, а плитке он не нужен.
            var tile = new StackPanel
            {
                Spacing = 6, Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Children = { preview, label },
            };
            tile.Tapped += (_, _) =>
            {
                AppSettings.Theme = theme;
                ThemeManager.Apply(theme);
                BuildLookControls();
            };
            ThemeTiles.Children.Add(tile);
        }

        ScaleSegment.Children.Clear();
        foreach (var scale in ScaleOptions)
            ScaleSegment.Children.Add(Segment($"{scale * 100:0}%", Math.Abs(scale - Scaling.Current) < 0.001, () =>
            {
                Scaling.Set(scale);
                BuildLookControls();
            }));

        FontSegment.Children.Clear();
        foreach (var (label, delta) in FontOptions)
            FontSegment.Children.Add(Segment(label, delta == FontScale.Delta, () =>
            {
                FontScale.SetDelta(delta);
                BuildLookControls();
            }));

        var font  = Application.Current is { } app && app.TryFindResource("FontSizeBase", out var b) && b is double fb ? fb : 12;
        var small = Application.Current is { } app2 && app2.TryFindResource("FontSizeSmall", out var sm) && sm is double fs ? fs : 10;
        SampleNote.Text = $"Масштаб {Scaling.Current * 100:0}% · основной текст {font:0} px, мелкий {small:0} px";
    }

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    // Сегмент выбора — как «Текст / Как в документе» в панели чтения: обертка Border.modeseg
    // в разметке, кнопки ToggleButton.modeseg внутри. Выбран ровно один.
    private static ToggleButton Segment(string label, bool on, Action pick)
    {
        var b = new ToggleButton { Content = label, IsChecked = on };
        b.Classes.Add("modeseg");
        b.Click += (_, _) =>
        {
            b.IsChecked = true;
            pick();
        };
        return b;
    }

    // ── Диагностика ───────────────────────────────────────────────────────────

    private void BuildLevelFilter()
    {
        LevelSegment.Children.Clear();
        foreach (var (label, level) in new[] { ("Все", "all"), ("Ошибки", "errors"), ("Предупреждения", "warnings") })
            LevelSegment.Children.Add(Segment(label, level == _levelFilter, () =>
            {
                _levelFilter = level;
                BuildLevelFilter();
                _ = LoadLogAsync(reset: true);
            }));
    }

    private LogReader.LogQuery LevelQuery() => _levelFilter switch
    {
        "errors"   => new LogReader.LogQuery { Filters = new Dictionary<string, IReadOnlyCollection<string>> { ["level"] = ["Error", "Fatal"] } },
        "warnings" => new LogReader.LogQuery { Filters = new Dictionary<string, IReadOnlyCollection<string>> { ["level"] = ["Warning"] } },
        _          => new LogReader.LogQuery(),
    };

    private async Task LoadLogAsync(bool reset)
    {
        var reader = new LogReader(AppSettings.LogsDbPath);
        if (reset)
        {
            _logRows.Clear();
            _logQuery = LevelQuery();
        }
        var page = await reader.GetPageAsync(_logQuery, LogPage);
        _logRows.AddRange(page.Select(r => new LogRowView { Row = r }));
        if (page.Count > 0) _logQuery = _logQuery with { AfterId = page[^1].Id };
        LogRows.ItemsSource = null;
        LogRows.ItemsSource = _logRows;
        NoLogs.IsVisible = _logRows.Count == 0;
        MoreLogs.IsVisible = page.Count == LogPage;

        var count = await reader.CountAsync(LevelQuery());
        LogCount.Text = $"{count:N0} {Plural.Of((int)Math.Min(count, int.MaxValue), "запись", "записи", "записей")}";
        long bytes;
        try { bytes = File.Exists(AppSettings.LogsDbPath) ? new FileInfo(AppSettings.LogsDbPath).Length : 0; }
        catch { bytes = 0; }
        LogNote.Text = $"Журнал хранится в logs.db ({Sizes.Text(bytes)}). Записи старше 30 дней удаляются сами.";
    }

    private void OnMoreLogs(object? sender, RoutedEventArgs e) => _ = LoadLogAsync(reset: false);

    private void OnLogRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LogRowView row) row.IsOpen = !row.IsOpen;
    }

    private async void OnCopyLog(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LogRowView row || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync($"{row.Time} {row.Level}\n{row.Message}\n{row.Detail}".TrimEnd());
        MainWindow.NoticeOf(this, "Запись журнала скопирована.");
    }

    private async void OnExportLog(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Выгрузить журнал", SuggestedFileName = $"PdfLibrary-журнал-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            var reader = new LogReader(AppSettings.LogsDbPath);
            var query = LevelQuery();
            var count = await Task.Run(() => LogCsv.Write(path, reader.ReadCsvRows(query)));
            MainWindow.NoticeOf(this, $"Журнал выгружен: {count:N0} записей в {path}");
        }
        catch (Exception ex)
        {
            MainWindow.NoticeOf(this, $"Журнал не выгрузился: {ex.Message}", isError: true);
        }
    }

    private async void OnClearLog(object? sender, RoutedEventArgs e)
    {
        if (MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new ConfirmPanel("Очистить журнал",
                "Все записи журнала удалятся. Если нужно приложить журнал к обращению — сначала выгрузите его в CSV.",
                "Очистить")) is not true) return;
        await new LogReader(AppSettings.LogsDbPath).ClearAsync();
        Serilog.Log.Information("Журнал очищен");
        await LoadLogAsync(reset: true);
    }

    // ── О программе ───────────────────────────────────────────────────────────

    private void FillAbout()
    {
        VersionText.Text = $"Версия {AppSettings.AppVersion}" +
                           (AppSettings.BuildDate is { } built ? $" · сборка от {built:dd.MM.yyyy}" : "");
        DataDirText.Text = AppSettings.AppDataDir;
        OsText.Text = OsName();
        PlatformText.Text = $".NET {Environment.Version.Major}.{Environment.Version.Minor} · Avalonia " +
                            typeof(Application).Assembly.GetName().Version?.ToString(2);
        if (_library is { } library)
        {
            var docs = library.Documents.Count(d => !d.Missing);
            var sources = library.Sources.Count;
            LibraryText.Text = $"{docs:N0} {Plural.Documents(docs)} в {sources} " +
                               Plural.Of(sources, "источнике", "источниках", "источниках");
        }
    }

    private static string OsName()
    {
        var arch = RuntimeInformation.OSArchitecture == Architecture.X64 ? "64-разрядная" : RuntimeInformation.OSArchitecture.ToString();
        if (OperatingSystem.IsWindows())
            return $"Windows {(Environment.OSVersion.Version.Build >= 22000 ? "11" : "10")}, {arch}";
        return $"{RuntimeInformation.OSDescription}, {arch}";
    }

    private void OnUpdateFolderLostFocus(object? sender, RoutedEventArgs e) => _vm.SaveUpdateFolder();

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        _vm.SaveUpdateFolder();
        CheckUpdateButton.IsEnabled = false;
        ApplyUpdateButton.IsVisible = false;
        UpdateStatus.IsVisible = true;
        UpdateIcon.IsVisible = false;
        UpdateText.Text = "Проверяю…";
        UpdateText.Foreground = Res.Brush("BrushSecondaryText");
        try
        {
            _update = await UpdateService.CheckAsync(AppSettings.UpdateFolder);
            UpdateText.Text = _update.Message;
            UpdateIcon.IsVisible = _update.Result == UpdateCheck.UpToDate;
            UpdateText.Foreground = Res.Brush(_update.Result == UpdateCheck.Error ? "BrushRed" : "BrushPrimaryText");
            ApplyUpdateButton.IsVisible = _update.Result == UpdateCheck.Available;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void OnApplyUpdate(object? sender, RoutedEventArgs e)
    {
        if (_update?.Artifact is not { } artifact) return;
        if (InstallService.IsRunningInstalledCopy() != true)
        {
            UpdateText.Text = "Обновлять можно только установленную программу — запустите ее из папки установки.";
            UpdateText.Foreground = Res.Brush("BrushRed");
            return;
        }
        if (MainWindow.DialogsOf(this) is { } dialogs &&
            await dialogs.ShowAsync(new ConfirmPanel("Обновление",
                _update.Message + " Программа закроется, обновится и снова откроется.", "Обновить")) is not true)
            return;
        ApplyUpdateButton.IsEnabled = false;
        UpdateText.Text = "Загружаю обновление…";
        var error = await UpdateService.ApplyAsync(artifact, new Progress<double>(p => UpdateText.Text = $"Загружаю обновление… {p * 100:0} %"));
        if (error is null) return;
        ApplyUpdateButton.IsEnabled = true;
        UpdateText.Text = error;
        UpdateText.Foreground = Res.Brush("BrushRed");
    }

    private void OnOpenDataDir(object? sender, RoutedEventArgs e)
    {
        if (DocActions.ShowInFolder(AppSettings.AppDataDir) is { } error) MainWindow.NoticeOf(this, error, isError: true);
    }
}

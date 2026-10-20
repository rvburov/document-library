// Стенд экранов: настоящее главное окно программы на подготовленной библиотеке, без экрана.
//
// Зачем, если есть модульные и интеграционные тесты. Они проверяют числа и строки, а половина
// того, что ломается в интерфейсе, ломается не в числах: ресурс в разметке (иконка, кисть, тема
// кнопки) разрешается при ЗАГРУЗКЕ, и опечатка в ключе роняет экран в тот момент, когда человек
// его открыл; привязка к несуществующему свойству молча показывает пустоту. Стенд открывает все
// экраны, проходит поиск, панель чтения и настройки и сохраняет кадры в PNG: их сверяют с
// макетом глазами.
//
// Запуск: dotnet run --project Tests/Screens [-- папка-для-кадров]
// Кадры по умолчанию — в artifacts/screens.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PdfLibrary;
using PdfLibrary.Core.Documents;
using PdfLibrary.Database;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Services;
using PdfLibrary.Tests.Screens;
using PdfLibrary.Utils;
using PdfLibrary.Views;
using PdfLibrary.Views.Library;
using PdfLibrary.Views.Reader;
using PdfLibrary.Views.Settings;

var data = Path.Combine(Path.GetTempPath(), "pl-screens-" + Guid.NewGuid().ToString("N")[..8]);
Environment.SetEnvironmentVariable(AppSettings.DataDirVariable, Path.Combine(data, "appdata"));
var ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ru;
System.Globalization.CultureInfo.CurrentCulture = ru;

var shots = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(RepoRoot(), "artifacts", "screens");
Directory.CreateDirectory(shots);

AppBuilder.Configure<App>()
          .UseSkia()
          .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
          .WithInterFont()
          .SetupWithoutStarting();

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    if (!ok) failures.Add(what);
}

try
{
    var docs = Path.Combine(data, "docs");
    SamplePdfs.Write(docs);
    Console.WriteLine($"Документы стенда: {(SamplePdfs.Cyrillic ? "русские" : "латиницей — шрифта с кириллицей нет")}");

    AppSettings.EnsureCreated();
    PdfLibrary.Logging.AppLog.Init();
    Serilog.Log.Information("Запуск программы, версия {Version:l} (стенд экранов)", AppSettings.AppVersion);
    var store = LibraryStore.Open(AppSettings.LibraryDbPath);
    AppSettings.AttachStore(store);
    DocIndexService.Attach(AppSettings.CachePath, AppSettings.MaxFileMb);
    var library = new LibraryService(store, DocIndexService.Current, a => Dispatcher.UIThread.Post(a));
    library.Load();
    ThemeManager.Apply(AppSettings.Theme);
    FontScale.Init();
    Scaling.Load();

    var window = new MainWindow(library) { Width = 1331, Height = 813 };
    window.Show();
    Pump(TimeSpan.FromMilliseconds(300));

    Console.WriteLine("Первый запуск");
    Check(Find<PdfLibrary.Views.Welcome.WelcomeView>(window)?.IsVisible == true, "пустая библиотека открывается экраном первого запуска");
    Shot(window, "01-welcome");

    // Источники — как в макете: группа «Насосная станция» из проектной документации и загрузок.
    var project = library.AddSource(Path.Combine(docs, "Проектная документация"), "", null)!;
    var archive = library.AddSource(Path.Combine(docs, "Сетевой архив"), "", null)!;
    var downloads = library.AddSource(Path.Combine(docs, "Загрузки"), "", null)!;
    Until(() => !library.Checking && library.Documents.Count == 9, "проверка папок", TimeSpan.FromSeconds(60));
    Shot(window, "02-welcome-added");
    library.AddGroup("Насосная станция", [project.Id, downloads.Id]);
    Until(() => !library.Reading.Running && library.Documents.All(d => d.TextState != DocTextState.NotRead),
          "чтение документов", TimeSpan.FromSeconds(120));
    Check(library.Documents.Count(d => d.TextState == DocTextState.Read) == 8, "прочитано 8 документов, один скан");
    Check(library.Documents.Single(d => d.FileName.StartsWith("Паспорт_")).TextState == DocTextState.NoText, "скан без текстового слоя");

    var green = library.AddTag("Действующий", "#34C759");
    var orange = library.AddTag("На согласовании", "#FF9500");
    library.AddTag("Отменен", "#8E8E93");
    library.SetTag(green.Id, library.Documents.Where(d => d.DocType is DocTypes.Gost or DocTypes.Manual or DocTypes.Drawing).ToList(), true);
    library.SetTag(orange.Id, library.Documents.Where(d => d.DocType is DocTypes.Protocol or DocTypes.Scheme).ToList(), true);

    Click(window, "Открыть библиотеку");
    Pump(TimeSpan.FromMilliseconds(800));

    Console.WriteLine("Лента");
    var feed = Find<FeedView>(window)!;
    var vm = (LibraryViewModel)feed.DataContext!;
    Check(feed.IsVisible, "лента показана после «Открыть библиотеку»");
    Check(vm.Cards.Count == 9, $"в ленте 9 карточек (есть {vm.Cards.Count})");
    Check(vm.Subtitle.StartsWith("9 документов · 3 источника"), $"подзаголовок: {vm.Subtitle}");
    Check(vm.SourceRows.Any(r => r.IsGroup && r.Name == "Насосная станция"), "группа в боковой панели");
    Check(vm.TagRows.Count == 3, "три метки в боковой панели");
    Pump(TimeSpan.FromSeconds(2));   // миниатюры
    Shot(window, "03-feed");

    vm.Query = "насос центробежный";
    vm.ApplySearch();
    Until(() => vm.AppliedQuery.Length > 0, "поиск", TimeSpan.FromSeconds(10));
    Pump(TimeSpan.FromSeconds(1));
    Check(vm.Cards.Count >= 4, $"поиск «насос центробежный» находит не меньше 4 документов (нашел {vm.Cards.Count})");
    Check(vm.Cards.Count(c => c.HasSnippet) >= 3, "у найденного по тексту есть строки с подсветкой");
    Check(vm.StatusText.StartsWith("Найдено"), $"строка состояния: {vm.StatusText}");
    Shot(window, "04-feed-search");

    vm.Cards[0].IsSelected = true;
    vm.Cards[1].IsSelected = true;
    Pump(TimeSpan.FromMilliseconds(300));
    Check(vm.SelectedText == "Выбрано 2", "панель действий с выбранными");
    Shot(window, "05-feed-selection");
    vm.ClearSelection();

    Console.WriteLine("Тип документа и метки");
    var drawings = vm.TypeRows.FirstOrDefault(t => t.Name == DocTypes.Drawing);
    Check(drawings is not null, "тип «Чертеж» в фильтре");
    vm.ClearSearch();
    Until(() => vm.AppliedQuery.Length == 0, "сброс поиска", TimeSpan.FromSeconds(5));
    vm.PickTag(vm.TagRows.First(t => t.Name == "Действующий"));
    Pump(TimeSpan.FromMilliseconds(300));
    Check(vm.Cards.All(c => c.Doc.Tags.Contains(green.Id)) && vm.Cards.Count > 0, "фильтр по метке");
    vm.PickTag(vm.TagRows.First(t => t.Name == "Действующий"));
    Pump(TimeSpan.FromMilliseconds(300));

    Console.WriteLine("Панель чтения");
    vm.Query = "насос центробежный";
    vm.ApplySearch();
    Until(() => vm.AppliedQuery.Length > 0, "поиск", TimeSpan.FromSeconds(10));
    var manual = vm.Cards.First(c => c.Doc.FileName == "ЦНС-38-44_РЭ.pdf");
    feed.RequestRead(manual);
    var reader = Find<ReaderScreen>(window)!;
    Until(() => reader.Reader?.IsOpen == true && reader.Reader.Hits.Count > 0 && !reader.Reader.Counting,
          "открытие документа и поиск по нему", TimeSpan.FromSeconds(30));
    Pump(TimeSpan.FromSeconds(2));   // страницы и миниатюры страниц
    Check(reader.IsVisible, "панель чтения показана");
    Check(reader.Reader!.PageCount == 8, $"8 страниц (есть {reader.Reader.PageCount})");
    Check(reader.Reader.Counter.Contains(" из "), $"счетчик совпадений: {reader.Reader.Counter}");
    Check(reader.Reader.StatusRight.StartsWith("Текстовый слой есть · A4 × 8"), $"строка состояния: {reader.Reader.StatusRight}");
    Shot(window, "06-reader");
    // F3 и Shift+F3 — клавишами в окно, как нажал бы человек: переход идет через обработчик окна.
    window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
    Pump(TimeSpan.FromMilliseconds(300));
    Check(reader.Reader.CurrentHit == 1, $"F3 — следующее совпадение: {reader.Reader.Counter}");
    window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
    Pump(TimeSpan.FromMilliseconds(300));
    Check(reader.Reader.CurrentHit == 0, $"Shift+F3 — предыдущее совпадение: {reader.Reader.Counter}");
    window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
    Pump(TimeSpan.FromSeconds(1));
    Shot(window, "07-reader-next-hit");

    var drawing = vm.Cards.FirstOrDefault(c => c.Doc.FileName == "АБВГ.301122.001 ВО.pdf");
    if (drawing is not null)
    {
        reader.Open(drawing);
        Until(() => reader.Reader.IsOpen && reader.Reader.Doc == drawing.Doc, "открытие чертежа", TimeSpan.FromSeconds(30));
        Pump(TimeSpan.FromSeconds(2));
        Check(reader.Reader.Doc!.Sheets == "A1 × 1, A3 × 2", $"форматы чертежа: {reader.Reader.Doc.Sheets}");
        Shot(window, "08-reader-drawing");
    }

    Console.WriteLine("Настройки");
    Click(window, null, "Настройки");
    Pump(TimeSpan.FromMilliseconds(500));
    var settings = Find<SettingsView>(window)!;
    Check(settings.IsVisible, "настройки открыты");
    var names = new[] { "Общее", "Источники и чтение", "Лента и карточки", "Оформление", "Диагностика", "О программе" };
    for (var i = 0; i < names.Length; i++)
    {
        Click(settings, names[i]);
        Pump(TimeSpan.FromMilliseconds(800));
        Shot(window, $"{09 + i:D2}-settings-{i + 1}");
        if (names[i] == "Диагностика")
            Check(Texts(settings).Any(t => t.StartsWith("Журнал хранится в logs.db")), "диагностика читает журнал");
    }

    Console.WriteLine("Темная тема");
    AppSettings.Theme = AppTheme.Dark;
    ThemeManager.Apply(AppTheme.Dark);
    Click(settings, "Оформление");
    Pump(TimeSpan.FromMilliseconds(500));
    Shot(window, "15-settings-look-dark");
    Click(window, null, "Библиотека");
    Pump(TimeSpan.FromMilliseconds(500));
    var readerVisible = Find<ReaderScreen>(window)!.IsVisible;
    Shot(window, readerVisible ? "16-reader-dark" : "16-feed-dark");
    if (readerVisible)
    {
        Click(window, null, "Закрыть панель чтения");
        Pump(TimeSpan.FromMilliseconds(800));
        Shot(window, "17-feed-dark");
    }

    window.Close();
    library.Dispose();
    PdfLibrary.Logging.AppLog.Shutdown();
    await DocIndexService.DetachAsync();
}
catch (Exception ex)
{
    failures.Add("стенд упал: " + ex);
    Console.WriteLine(ex);
}
finally
{
    SqliteGate.ClearAllPools();
    try { Directory.Delete(data, recursive: true); } catch { /* занят — уберет система */ }
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? $"Стенд экранов: все проверки прошли. Кадры — {shots}" : $"Стенд экранов: не прошло {failures.Count}");
foreach (var f in failures) Console.WriteLine("  - " + f);
return failures.Count == 0 ? 0 : 1;

// ── Помощники ────────────────────────────────────────────────────────────────

void Pump(TimeSpan time)
{
    var end = DateTime.UtcNow + time;
    while (DateTime.UtcNow < end)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(15);
    }
    Dispatcher.UIThread.RunJobs();
}

void Until(Func<bool> condition, string what, TimeSpan limit)
{
    var end = DateTime.UtcNow + limit;
    while (DateTime.UtcNow < end)
    {
        Dispatcher.UIThread.RunJobs();
        if (condition()) return;
        Thread.Sleep(20);
    }
    Check(false, "не дождались: " + what);
}

void Shot(Window window, string name)
{
    Dispatcher.UIThread.RunJobs();
    var frame = window.CaptureRenderedFrame();
    if (frame is null) { Check(false, "кадр не снят: " + name); return; }
    frame.Save(Path.Combine(shots, name + ".png"));
}

static T? Find<T>(Visual root) where T : Visual =>
    root.GetVisualDescendants().OfType<T>().FirstOrDefault();

static IEnumerable<string> Texts(Visual root) =>
    root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

// Нажать видимую кнопку по надписи внутри нее или по подсказке.
void Click(Visual root, string? text, string? tip = null)
{
    var button = root.GetVisualDescendants().OfType<Button>()
        .Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled)
        .FirstOrDefault(b => (text is not null && (Equals(b.Content, text) ||
                                                   b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text)))
                          || (tip is not null && Equals(ToolTip.GetTip(b), tip)));
    if (button is null) { Check(false, $"кнопка «{text ?? tip}» не найдена"); return; }
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdfLibrary.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

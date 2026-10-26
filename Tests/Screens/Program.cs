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
using PdfLibrary.Setup;
using PdfLibrary.Tests.Screens;
using PdfLibrary.Utils;
using PdfLibrary.Views;
using Avalonia.Controls.Primitives;
using PdfLibrary.Controls.Components;
using PdfLibrary.Views.Library;
using PdfLibrary.Views.Settings;
using PdfLibrary.Views.Dialogs;

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
    var feed = Find<LibraryView>(window)!;
    var vm = feed.Vm!;
    Check(feed.IsVisible, "лента показана после «Открыть библиотеку»");
    Check(vm.Cards.Count == 9, $"в ленте 9 карточек (есть {vm.Cards.Count})");
    Check(Texts(window).Contains("Библиотека PDF"), "заголовок раздела в навбаре");
    Check(vm.SourceRows.Any(r => r.IsGroup && r.Name == "Насосная станция"), "группа в боковой панели");
    Check(vm.TagRows.Count == 3, "три метки в боковой панели");
    var sidebar = Find<LibrarySidebar>(window)!;
    Check(Texts(sidebar).Contains("Метки") && Texts(sidebar).Contains("Действующий"), "узел «Метки» с метками в боковой панели");
    Check(Find<ActivityBar>(window)?.Bounds.Width == 48, "панель разделов шириной 48");
    var grips = window.GetVisualDescendants().OfType<Border>()
        .Count(b => b.IsEffectivelyVisible && b.Cursor?.ToString() == "SizeWestEast");
    Check(grips == 1, $"ручка ширины боковой панели видна (ручек {grips})");
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
    Check(vm.SelectedCount == 2 && !Texts(feed).Contains("Выбрано 2") && !Texts(feed).Contains("Копировать в папку"),
          "выбор двух документов не показывает нижнюю плашку действий");
    Shot(window, "05-feed-selection");
    vm.ClearSelection();

    Console.WriteLine("Тип документа и метки");
    var drawings = vm.TypeRows.FirstOrDefault(t => t.Name == DocTypes.Drawing);
    Check(drawings is not null, "тип «Чертеж» в фильтре");
    vm.SetTypes(new HashSet<string> { DocTypes.Drawing });
    Pump(TimeSpan.FromMilliseconds(300));
    Check(vm.Cards.Count > 0 && vm.Cards.All(c => c.Doc.DocType == DocTypes.Drawing), "фильтр «Тип документа»");
    Check(vm.TypeLabel == "Тип документа: 1", $"подпись фильтра: {vm.TypeLabel}");
    vm.SetTypes(null);
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
    var reader = Find<DocReaderView>(window)!;
    Until(() => reader.Document == manual.Doc && reader.PagesScanned && reader.CounterText.Contains(" из "),
          "открытие документа и поиск по его страницам", TimeSpan.FromSeconds(30));
    Pump(TimeSpan.FromSeconds(2));   // картинки страниц
    Check(reader.IsVisible, "панель чтения показана");
    Check(reader.PageCount == 8, $"8 страниц (есть {reader.PageCount})");
    Check(reader.StateText.Length == 0, $"на месте страниц нет надписи: {reader.StateText}");
    Check(!reader.GetVisualDescendants().OfType<ToggleButton>().Any(b => Equals(b.Content, "Текст")),
          "режима «Текст» нет — только страницы");
    Check(reader.CurrentMatch == 0, $"текущее совпадение — первое: {reader.CounterText}");
    grips = window.GetVisualDescendants().OfType<Border>()
        .Count(b => b.IsEffectivelyVisible && b.Cursor?.ToString() == "SizeWestEast");
    Check(grips == 2, $"ручки ширины у боковой панели и у панели чтения (ручек {grips})");
    Shot(window, "06-reader");
    // F3 и Shift+F3 — клавишами в окно, как нажал бы человек: переход идет через обработчик окна.
    window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
    Pump(TimeSpan.FromSeconds(1));
    Check(reader.CurrentMatch == 1, $"F3 — следующее совпадение: {reader.CounterText}");
    window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
    Pump(TimeSpan.FromMilliseconds(300));
    Check(reader.CurrentMatch == 0, $"Shift+F3 — предыдущее совпадение: {reader.CounterText}");
    window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
    Pump(TimeSpan.FromSeconds(1));
    Shot(window, "07-reader-next-hit");

    Console.WriteLine("Очистка поиска клавиатурой");
    var searchBox = feed.GetVisualDescendants().OfType<TextBox>()
        .Single(b => Equals(b.Watermark, "Найти в документах"));
    searchBox.Focus();
    searchBox.SelectAll();
    window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
    window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
    Pump(TimeSpan.FromMilliseconds(500));
    Check(searchBox.Text == "" && vm.Query == "", "запрос удален клавишей Backspace");
    Check(!vm.Searching && vm.AppliedQuery == "" && vm.Cards.Count == 9,
          "удаление запроса сразу возвращает все 9 документов без Enter");
    Check(vm.Cards.All(c => !c.HasSnippet) && !vm.StatusText.StartsWith("Найдено"),
          "после очистки сняты фрагменты и строка результата поиска");
    Check(reader.CounterText == "" && reader.FindControl<TextBox>("FindBox")!.Text == "",
          "очистка поиска снимает запрос и счетчик в открытом документе");
    // После неуспешной проверки возвращаем исходное состояние для остальных проверок стенда.
    if (vm.Searching) vm.ClearSearch();

    vm.Query = "договор";
    vm.ApplySearch();
    vm.Query = "   ";
    Pump(TimeSpan.FromMilliseconds(500));
    Check(!vm.Searching && vm.AppliedQuery == "" && vm.Cards.Count == 9,
          "пробельный запрос отменяет незавершенный поиск и не возвращает старую выдачу");
    vm.ClearSearch();
    Pump(TimeSpan.FromMilliseconds(300));

    // Скан без текстового слоя: страницы есть, искать на них нечего — плашка внизу
    // говорит об этом, а не оставляет человека с пустым счетчиком.
    var scan = vm.Cards.FirstOrDefault(c => c.Doc.TextState == DocTextState.NoText);
    if (scan is not null)
    {
        var cardsHost = feed.GetVisualDescendants().OfType<ItemsControl>()
            .Single(c => c.Name == "CardsHost");
        cardsHost.ScrollIntoView(scan);
        Pump(TimeSpan.FromMilliseconds(300));
        var card = cardsHost.ContainerFromIndex(vm.Cards.ToList().IndexOf(scan))!;
        var readLink = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Прочитать");
        var readPoint = readLink.TranslatePoint(new Point(readLink.Bounds.Width / 2, readLink.Bounds.Height / 2), window)!.Value;
        window.MouseMove(readPoint);
        window.MouseDown(readPoint, MouseButton.Left);
        window.MouseUp(readPoint, MouseButton.Left);
        Until(() => reader.Document == scan.Doc && reader.PageCount > 0, "открытие скана", TimeSpan.FromSeconds(30));
        Check(reader.Document == scan.Doc && reader.Document != manual.Doc,
              "после очистки нажатие «Прочитать» открывает другой документ вместо прежнего результата");
        Pump(TimeSpan.FromMilliseconds(500));
        Check(reader.NoteText.Contains("Скан без текстового слоя"), $"у скана строка про текстовый слой: {reader.NoteText}");
        var note = reader.FindControl<TextBlock>("CutLabel")!;
        var pages = reader.FindControl<ListBox>("PagesList")!;
        var scroll = pages.GetVisualDescendants().OfType<ScrollViewer>().First();
        var noteTop = note.TranslatePoint(new Point(), reader)!.Value.Y;
        Check(noteTop > reader.Bounds.Height / 2, "сообщение о скане находится внизу панели чтения");
        scroll.Offset = new Vector(0, 500);
        Pump(TimeSpan.FromMilliseconds(500));
        Check(scroll.Offset.Y > 0, "страницы скана прокручены");
        Check(Math.Abs(note.TranslatePoint(new Point(), reader)!.Value.Y - noteTop) < 1,
              "сообщение о скане закреплено при прокрутке страниц");
        Shot(window, "07-reader-scan-note");
    }
    else Check(false, "скан в ленте");

    var drawing = vm.Cards.FirstOrDefault(c => c.Doc.FileName == "АБВГ.301122.001 ВО.pdf");
    if (drawing is not null)
    {
        feed.RequestRead(drawing);
        Until(() => reader.Document == drawing.Doc && reader.PageCount > 0, "открытие чертежа", TimeSpan.FromSeconds(30));
        Pump(TimeSpan.FromSeconds(2));
        Check(drawing.Doc.Sheets == "A1 × 1, A3 × 2", $"форматы чертежа: {drawing.Doc.Sheets}");
        Shot(window, "08-reader-drawing");
    }

    Console.WriteLine("Настройки");
    Click(window, null, "Настройки");
    Pump(TimeSpan.FromMilliseconds(500));
    var settings = Find<SettingsView>(window)!;
    var sections = Find<SidebarList>(window)!;
    Check(settings.IsVisible && sections.IsVisible, "настройки открыты, разделы — в боковой панели");
    var keys = SettingsView.Categories.Select(c => c.Key).ToList();
    for (var i = 0; i < keys.Count; i++)
    {
        sections.Select(keys[i]);
        Pump(TimeSpan.FromMilliseconds(800));
        var groups = settings.GetVisualDescendants().OfType<Border>()
            .Where(b => b.IsEffectivelyVisible && b.Classes.Contains("group")).ToList();
        Check(groups.Count > 0 && groups.All(b => b.Bounds.Width > settings.Bounds.Width - 64),
              $"блоки настроек на всю ширину: {keys[i]}");
        Shot(window, $"{09 + i:D2}-settings-{i + 1}");
        if (keys[i] == "diagnostics")
            Check(Texts(settings).Any(t => t.StartsWith("Журнал хранится в logs.db")), "диагностика читает журнал");
        if (keys[i] == "look")
        {
            var labels = Texts(settings).ToHashSet();
            Check(!labels.Overlaps(["ТЕМА", "Светлая", "Темная", "Как в системе"]),
                  "в оформлении нет выбора цветовой темы");
            Check(labels.Contains("Масштаб интерфейса") && labels.Contains("Размер шрифта") && labels.Contains("ПРИМЕР ТЕКСТА"),
                  "остальные пункты оформления сохранены");
        }
    }

    Console.WriteLine("Ручная проверка обновления");
    var updateFolder = Path.Combine(data, "update-check");
    Directory.CreateDirectory(updateFolder);
    var updateFile = InstallService.IsMac
        ? InstallService.MacInnerBinary(Path.Combine(updateFolder, InstallService.AppBundleName))
        : Path.Combine(updateFolder, InstallService.AppExeName);
    Directory.CreateDirectory(Path.GetDirectoryName(updateFile)!);
    File.Copy(Environment.ProcessPath!, updateFile);
    File.SetLastWriteTimeUtc(updateFile, DateTime.UtcNow.AddMinutes(1));
    Check(UpdateService.Check(updateFolder).Result == UpdateCheck.Available, "подготовлена новая сборка для проверки обновления");
    var settingsVm = (SettingsViewModel)settings.DataContext!;
    settingsVm.UpdateFolder = updateFolder;
    var checkUpdate = settings.FindControl<Button>("CheckUpdateButton")!;
    var dialogs = Find<DialogHost>(window)!;
    Click(settings, "Проверить");
    Until(() => dialogs.IsOpen || checkUpdate.IsEnabled, "проверка обновления", TimeSpan.FromSeconds(10));
    var updatePrompt = Find<ConfirmPanel>(window);
    Check(dialogs.IsOpen && updatePrompt is not null && !checkUpdate.IsEnabled,
          "кнопка «Проверить» сразу открывает окно обновления и остается недоступной до его закрытия");
    Check(!settings.FindControl<StackPanel>("UpdateStatus")!.IsVisible,
          "новая сборка не выводится отдельной строкой со ссылкой «Обновить»");
    if (updatePrompt is not null)
    {
        Shot(window, "14a-update-confirmation");
        Click(updatePrompt, "Отмена");
        Pump(TimeSpan.FromMilliseconds(200));
        Check(!dialogs.IsOpen && checkUpdate.IsEnabled, "отмена закрывает окно обновления и возвращает кнопку проверки");
    }
    settingsVm.UpdateFolder = Path.Combine(data, "missing-update-folder");
    Click(settings, "Проверить");
    Until(() => checkUpdate.IsEnabled, "проверка недоступной папки обновлений", TimeSpan.FromSeconds(10));
    Check(!dialogs.IsOpen && Texts(settings).Any(t => t.StartsWith("Папка обновлений недоступна")),
          "ошибка проверки показана в настройках без предложения обновиться");
    File.SetLastWriteTimeUtc(updateFile, File.GetLastWriteTimeUtc(Environment.ProcessPath!));
    settingsVm.UpdateFolder = updateFolder;
    Click(settings, "Проверить");
    Until(() => checkUpdate.IsEnabled, "проверка актуальной сборки", TimeSpan.FromSeconds(10));
    Check(!dialogs.IsOpen && Texts(settings).Any(t => t.StartsWith("Установлена последняя версия")),
          "актуальная сборка не открывает окно обновления");

    Check(Math.Abs(Scaling.Current - 1) < 0.001, $"открытие настроек не меняет масштаб ({Scaling.Current:P0})");
    Check(FontScale.Delta == 0, $"открытие настроек не меняет размер шрифта ({FontScale.Delta})");

    Console.WriteLine("Темная тема");
    AppSettings.Theme = AppTheme.Dark;
    ThemeManager.Apply(AppTheme.Dark);
    sections.Select("look");
    Pump(TimeSpan.FromMilliseconds(500));
    Shot(window, "15-settings-look-dark");
    Click(window, null, "Библиотека");
    Pump(TimeSpan.FromMilliseconds(800));
    Check(reader.IsVisible, "панель чтения осталась открытой после настроек");
    Shot(window, "16-reader-dark");
    // Нажатие мышью проверяет доступность кнопки: смена IsChecked из кода не замечает,
    // что панель документа перекрыла кнопку вместе с правой частью ленты.
    var readerToggle = window.GetVisualDescendants().OfType<ToggleButton>()
        .First(b => b.IsEffectivelyVisible && Equals(ToolTip.GetTip(b), "Закрыть панель чтения"));
    var initialWidth = window.Width;
    foreach (var width in new[] { initialWidth, 1662, window.MinWidth })
    {
        window.Width = width;
        feed.RequestRead(manual);
        Pump(TimeSpan.FromMilliseconds(800));
        var closePoint = readerToggle.TranslatePoint(
            new Point(readerToggle.Bounds.Width / 2, readerToggle.Bounds.Height / 2), window)!.Value;
        var readerLeft = reader.TranslatePoint(new Point(), window)!.Value.X;
        Check(closePoint.X < readerLeft, $"кнопка закрытия слева от панели чтения, окно {width}");
        window.MouseMove(closePoint);
        window.MouseDown(closePoint, MouseButton.Left);
        window.MouseUp(closePoint, MouseButton.Left);
        Pump(TimeSpan.FromMilliseconds(800));
        Check(!reader.IsVisible, $"панель чтения закрыта мышью, окно {width}");
    }
    window.Width = initialWidth;
    Pump(TimeSpan.FromMilliseconds(300));
    Shot(window, "17-feed-dark");

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

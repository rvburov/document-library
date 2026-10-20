using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using PdfLibrary.Core.Documents;
using PdfLibrary.Database;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Notifications;
using PdfLibrary.Services;
using PdfLibrary.Setup;
using PdfLibrary.Utils;
using PdfLibrary.Views;
using Serilog;

namespace PdfLibrary;

public partial class App : Application
{
    private TrayIcon? _tray;

    /// <summary>Библиотека программы; null — до конца старта и в режиме установщика.</summary>
    public static LibraryService? Library { get; private set; }

    /// <summary>Значок у часов создан — окно можно прятать при закрытии.</summary>
    public static bool TrayShown { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Режим установщика или удаления: окно установки вместо программы, библиотеку не
            // трогаем. Подробности — docs/developer/guides/install-and-update.md.
            if (Program.Mode != SetupMode.App)
            {
                desktop.MainWindow = new InstallerWindow(Program.Mode == SetupMode.Uninstall);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // Необработанное исключение на UI-потоке (обработчик клика, продолжение async void)
            // иначе вышло бы из цикла сообщений и завершило процесс. Ловим здесь: след остается в
            // crash.log и logs.db, работа продолжается. Точечные catch в UI остаются основной
            // защитой, это последний рубеж внутри процесса.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Program.LogCrash("ui-handled", e.Exception);
                e.Handled = true;
            };

            // Залежавшийся временный файл обновления — фоном, старт не ждет.
            _ = Task.Run(InstallService.CleanStaleUpdateArtifacts);

            // Пока библиотека открывается, программа не должна закрыться от отсутствия главного
            // окна — держим явное завершение, норма вернется после показа окна.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ThemeManager.Apply(AppTheme.Light);

            var splash = new SplashWindow();
            splash.Show();

            _ = Task.Run(() =>
            {
                LibraryService library;
                try
                {
                    library = Prepare();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Библиотека не открылась на старте: {Path:l}", AppSettings.LibraryDbPath);
                    Dispatcher.UIThread.Post(() =>
                    {
                        splash.SetError($"Не удалось открыть библиотеку:\n{ex.Message}");
                        DispatcherTimer.RunOnce(() => desktop.Shutdown(1), TimeSpan.FromSeconds(6));
                    });
                    return;
                }

                Dispatcher.UIThread.Post(() => ShowMain(desktop, splash, library));
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Фоновая часть старта: база, настройки, автозапуск, кеш прочитанного, список документов.
    /// Сбой library.db — исключение наружу: без нее программе показывать нечего.
    /// </summary>
    private static LibraryService Prepare()
    {
        var store = LibraryStore.Open(AppSettings.LibraryDbPath);
        AppSettings.AttachStore(store);

        // Автозапуск: сверить запись в системе с настройкой — вернуть пропавшую (переустановка,
        // чистилка автозагрузки), переписать устаревшую (сменилась папка установки), снять лишнюю.
        Autostart.Sync(AppSettings.Autostart);

        // Кеш прочитанного. Его сбой работу не останавливает: лента и фильтры живут в
        // library.db, а без кеша пропадает только поиск внутри документов — об этом говорит
        // строка состояния поиска. Поврежденный файл кеш пересоздает сам.
        DocIndexService.Attach(AppSettings.CachePath, AppSettings.MaxFileMb);
        try { DocThumbnail.Tidy(AppSettings.ThumbsDir); }
        catch (Exception ex) { Log.Warning(ex, "Миниатюры не убраны: {Path:l}", AppSettings.ThumbsDir); }

        var library = new LibraryService(store, DocIndexService.Current, a => Dispatcher.UIThread.Post(a));
        library.Load();
        return library;
    }

    private void ShowMain(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, LibraryService library)
    {
        Library = library;
        ThemeManager.Apply(AppSettings.Theme);
        FontScale.Init();
        Scaling.Load();

        var main = new MainWindow(library);
        desktop.MainWindow = main;
        main.Show();
        splash.Close();
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Уведомления — по настройкам «Общего».
        library.NewDocumentsFound += (count, source) =>
        {
            if (AppSettings.NotifyNew)
                Notifier.Show("Новые документы", $"{count} {Plural.Documents(count)} в источнике «{source}»");
        };
        library.ReadingFinished += done =>
        {
            if (AppSettings.NotifyRead)
                Notifier.Show("Чтение документов закончено", $"Прочитано {done} {Plural.Documents(done)}");
        };

        UpdateTray();
        AppSettings.Changed += key =>
        {
            if (key == AppSettings.KeyBackground) Dispatcher.UIThread.Post(UpdateTray);
        };

        // Самозамена при обновлении не удалась (файл был занят) — версия осталась прежней.
        if (Program.UpdateFailedReason is { } failedReason)
        {
            Log.Warning("Обновление не применилось ({Reason:l}) — версия прежняя: {Version:l}",
                        failedReason, AppSettings.AppVersion);
            main.ShowNotice(InstallService.UpdateFailedMessage(failedReason));
        }

        // Повторный запуск (второй экземпляр) просит показать окно.
        if (Program.Instance is { } instance)
        {
            instance.StartListening();
            instance.ShowRequested += () => Dispatcher.UIThread.Post(() =>
            {
                Log.Information("Повторный запуск: окно работающей программы показано");
                main.ShowAndActivate();
            });
        }

        desktop.Exit += (_, _) =>
        {
            // Файл единственного экземпляра — первым делом: если закрытие зависнет, повторный
            // запуск (и перезапуск обновлением) не должен молча выходить из-за занятого замка.
            Program.Instance?.Dispose();
            library.Dispose();
            _ = DocIndexService.DetachAsync();
            Program.NotificationManager?.Dispose();
            _tray?.Dispose();
            Log.Information("Завершение программы ({Reason:l}): работала {Uptime:l}",
                            Program.ExitReason ?? "закрытие окна",
                            Logging.Diag.Duration(DateTime.Now - Program.StartedAt));
        };

        library.StartWork();

        // Автообновление из папки — после показа окна и в фоне: сетевая папка может отвечать
        // долго, а старт ее ждать не должен.
        if (AppSettings.AutoUpdate && AppSettings.UpdateFolder.Length > 0)
            _ = UpdateService.CheckOnStartupAsync(main);
    }

    /// <summary>
    /// Значок у часов — пока включена «Работать в фоне при закрытом окне»: закрытие окна прячет
    /// программу туда, а выход — через меню значка.
    /// </summary>
    private void UpdateTray()
    {
        if (!AppSettings.Background)
        {
            _tray?.Dispose();
            _tray = null;
            TrayIcon.SetIcons(this, null);
            TrayShown = false;
            return;
        }
        if (_tray is not null) return;

        var open = new NativeMenuItem("Открыть Библиотеку PDF");
        open.Click += (_, _) => MainWindowOf()?.ShowAndActivate();
        var exit = new NativeMenuItem("Выход");
        exit.Click += (_, _) =>
        {
            Program.ExitReason = "выход из меню значка";
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };

        _tray = new TrayIcon
        {
            ToolTipText = AppSettings.DisplayName,
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PdfLibrary/Assets/Branding/icon.ico"))),
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } },
        };
        _tray.Clicked += (_, _) => MainWindowOf()?.ShowAndActivate();
        TrayIcon.SetIcons(this, [_tray]);
        TrayShown = true;
    }

    private MainWindow? MainWindowOf() =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
}

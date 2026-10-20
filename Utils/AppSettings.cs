using System.Globalization;
using System.Reflection;

namespace PdfLibrary.Utils;

/// <summary>
/// Хранилище настроек (ключ — значение). Реализуется поверх таблицы <c>settings</c> в
/// library.db (<c>LibraryStore</c>); интерфейс живет в Utils, чтобы не тянуть слой базы в
/// <see cref="AppSettings"/>.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Значение по ключу или null, если ключа нет.</summary>
    string? Get(string key);

    /// <summary>Записать или обновить значение.</summary>
    void Set(string key, string value);

    /// <summary>Удалить ключ — настройка возвращается к стандарту. Нет ключа — ничего не делает.</summary>
    void Remove(string key);
}

/// <summary>
/// Пути к файлам программы и типизированный доступ к настройкам.
///
/// Стандарт (значения по умолчанию) — константы в коде. library.db хранит ТОЛЬКО отличия от
/// стандарта: нет отличия или хранилища — возвращается стандарт. Поэтому без сохраненных настроек
/// программа работает по стандарту, а сброс настройки — удаление ее ключа (запись значения,
/// равного стандарту, ключ удаляет).
/// </summary>
public static class AppSettings
{
    // Технический идентификатор: имя папки данных, сборки, бандла, установщика.
    // НЕ МЕНЯТЬ — иначе программа станет искать данные в другой папке и «потеряет» библиотеку.
    public const string AppName     = "PdfLibrary";
    // Отображаемое имя — то, что видит человек: заголовки окон, заставка, «О программе».
    public const string DisplayName = "Библиотека PDF";

    // Версия — из самой сборки, а НЕ константой: скрипты выпуска правят <Version> в
    // PdfLibrary.csproj, и константа неизбежно отставала бы. Одно место правды — csproj.
    public static string AppVersion { get; } = ResolveVersion();

    /// <summary>Дата сборки — из метаданных сборки (<c>BuildDate</c> в csproj); null — не записана.</summary>
    public static DateTime? BuildDate { get; } = ResolveBuildDate();

    private static string ResolveVersion()
    {
        // InformationalVersion — это <Version> дословно; SDK может дописать «+<хеш коммита>»,
        // в журнале и «О программе» он лишний.
        var info = typeof(AppSettings).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return typeof(AppSettings).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static DateTime? ResolveBuildDate()
    {
        var raw = typeof(AppSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        return DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                      DateTimeStyles.AssumeUniversal, out var date)
            ? date.Date
            : null;
    }

    public const int WindowMinWidth  = 960;
    public const int WindowMinHeight = 600;

    // ── Пути ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Переменная окружения с папкой данных — для стендов и тестов: программа поднимается на
    /// подготовленной библиотеке и не трогает настоящую.
    /// </summary>
    public const string DataDirVariable = "PDFLIBRARY_DATA";

    public static string AppDataDir   { get; } = ResolveAppDataDir();
    /// <summary>База библиотеки: источники, группы, документы, метки, настройки.</summary>
    public static string LibraryDbPath { get; } = Path.Combine(AppDataDir, "library.db");
    /// <summary>Кеш прочитанного: текст для поиска, страницы, форматы листов (пересобираемый).</summary>
    public static string CachePath     { get; } = Path.Combine(AppDataDir, "library-cache.db");
    /// <summary>Миниатюры первых страниц — картинки на диске, а не в памяти: лента идет через тысячи документов.</summary>
    public static string ThumbsDir     { get; } = Path.Combine(AppDataDir, "thumbs");
    /// <summary>Журнал работы программы (Serilog → SQLite).</summary>
    public static string LogsDbPath    { get; } = Path.Combine(AppDataDir, "logs.db");

    public static void EnsureCreated() => Directory.CreateDirectory(AppDataDir);

    private static string ResolveAppDataDir()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirVariable);
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);

        // Release (установленная программа) — локальная папка данных пользователя:
        // %LocalAppData%\PdfLibrary на Windows, ~/Library/Application Support/PdfLibrary на macOS,
        // ~/.local/share/PdfLibrary на Linux. Локальная, а не перемещаемая: кеш текста весит
        // гигабайты и ездить за пользователем по сети не должен.
        // Debug — папка .localdata в корне проекта: изолирована «как venv», переживает снос
        // artifacts/ и не мешается с исходниками. Корень ищем по маркеру, а не счетом «..».
#if DEBUG
        var root = FindProjectRoot() ?? AppContext.BaseDirectory;
        return Path.Combine(root, ".localdata");
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
#endif
    }

#if DEBUG
    private static string? FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdfLibrary.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
#endif

    // ── Хранилище ─────────────────────────────────────────────────────────────

    // Подключается на старте после открытия library.db. До подключения чтения возвращают
    // стандарт, а записи ничего не делают.
    private static ISettingsStore? _store;

    /// <summary>Подключить хранилище (library.db). Зовется на старте после открытия базы.</summary>
    public static void AttachStore(ISettingsStore? store) => _store = store;

    /// <summary>Настройка изменилась — ключ. Приходит в потоке, который записал значение.</summary>
    public static event Action<string>? Changed;

    // ── Общее ─────────────────────────────────────────────────────────────────

    public static bool Autostart
    {
        get => GetBool(KeyAutostart, false);
        set => SetBool(KeyAutostart, value, false);
    }

    /// <summary>Работать в фоне при закрытом окне: закрытие прячет окно в значок у часов.</summary>
    public static bool Background
    {
        get => GetBool(KeyBackground, false);
        set => SetBool(KeyBackground, value, false);
    }

    /// <summary>Уведомление о новых документах в источниках.</summary>
    public static bool NotifyNew
    {
        get => GetBool(KeyNotifyNew, false);
        set => SetBool(KeyNotifyNew, value, false);
    }

    /// <summary>Уведомление о конце фонового чтения.</summary>
    public static bool NotifyRead
    {
        get => GetBool(KeyNotifyRead, false);
        set => SetBool(KeyNotifyRead, value, false);
    }

    /// <summary>Программа для кнопки «Открыть»: путь к ней; пусто — как в системе.</summary>
    public static string OpenWith
    {
        get => GetString(KeyOpenWith, "");
        set => SetString(KeyOpenWith, value, "");
    }

    /// <summary>Открывать при запуске последний документ — в панели чтения, на той же странице.</summary>
    public static bool ReopenLast
    {
        get => GetBool(KeyReopenLast, false);
        set => SetBool(KeyReopenLast, value, false);
    }

    /// <summary>Последний открытый в панели чтения документ и страница (с нуля).</summary>
    public static (string Path, int Page) LastDocument
    {
        get => (GetString(KeyLastDoc, ""), GetInt(KeyLastPage, 0, 0, int.MaxValue));
        set
        {
            SetString(KeyLastDoc, value.Path, "");
            SetInt(KeyLastPage, value.Page, 0);
        }
    }

    // ── Источники и чтение ────────────────────────────────────────────────────

    /// <summary>Следить за папками: новые и измененные PDF появляются в ленте сами.</summary>
    public static bool Watch
    {
        get => GetBool(KeyWatch, true);
        set => SetBool(KeyWatch, value, true);
    }

    /// <summary>Читать новые документы сразу — фоновое чтение без кнопки.</summary>
    public static bool ReadAuto
    {
        get => GetBool(KeyReadAuto, true);
        set => SetBool(KeyReadAuto, value, true);
    }

    /// <summary>Пункты «Не читать файлы тяжелее», МБ.</summary>
    public static readonly int[] MaxFileMbOptions = [200, 50, 1024];
    public const int MaxFileMbDefault = 200;

    /// <summary>Не читать файлы тяжелее, МБ: такие остаются в ленте и ищутся по названию.</summary>
    public static int MaxFileMb
    {
        get
        {
            var mb = GetInt(KeyMaxFileMb, MaxFileMbDefault, 1, 1_000_000);
            return Array.IndexOf(MaxFileMbOptions, mb) >= 0 ? mb : MaxFileMbDefault;
        }
        set => SetInt(KeyMaxFileMb, value, MaxFileMbDefault);
    }

    public const string SkipMasksDefault = "~$*.pdf";

    /// <summary>Маски файлов, которые источник пропускает, через запятую.</summary>
    public static string SkipMasks
    {
        get => GetString(KeySkipMasks, SkipMasksDefault);
        set => SetString(KeySkipMasks, value.Trim(), SkipMasksDefault);
    }

    // ── Лента и карточки ──────────────────────────────────────────────────────

    public static CardSize CardSize
    {
        get => GetString(KeyCardSize, "medium") switch
        {
            "small" => CardSize.Small,
            "large" => CardSize.Large,
            _       => CardSize.Medium,
        };
        set => SetString(KeyCardSize, value switch
        {
            CardSize.Small => "small",
            CardSize.Large => "large",
            _              => "medium",
        }, "medium");
    }

    /// <summary>Миниатюра первой страницы на плитке; выключено — только формат и страницы.</summary>
    public static bool CardThumbnail
    {
        get => GetBool(KeyCardThumb, true);
        set => SetBool(KeyCardThumb, value, true);
    }

    /// <summary>Строка «источник · папка · файл» под названием.</summary>
    public static bool CardPath
    {
        get => GetBool(KeyCardPath, true);
        set => SetBool(KeyCardPath, value, true);
    }

    /// <summary>«3,1 МБ · 48 стр. · A4» справа внизу.</summary>
    public static bool CardMeta
    {
        get => GetBool(KeyCardMeta, true);
        set => SetBool(KeyCardMeta, value, true);
    }

    /// <summary>Голубая метка типа документа.</summary>
    public static bool CardType
    {
        get => GetBool(KeyCardType, true);
        set => SetBool(KeyCardType, value, true);
    }

    /// <summary>Показывать документы, которых нет на диске («файл не найден»).</summary>
    public static bool FeedMissing
    {
        get => GetBool(KeyFeedMissing, true);
        set => SetBool(KeyFeedMissing, value, true);
    }

    /// <summary>Показывать сканы без текстового слоя.</summary>
    public static bool FeedScans
    {
        get => GetBool(KeyFeedScans, true);
        set => SetBool(KeyFeedScans, value, true);
    }

    /// <summary>Порядок ленты: rel, mod, az, za, size.</summary>
    public static string Sort
    {
        get => GetString(KeySort, "rel");
        set => SetString(KeySort, value, "rel");
    }

    // ── Оформление ────────────────────────────────────────────────────────────

    public static AppTheme Theme
    {
        get => GetString(KeyTheme, "light") switch
        {
            "dark" => AppTheme.Dark,
            "auto" => AppTheme.Auto,
            _      => AppTheme.Light,
        };
        set => SetString(KeyTheme, value switch
        {
            AppTheme.Dark => "dark",
            AppTheme.Auto => "auto",
            _             => "light",
        }, "light");
    }

    /// <summary>Пункты «Масштаб интерфейса».</summary>
    public static readonly double[] ScaleOptions = [0.9, 1.0, 1.1, 1.25];
    public const double ScaleDefault = 1.0;

    public static double Scale
    {
        get
        {
            var raw = _store?.Get(KeyScale);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                   && ScaleOptions.Any(o => Math.Abs(o - v) < 0.001)
                ? v
                : ScaleDefault;
        }
        set
        {
            if (Math.Abs(value - ScaleDefault) < 0.001) Remove(KeyScale);
            else Put(KeyScale, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Пункты «Размер шрифта»: прибавка к стандартным размерам в пикселях.</summary>
    public static readonly int[] FontDeltaOptions = [-1, 0, 2];

    public static int FontDelta
    {
        get
        {
            var d = GetInt(KeyFontDelta, 0, -10, 10);
            return Array.IndexOf(FontDeltaOptions, d) >= 0 ? d : 0;
        }
        set => SetInt(KeyFontDelta, value, 0);
    }

    // ── О программе ───────────────────────────────────────────────────────────

    /// <summary>Папка обновлений — откуда брать новые версии; пусто — не задана.</summary>
    public static string UpdateFolder
    {
        get => GetString(KeyUpdateFolder, "");
        set => SetString(KeyUpdateFolder, value.Trim(), "");
    }

    /// <summary>Автообновление: новая версия ставится сама при следующем запуске.</summary>
    public static bool AutoUpdate
    {
        get => GetBool(KeyAutoUpdate, true);
        set => SetBool(KeyAutoUpdate, value, true);
    }

    // ── Окно ──────────────────────────────────────────────────────────────────

    /// <summary>Боковая панель источников и меток скрыта.</summary>
    public static bool SidebarHidden
    {
        get => GetBool(KeySidebarHidden, false);
        set => SetBool(KeySidebarHidden, value, false);
    }

    // ── Ключи ─────────────────────────────────────────────────────────────────

    internal const string KeyAutostart     = "autostart";
    internal const string KeyBackground    = "background";
    internal const string KeyNotifyNew     = "notify.new";
    internal const string KeyNotifyRead    = "notify.read";
    internal const string KeyOpenWith      = "open.with";
    internal const string KeyReopenLast    = "open.last";
    internal const string KeyLastDoc       = "last.doc";
    internal const string KeyLastPage      = "last.page";
    internal const string KeyWatch         = "sources.watch";
    internal const string KeyReadAuto      = "read.auto";
    internal const string KeyMaxFileMb     = "read.maxMb";
    internal const string KeySkipMasks     = "sources.skip";
    internal const string KeyCardSize      = "card.size";
    internal const string KeyCardThumb     = "card.thumb";
    internal const string KeyCardPath      = "card.path";
    internal const string KeyCardMeta      = "card.meta";
    internal const string KeyCardType      = "card.type";
    internal const string KeyFeedMissing   = "feed.missing";
    internal const string KeyFeedScans     = "feed.scans";
    internal const string KeySort          = "feed.sort";
    internal const string KeyTheme         = "theme";
    internal const string KeyScale         = "scale";
    internal const string KeyFontDelta     = "font.delta";
    internal const string KeyUpdateFolder  = "update.folder";
    internal const string KeyAutoUpdate    = "update.auto";
    internal const string KeySidebarHidden = "sidebar.hidden";

    // ── Чтение и запись ───────────────────────────────────────────────────────

    private static bool GetBool(string key, bool fallback) =>
        bool.TryParse(_store?.Get(key), out var v) ? v : fallback;

    private static void SetBool(string key, bool value, bool fallback)
    {
        if (value == fallback) Remove(key);
        else Put(key, value ? "true" : "false");
    }

    private static int GetInt(string key, int fallback, int min, int max) =>
        int.TryParse(_store?.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        && v >= min && v <= max
            ? v
            : fallback;

    private static void SetInt(string key, int value, int fallback)
    {
        if (value == fallback) Remove(key);
        else Put(key, value.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetString(string key, string fallback) => _store?.Get(key) ?? fallback;

    private static void SetString(string key, string value, string fallback)
    {
        if (value == fallback) Remove(key);
        else Put(key, value);
    }

    private static void Put(string key, string value)
    {
        if (_store is null || _store.Get(key) == value) return;
        _store.Set(key, value);
        Changed?.Invoke(key);
    }

    private static void Remove(string key)
    {
        if (_store?.Get(key) is null) return;
        _store.Remove(key);
        Changed?.Invoke(key);
    }
}

/// <summary>Размер карточки ленты: высота, миниатюра, сколько строк найденного текста видно.</summary>
public enum CardSize { Small, Medium, Large }

/// <summary>Тема оформления.</summary>
public enum AppTheme { Light, Dark, Auto }

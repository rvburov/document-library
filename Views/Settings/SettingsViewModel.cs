using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Settings;

/// <summary>
/// Значения настроек для экранов «Настройки». Каждое свойство пишет свою настройку сразу —
/// кнопки «Сохранить» нет, как в макете: переключил — действует.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private bool _loading;

    public SettingsViewModel() => Load();

    /// <summary>Перечитать значения — экран открыли заново.</summary>
    public void Load()
    {
        _loading = true;
        Autostart     = Utils.Autostart.IsSupported && AppSettings.Autostart;
        Background    = AppSettings.Background;
        NotifyNew     = AppSettings.NotifyNew;
        NotifyRead    = AppSettings.NotifyRead;
        ReopenLast    = AppSettings.ReopenLast;
        Watch         = AppSettings.Watch;
        ReadAuto      = AppSettings.ReadAuto;
        SkipMasks     = AppSettings.SkipMasks;
        CardThumbnail = AppSettings.CardThumbnail;
        CardPath      = AppSettings.CardPath;
        CardMeta      = AppSettings.CardMeta;
        CardType      = AppSettings.CardType;
        FeedMissing   = AppSettings.FeedMissing;
        FeedScans     = AppSettings.FeedScans;
        UpdateFolder  = AppSettings.UpdateFolder;
        AutoUpdate    = AppSettings.AutoUpdate;
        _loading = false;
    }

    // ── Общее ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _background;
    [ObservableProperty] private bool _notifyNew;
    [ObservableProperty] private bool _notifyRead;
    [ObservableProperty] private bool _reopenLast;

    /// <summary>Автозапуск доступен только установленной копии программы.</summary>
    public bool AutostartSupported => Utils.Autostart.IsSupported;

    public string AutostartDescription => AutostartSupported
        ? "Программа стартует сама и сразу проверяет папки-источники. Доступно только установленной копии программы."
        : "Программа стартует сама и сразу проверяет папки-источники. " + Utils.Autostart.UnsupportedReason();

    partial void OnAutostartChanged(bool value)
    {
        if (_loading) return;
        if (!Utils.Autostart.Set(value))
        {
            // Система не приняла запись — переключатель возвращается к тому, что в системе есть.
            _loading = true;
            Autostart = Utils.Autostart.IsEnabled();
            _loading = false;
            return;
        }
        AppSettings.Autostart = value;
    }

    partial void OnBackgroundChanged(bool value) { if (!_loading) AppSettings.Background = value; }
    partial void OnNotifyNewChanged(bool value) { if (!_loading) AppSettings.NotifyNew = value; }
    partial void OnNotifyReadChanged(bool value) { if (!_loading) AppSettings.NotifyRead = value; }
    partial void OnReopenLastChanged(bool value) { if (!_loading) AppSettings.ReopenLast = value; }

    // ── Источники и чтение ────────────────────────────────────────────────────

    [ObservableProperty] private bool _watch;
    [ObservableProperty] private bool _readAuto;
    [ObservableProperty] private string _skipMasks = "";

    partial void OnWatchChanged(bool value) { if (!_loading) AppSettings.Watch = value; }
    partial void OnReadAutoChanged(bool value) { if (!_loading) AppSettings.ReadAuto = value; }

    /// <summary>Записать маски «Пропускать файлы» (по уходу из поля).</summary>
    public void SaveMasks()
    {
        var normalized = string.Join(", ", Core.Library.SourceScanner.ParseMasks(SkipMasks));
        if (normalized == AppSettings.SkipMasks) return;
        AppSettings.SkipMasks = normalized;
        _loading = true;
        SkipMasks = normalized;
        _loading = false;
    }

    /// <summary>Подписи пунктов «Не читать файлы тяжелее».</summary>
    public static string MaxFileText(int mb) => mb >= 1024 ? $"{mb / 1024} ГБ" : $"{mb} МБ";

    public void SetMaxFileMb(int mb)
    {
        AppSettings.MaxFileMb = mb;
        if (DocIndexService.Current is { } service) service.MaxFileMb = mb;
    }

    // ── Лента и карточки ──────────────────────────────────────────────────────

    [ObservableProperty] private bool _cardThumbnail;
    [ObservableProperty] private bool _cardPath;
    [ObservableProperty] private bool _cardMeta;
    [ObservableProperty] private bool _cardType;
    [ObservableProperty] private bool _feedMissing;
    [ObservableProperty] private bool _feedScans;

    partial void OnCardThumbnailChanged(bool value) { if (!_loading) AppSettings.CardThumbnail = value; }
    partial void OnCardPathChanged(bool value) { if (!_loading) AppSettings.CardPath = value; }
    partial void OnCardMetaChanged(bool value) { if (!_loading) AppSettings.CardMeta = value; }
    partial void OnCardTypeChanged(bool value) { if (!_loading) AppSettings.CardType = value; }
    partial void OnFeedMissingChanged(bool value) { if (!_loading) AppSettings.FeedMissing = value; }
    partial void OnFeedScansChanged(bool value) { if (!_loading) AppSettings.FeedScans = value; }

    // ── О программе ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _updateFolder = "";
    [ObservableProperty] private bool _autoUpdate;

    partial void OnAutoUpdateChanged(bool value) { if (!_loading) AppSettings.AutoUpdate = value; }

    public void SaveUpdateFolder() => AppSettings.UpdateFolder = UpdateFolder;
}

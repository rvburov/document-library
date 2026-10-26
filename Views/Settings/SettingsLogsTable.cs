using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PdfLibrary.Controls.Components;
using PdfLibrary.Logging;
using PdfLibrary.Utils;
using Path = Avalonia.Controls.Shapes.Path;

namespace PdfLibrary.Views.Settings;

/// <summary>Строка таблицы диагностики; исходная запись нужна для курсора и полного текста.</summary>
public sealed record LogRowView(LogReader.LogRow Row)
{
    public string Time => LogLabels.FormatTs(Row.Timestamp);
    public string Level => LogLabels.LevelLabel(Row.Level);
    public string Message => Row.Message;
    public string Detail => Row.Exception ?? "";
    public string ExceptionSummary => Detail.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
}

public sealed record LogDetailField(string Key, string Name, string Value);

public partial class SettingsView
{
    private const int LogsPageSize = 200;
    private static readonly (string Key, string Header, string Icon, string Property)[] LogsColumns =
    [
        ("date", "Время", "IconClock", nameof(LogRowView.Time)),
        ("level", "Уровень", "IconFieldStatus", nameof(LogRowView.Level)),
        ("message", "Сообщение", "IconMessageWarning", nameof(LogRowView.Message)),
        ("exception", "Ошибка", "IconTriangleAlert", nameof(LogRowView.ExceptionSummary)),
    ];

    private readonly ObservableCollection<LogRowView> _logsRows = [];
    private readonly Dictionary<string, HashSet<string>> _logsFilters = [];
    private readonly Dictionary<string, Path> _logsHeaderGlyphs = [];
    private string? _logsSortKey;
    private bool _logsSortAsc;
    private bool _logsLoading;
    private bool _logsEnd;
    private int _logsGeneration;
    private double _logDetailWidth = 440;
    private string _logDetailKey = "message";
    internal Flyout? LogFilterPopup { get; private set; }

    private void BuildLogsTable()
    {
        if (LogsGrid.Columns.Count > 0) return;
        LogsGrid.ItemsSource = _logsRows;
        foreach (var def in LogsColumns)
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), HorizontalAlignment = HorizontalAlignment.Stretch };
            var icon = LogGlyph(def.Icon);
            icon.Margin = new Thickness(8, 0, 0, 0);
            header.Children.Add(icon);
            var title = new TextBlock { Text = def.Header, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            var arrow = LogGlyph("IconChevronDown");
            _logsHeaderGlyphs[def.Key] = arrow;
            var filter = new Button { Content = arrow, Width = 26, Height = 30, Padding = new Thickness(5), Margin = new Thickness(0, 0, 4, 0) };
            filter.Classes.Add("icon");
            filter.Name = "LogFilter_" + def.Key;
            ToolTip.SetTip(filter, "Фильтр и сортировка: " + def.Header);
            filter.Click += (_, _) => ShowLogsFilter(filter, def.Key);
            Grid.SetColumn(filter, 2);
            header.Children.Add(filter);
            LogsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header, Binding = new Binding(def.Property),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 110,
            });
        }
        LogsGrid.SelectionChanged += (_, _) =>
        {
            if (LogsGrid.SelectedItem is LogRowView row) OpenLogDetail(row);
        };
        LogsGrid.LoadingRow += (_, e) =>
        {
            if (e.Row.Index >= _logsRows.Count - 15) _ = LoadLogAsync(reset: false);
        };
    }

    private static Path LogGlyph(string key)
    {
        var glyph = new Path
        {
            Data = Res.Icon(key), Width = 16, Height = 16, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, glyph.GetResourceObservable("BrushSecondaryText"));
        return glyph;
    }

    private LogReader.LogQuery LogsQuery(LogReader.LogRow? after = null) => new()
    {
        Filters = _logsFilters.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)kv.Value.ToArray()),
        SortColumn = _logsSortKey, SortAsc = _logsSortAsc, AfterId = after?.Id,
        AfterValue = after is null || _logsSortKey is null ? null : LogReader.SortValue(_logsSortKey, after),
    };

    private async Task LoadLogAsync(bool reset)
    {
        if (reset)
        {
            _logsGeneration++;
            CloseLogDetail();
            _logsRows.Clear();
            _logsEnd = false;
            NoLogs.IsVisible = false;
        }
        if (_logsLoading || _logsEnd) return;
        _logsLoading = true;
        var generation = _logsGeneration;
        var query = LogsQuery(_logsRows.LastOrDefault()?.Row);
        try
        {
            var reader = new LogReader(AppSettings.LogsDbPath);
            var page = await reader.GetPageAsync(query, LogsPageSize);
            if (generation != _logsGeneration) return;
            var first = _logsRows.Count == 0;
            foreach (var row in page) _logsRows.Add(new LogRowView(row));
            _logsEnd = page.Count < LogsPageSize;
            NoLogs.Text = _logsFilters.Count > 0 ? "Нет записей, подходящих под фильтры." : "Записей пока нет.";
            NoLogs.IsVisible = _logsRows.Count == 0;
            if (first)
            {
                var count = await reader.CountAsync(query);
                if (generation != _logsGeneration) return;
                LogCount.Text = $"{count:N0} {Plural.Of((int)Math.Min(count, int.MaxValue), "запись", "записи", "записей")}";
            }
            long bytes;
            try { bytes = File.Exists(AppSettings.LogsDbPath) ? new FileInfo(AppSettings.LogsDbPath).Length : 0; }
            catch { bytes = 0; }
            LogNote.Text = $"Журнал хранится в logs.db ({Sizes.Text(bytes)}). Записи старше 30 дней удаляются сами.";
        }
        catch (Exception ex)
        {
            if (generation != _logsGeneration) return;
            _logsEnd = true;
            LogNote.Text = $"Не удалось прочитать журнал: {ex.Message}";
            Serilog.Log.Warning(ex, "Таблица диагностики не прочиталась");
        }
        finally
        {
            _logsLoading = false;
            // Смена фильтра во время чтения не должна оставить новый запрос без первой страницы.
            if (generation != _logsGeneration) _ = LoadLogAsync(reset: false);
        }
    }

    private void ShowLogsFilter(Control anchor, string key)
    {
        var filters = LogsQuery().Filters;
        LogFilterPopup?.Hide();
        LogFilterPopup = ColumnFilterPanel.Show(this, anchor, new ColumnFilterSpec
        {
            Key = key,
            AscLabel = key == "date" ? "Сначала старые" : "От А до Я",
            DescLabel = key == "date" ? "Сначала новые" : "От Я до А",
            Sort = _logsSortKey == key ? _logsSortAsc : null,
            Filter = _logsFilters.GetValueOrDefault(key),
            Display = raw => LogFilterLabel(key, raw),
            LoadValues = () => new LogReader(AppSettings.LogsDbPath).GetCountsAsync(key, filters),
            LoadFailed = ex => Serilog.Log.Warning(ex, "Значения фильтра диагностики не прочитались: {Column:l}", key),
            Apply = result =>
            {
                if (result.FilterKnown)
                {
                    if (result.Filter is null) _logsFilters.Remove(key);
                    else _logsFilters[key] = new HashSet<string>(result.Filter);
                }
                if (result.Sort is { } asc) { _logsSortKey = key; _logsSortAsc = asc; }
                else if (_logsSortKey == key) _logsSortKey = null;
                RefreshLogsHeaders();
                _ = LoadLogAsync(reset: true);
            },
        });
    }

    private static string LogFilterLabel(string key, string raw)
    {
        if (raw.Length == 0) return "(Пусто)";
        if (key == "level") return LogLabels.LevelLabel(raw);
        if (key == "date" && DateTime.TryParseExact(raw, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
            return date.ToString("dd.MM.yyyy");
        if (key == "exception") return raw.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? raw;
        return raw;
    }

    private void RefreshLogsHeaders()
    {
        foreach (var (key, glyph) in _logsHeaderGlyphs)
        {
            var active = _logsFilters.ContainsKey(key) || _logsSortKey == key;
            glyph.Data = Res.Icon(active ? "IconFilter" : "IconChevronDown");
            glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty,
                       glyph.GetResourceObservable(active ? "BrushAccent" : "BrushSecondaryText"));
        }
    }

    private void OpenLogDetail(LogRowView row)
    {
        if (!LogDetailPanel.IsVisible)
        {
            SettingsLayout.ColumnDefinitions[2].MinWidth = 320;
            SettingsLayout.ColumnDefinitions[2].MaxWidth = 900;
            SettingsLayout.ColumnDefinitions[2].Width = new GridLength(_logDetailWidth);
        }
        LogDetailPanel.IsVisible = LogDetailSplitter.IsVisible = true;
        var fields = new[]
        {
            new LogDetailField("date", "Время", row.Time), new LogDetailField("level", "Уровень", row.Level),
            new LogDetailField("message", "Сообщение", row.Message), new LogDetailField("exception", "Ошибка", row.Detail),
        };
        LogDetailFields.ItemsSource = fields;
        LogDetailFields.SelectedItem = fields.First(f => f.Key == _logDetailKey);
    }

    private void CloseLogDetail()
    {
        if (LogDetailPanel.IsVisible) _logDetailWidth = Math.Clamp(SettingsLayout.ColumnDefinitions[2].ActualWidth, 320, 900);
        LogDetailPanel.IsVisible = LogDetailSplitter.IsVisible = false;
        SettingsLayout.ColumnDefinitions[2].MinWidth = 0;
        SettingsLayout.ColumnDefinitions[2].Width = new GridLength(0);
        LogsGrid.SelectedItem = null;
        LogDetailFields.ItemsSource = null;
        LogDetailText.Text = "";
    }

    private void OnCloseLogDetail(object? sender, RoutedEventArgs e) => CloseLogDetail();

    private void OnLogDetailFieldChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LogDetailFields.SelectedItem is not LogDetailField field) return;
        _logDetailKey = field.Key;
        LogDetailTitle.Text = field.Name;
        LogDetailText.Text = field.Value;
    }

    private async void OnCopyLogDetail(object? sender, RoutedEventArgs e)
    {
        if (LogDetailText.Text is { Length: > 0 } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}

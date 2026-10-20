using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PdfLibrary.Services;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Welcome;

/// <summary>
/// Первый запуск: библиотека пуста. Папка добавляется сразу, без окна «Добавить источник»:
/// название — имя папки, группы еще нет. Путь можно вставить из буфера (Ctrl+V).
/// </summary>
public partial class WelcomeView : UserControl
{
    private LibraryService? _library;

    public WelcomeView()
    {
        InitializeComponent();
        KeyDown += OnKey;
    }

    /// <summary>«Открыть библиотеку» — перейти в ленту.</summary>
    public event Action? OpenLibraryRequested;

    public void Attach(LibraryService library)
    {
        _library = library;
        library.Changed += _ => Update();
        library.ReadingChanged += _ => Update();
    }

    /// <summary>Добавить папку (перетащили на окно или вставили путь).</summary>
    public async void AddFolder(string path)
    {
        if (_library is null) return;
        ErrorText.IsVisible = false;
        var exists = await Task.Run(() =>
        {
            try { return Directory.Exists(path); } catch { return false; }
        });
        if (!exists)
        {
            ErrorText.Text = "Папка не найдена или недоступна: " + path;
            ErrorText.IsVisible = true;
            return;
        }
        _library.AddSource(path, "", null);
        Update();
    }

    private async void OnPick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Папка с PDF" });
        foreach (var folder in folders)
            if (folder.TryGetLocalPath() is { } path) AddFolder(path);
    }

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!command || e.Key != Key.V || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = (await clipboard.GetTextAsync())?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(text)) AddFolder(text);
    }

    private void OnOpenLibrary(object? sender, RoutedEventArgs e) => OpenLibraryRequested?.Invoke();

    private void Update()
    {
        if (_library is null) return;
        // Добавленное — все источники библиотеки: экран первого запуска показывается, пока
        // человек не нажал «Открыть библиотеку», и папку могли перетащить и на окно.
        var sources = _library.Sources;
        StartBlock.IsVisible = sources.Count == 0;
        AddedBlock.IsVisible = sources.Count > 0;
        if (sources.Count == 0) return;

        AddedList.ItemsSource = sources.Select(source =>
        {
            var id = source.Id;
            if (source.FirstScan) return $"{source.Name} — {source.Path} · ищу PDF…";
            var docs = _library.Documents.Where(d => d.SourceId == id && !d.Missing).ToList();
            var folders = docs.Select(d => d.Folder).Distinct().Count();
            return $"{source.Name} — {source.Path} · найдено {docs.Count:N0} PDF в {folders} " +
                   Plural.Of(folders, "папке", "папках", "папках");
        }).ToList();

        var reading = _library.Reading;
        ReadBar.IsVisible = reading.Running;
        ReadBar.Value = reading.Fraction;
        ReadText.Text = reading.Running
            ? $"Читаю текст документов: {reading.Done:N0} из {reading.Total:N0}" +
              (reading.Left is { } left ? " · " + EtaEstimator.Format(left.TotalSeconds) : "")
            : _library.Checking ? "Ищу PDF в папках…" : "Текст документов прочитан.";
    }
}

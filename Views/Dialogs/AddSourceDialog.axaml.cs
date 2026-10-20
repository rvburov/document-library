using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PdfLibrary.Core.Library;
using PdfLibrary.Services;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Что выбрано в «Добавить источник».</summary>
public sealed record NewSource(string Path, string Name, int? GroupId);

/// <summary>
/// «Добавить источник»: путь (вставить или «Обзор…»), название — стандартно имя папки, группа.
/// Папка проверяется на диске перед добавлением: источник с опечаткой в пути показал бы пустую
/// ленту без объяснений.
/// </summary>
public partial class AddSourceDialog : DialogBase
{
    private int? _group;
    private readonly IReadOnlyList<GroupItem> _groups;

    public AddSourceDialog() : this([], null) { }

    public AddSourceDialog(IReadOnlyList<GroupItem> groups, string? path)
    {
        InitializeComponent();
        _groups = groups;
        GroupBlock.IsVisible = groups.Count > 0;
        BuildPills();
        if (path is not null) PathBox.Text = path;
    }

    public override string Title => "Добавить источник";

    public override void OnShown() => PathBox.Focus();

    private void BuildPills()
    {
        var pills = new List<Button>();
        foreach (var (id, name) in new[] { ((int?)null, "Без группы") }.Concat(_groups.Select(g => ((int?)g.Id, g.Name))))
        {
            var pill = new Button { Content = name, Margin = new Avalonia.Thickness(0, 0, 6, 6) };
            pill.Theme = Res.Theme("PillButton");
            pill.Classes.Set("on", id == _group);
            pill.Click += (_, _) => { _group = id; BuildPills(); };
            pills.Add(pill);
        }
        GroupPills.ItemsSource = pills;
    }

    private void OnPathChanged(object? sender, TextChangedEventArgs e)
    {
        var path = (PathBox.Text ?? "").Trim();
        AddButton.IsEnabled = path.Length > 0;
        NameBox.Watermark = path.Length > 0 ? LibraryService.FolderName(path) : "Возьмется из имени папки";
        ErrorText.IsVisible = false;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка с PDF", AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } local) PathBox.Text = local;
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        var path = (PathBox.Text ?? "").Trim().Trim('"');
        if (path.Length == 0) return;
        AddButton.IsEnabled = false;
        // Сетевая папка может отвечать секундами — проверяем в фоне.
        var exists = await Task.Run(() =>
        {
            try { return Directory.Exists(path); } catch { return false; }
        });
        AddButton.IsEnabled = true;
        if (!exists)
        {
            ErrorText.Text = "Папка не найдена или недоступна — проверьте путь и подключение к сети.";
            ErrorText.IsVisible = true;
            return;
        }
        Close(new NewSource(path, (NameBox.Text ?? "").Trim(), _group));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

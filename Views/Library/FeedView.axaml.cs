using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PdfLibrary.Services;
using PdfLibrary.Utils;
using PdfLibrary.Views.Dialogs;

namespace PdfLibrary.Views.Library;

/// <summary>Экран ленты: разметка в FeedView.axaml, здесь — реакция на нажатия.</summary>
public partial class FeedView : UserControl
{
    private bool _sortSync;

    public FeedView()
    {
        InitializeComponent();
        SortBox.ItemsSource = Core.Library.FeedSort.Options.Select(o => o.Label).ToList();
        DataContextChanged += (_, _) => SyncSort();
    }

    private LibraryViewModel? Vm => DataContext as LibraryViewModel;
    private LibraryService? Library => Vm?.Library;

    /// <summary>Открыть документ в панели чтения (null — первый в ленте или последний открытый).</summary>
    public event Action<CardViewModel?>? ReadRequested;

    /// <summary>Скрыть или показать боковую панель.</summary>
    public void SetSidebarVisible(bool visible)
    {
        Sidebar.IsVisible = visible;
        Layout.ColumnDefinitions[0].Width = visible ? new GridLength(232) : new GridLength(0);
    }

    /// <summary>Поставить курсор в строку поиска (Ctrl+F).</summary>
    public void FocusSearch()
    {
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    private void SyncSort()
    {
        if (Vm is null) return;
        _sortSync = true;
        SortBox.SelectedIndex = Math.Max(0, Array.FindIndex(Core.Library.FeedSort.Options, o => o.Key == Vm.Sort));
        _sortSync = false;
    }

    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_sortSync || Vm is null || SortBox.SelectedIndex < 0) return;
        Vm.Sort = Core.Library.FeedSort.Options[SortBox.SelectedIndex].Key;
    }

    // ── Боковая панель ────────────────────────────────────────────────────────

    private async void OnNewSource(object? sender, RoutedEventArgs e)
    {
        AddButton.Flyout?.Hide();
        await AddSourceAsync(null);
    }

    /// <summary>«Добавить источник»; <paramref name="path"/> — уже выбранная папка (перетащили).</summary>
    public async Task AddSourceAsync(string? path)
    {
        if (Library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new AddSourceDialog(library.Groups, path)) is not NewSource result) return;
        var before = library.Sources.Count;
        var source = library.AddSource(result.Path, result.Name, result.GroupId);
        if (source is not null && library.Sources.Count == before)
            MainWindow.NoticeOf(this, $"Эта папка уже в библиотеке — источник «{source.Name}».");
    }

    private async void OnNewGroup(object? sender, RoutedEventArgs e)
    {
        AddButton.Flyout?.Hide();
        if (Library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new NewGroupDialog(library.Sources, library.Groups)) is NewGroup result)
            library.AddGroup(result.Name, result.SourceIds);
    }

    private async void OnNewTag(object? sender, RoutedEventArgs e)
    {
        AddButton.Flyout?.Hide();
        await NewTagAsync();
    }

    private async void OnNewTagFromMenu(object? sender, RoutedEventArgs e)
    {
        TagMenuButton.Flyout?.Hide();
        await NewTagAsync();
    }

    private async Task NewTagAsync()
    {
        if (Vm is not { } vm || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new NewTagDialog(vm.SelectedCount)) is not NewTag result) return;
        var tag = vm.Library.AddTag(result.Name, result.Color);
        if (result.ApplyToSelected) vm.SetTagOnSelected(tag.Id, true);
    }

    private void OnToggleGroups(object? sender, RoutedEventArgs e) => Vm?.ToggleAllGroups();

    private void OnCheckFolders(object? sender, RoutedEventArgs e) => Library?.CheckAll();

    private void OnFlipGroup(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SourceRow row) Vm?.FlipGroup(row);
    }

    private void OnToggleRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SourceRow row) Vm?.ToggleRow(row);
    }

    private void OnPickTag(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TagRow row) Vm?.PickTag(row);
    }

    private void OnSourceContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SourceRow { IsSource: true } row || Library is not { } library) return;
        var menu = SourceMenu.Build(this, library, row.Id);
        menu.Open(sender as Control);
        e.Handled = true;
    }

    // ── Тип документа ─────────────────────────────────────────────────────────

    private void OnTypeClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TypeRow row) Vm?.SetType(row, row.IsOn);
    }

    private void OnCloseTypes(object? sender, RoutedEventArgs e) => TypeButton.Flyout?.Hide();

    // ── Поиск ─────────────────────────────────────────────────────────────────

    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Vm?.ApplySearch(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Vm?.ClearSearch(); e.Handled = true; }
    }

    private void OnFind(object? sender, RoutedEventArgs e) => Vm?.ApplySearch();

    private void OnClearSearch(object? sender, RoutedEventArgs e) => Vm?.ClearSearch();

    private void OnStatusAction(object? sender, RoutedEventArgs e) => Vm?.RunStatusAction();

    // ── Карточки ──────────────────────────────────────────────────────────────

    private void OnCardPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is CardViewModel card) card.EnsureThumbnail();
        e.Container.DataContextChanged -= OnContainerDataChanged;
        e.Container.DataContextChanged += OnContainerDataChanged;
    }

    private static void OnContainerDataChanged(object? sender, EventArgs e)
    {
        if ((sender as Control)?.DataContext is CardViewModel card) card.EnsureThumbnail();
    }

    private void OnOpenReader(object? sender, RoutedEventArgs e) => ReadRequested?.Invoke(null);

    /// <summary>Карточка просит открыть ее документ в панели чтения.</summary>
    public void RequestRead(CardViewModel card) => ReadRequested?.Invoke(card);

    // ── Выбранные документы ───────────────────────────────────────────────────

    private void OnTagMenuOpening(object? sender, EventArgs e)
    {
        if (Vm is not { } vm) return;
        var picks = vm.TagPicks();
        TagPickList.ItemsSource = picks;
        TagMenuTitle.Text = vm.TagMenuTitle;
        NoTagsText.IsVisible = picks.Count == 0;
    }

    private void OnTagPickClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TagPick pick) Vm?.SetTagOnSelected(pick.Id, pick.IsOn);
    }

    private void OnCloseTags(object? sender, RoutedEventArgs e) => TagMenuButton.Flyout?.Hide();

    private void OnClearSelection(object? sender, RoutedEventArgs e) => Vm?.ClearSelection();

    private async void OnCopySelected(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = vm.Selected.Where(d => !d.Missing).Select(d => d.FullPath).ToList();
        if (files.Count == 0)
        {
            MainWindow.NoticeOf(this, "Выбранных файлов нет на диске — копировать нечего.", isError: true);
            return;
        }
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Куда скопировать документы", AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;

        MainWindow.NoticeOf(this, $"Копирую {files.Count} {Plural.Documents(files.Count)}…");
        var (copied, errors) = await Task.Run(() => DocActions.CopyTo(files, folder));
        MainWindow.NoticeOf(this, errors.Count == 0
            ? $"Скопировано {copied} {Plural.Documents(copied)} в {folder}"
            : $"Скопировано {copied} из {files.Count}. Не вышло: {errors[0]}", isError: errors.Count > 0);
    }
}

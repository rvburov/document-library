using Avalonia.Controls;
using PdfLibrary.Services;
using PdfLibrary.Views.Dialogs;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Раздел «Лента»: навбар, блок поиска и лента карточек.
/// Боковая панель источников и панель чтения живут в колонках главного окна, а сюда приходят
/// событиями: окно раскладывает колонки, раздел отвечает за ленту.
/// </summary>
public partial class LibraryView : UserControl
{
    /// <summary>«Прочитать» в карточке, двойной щелчок по ней или Enter — открыть панель чтения.</summary>
    public event Action<CardViewModel>? ReadRequested;

    /// <summary>Нажали toggle панели чтения в навбаре: true — открыть, false — закрыть.</summary>
    public event Action<bool>? ReaderToggled;

    private LibraryViewModel? _vm;

    public LibraryView()
    {
        InitializeComponent();
        NavbarControl.ReaderToggled += (_, open) => ReaderToggled?.Invoke(open);
        DocList.DocReadRequested += (_, card) => ReadRequested?.Invoke(card);
        DocList.DocActivated += (_, card) =>
        {
            if (DocActions.OpenExternal(card.Doc.FullPath) is { } error) MainWindow.NoticeOf(this, error, isError: true);
        };
    }

    public LibraryViewModel? Vm => _vm;

    public void Attach(LibraryViewModel vm)
    {
        _vm = vm;
        NavbarControl.Attach(vm);
        DocList.Attach(vm);
    }

    /// <summary>Открыть документ в панели чтения — то же, что «Прочитать» на его карточке.</summary>
    public void RequestRead(CardViewModel card) => ReadRequested?.Invoke(card);

    /// <summary>Поставить курсор в строку поиска (Ctrl+F).</summary>
    public void FocusSearch() => DocList.FocusSearch();

    /// <summary>Отразить состояние панели чтения на кнопке навбара.</summary>
    public void SetReaderOpen(bool isOpen) => NavbarControl.SetReaderOpen(isOpen);

    /// <summary>Тема или размер карточки сменились — пересобрать карточки на месте.</summary>
    public void RebuildCards() => DocList.Rebuild();

    // ── Создание: источник, группа, метка ────────────────────────────────────

    /// <summary>Пункт «Создать» боковой панели: "source", "group" или "tag".</summary>
    public async Task CreateAsync(string kind)
    {
        switch (kind)
        {
            case "source": await AddSourceAsync(null); break;
            case "group":  await NewGroupAsync(); break;
            case "tag":    await NewTagAsync(); break;
        }
    }

    /// <summary>«Добавить источник»; <paramref name="path"/> — уже выбранная папка (перетащили).</summary>
    public async Task AddSourceAsync(string? path)
    {
        if (_vm?.Library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new AddSourceDialog(library.Groups, path)) is not NewSource result) return;
        var before = library.Sources.Count;
        var source = library.AddSource(result.Path, result.Name, result.GroupId);
        if (source is not null && library.Sources.Count == before)
            MainWindow.NoticeOf(this, $"Эта папка уже в библиотеке — источник «{source.Name}».");
    }

    private async Task NewGroupAsync()
    {
        if (_vm?.Library is not { } library || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new NewGroupDialog(library.Sources, library.Groups)) is NewGroup result)
            library.AddGroup(result.Name, result.SourceIds);
    }

    private async Task NewTagAsync()
    {
        if (_vm is not { } vm || MainWindow.DialogsOf(this) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new NewTagDialog(vm.SelectedCount)) is not NewTag result) return;
        var tag = vm.Library.AddTag(result.Name, result.Color);
        if (result.ApplyToSelected) vm.SetTagOnSelected(tag.Id, true);
    }
}

using Avalonia.Controls;
using PdfLibrary.Controls.Components;
using PdfLibrary.Services;
using PdfLibrary.Views.Dialogs;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Действия с источником — одни и те же в боковой панели (правая кнопка мыши) и в списке папок
/// «Источники и чтение» (кнопка «…»): проверить сейчас, переименовать, перенести в группу,
/// показать в проводнике, убрать из библиотеки.
/// </summary>
public static class SourceMenu
{
    /// <summary>Название пункта «показать папку» по системе: в проводнике, в Finder, в папке.</summary>
    public static string ShowInFolderText =>
        OperatingSystem.IsWindows() ? "Показать в проводнике"
        : OperatingSystem.IsMacOS() ? "Показать в Finder"
        : "Открыть папку";

    public static ContextMenu Build(Control owner, LibraryService library, int sourceId)
    {
        var menu = new ContextMenu();
        if (library.Source(sourceId) is not { } source) return menu;

        var check = new MenuItem { Header = MenuIcons.Header("IconRefresh", "Проверить сейчас") };
        check.Click += (_, _) => library.Check(sourceId);

        var rename = new MenuItem { Header = MenuIcons.Header("IconPencil", "Переименовать") };
        rename.Click += async (_, _) => await RenameAsync(owner, library, sourceId);

        var move = new MenuItem { Header = MenuIcons.Header("IconFolderTree", "Перенести в группу") };
        var noGroup = new MenuItem { Header = MenuIcons.ChoiceHeader("Без группы", source.GroupId is null) };
        noGroup.Click += (_, _) => library.SetSourceGroup(sourceId, null);
        var targets = new List<MenuItem> { noGroup };
        foreach (var group in library.Groups)
        {
            var item = new MenuItem { Header = MenuIcons.ChoiceHeader(group.Name, source.GroupId == group.Id) };
            var id = group.Id;
            item.Click += (_, _) => library.SetSourceGroup(sourceId, id);
            targets.Add(item);
        }
        move.ItemsSource = targets;
        move.IsEnabled = library.Groups.Count > 0;
        MenuIcons.FixSubmenuGap(move);

        var show = new MenuItem { Header = MenuIcons.Header("IconFolder", ShowInFolderText) };
        show.Click += (_, _) =>
        {
            if (DocActions.ShowInFolder(source.Path) is { } error) MainWindow.NoticeOf(owner, error, isError: true);
        };

        var remove = new MenuItem { Header = MenuIcons.DeleteHeader("Убрать из библиотеки") };
        remove.Click += async (_, _) => await RemoveAsync(owner, library, sourceId);

        menu.ItemsSource = new object[] { check, rename, move, show, new Separator(), remove };
        return menu;
    }

    public static async Task RenameAsync(Control owner, LibraryService library, int sourceId)
    {
        if (library.Source(sourceId) is not { } source || MainWindow.DialogsOf(owner) is not { } dialogs) return;
        if (await dialogs.ShowAsync(new TextDialog("Переименовать источник", "Название", source.Name)) is string name)
            library.RenameSource(sourceId, name);
    }

    public static async Task RemoveAsync(Control owner, LibraryService library, int sourceId)
    {
        if (library.Source(sourceId) is not { } source || MainWindow.DialogsOf(owner) is not { } dialogs) return;
        var message = $"Источник «{source.Name}» уйдет из библиотеки вместе с метками на его документах. " +
                      "Файлы в папке останутся на месте — источник можно добавить снова.";
        if (await dialogs.ShowAsync(new ConfirmPanel("Убрать из библиотеки", message, "Убрать")) is true)
            library.RemoveSource(sourceId);
    }
}

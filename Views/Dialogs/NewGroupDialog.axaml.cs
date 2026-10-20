using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PdfLibrary.Core.Library;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Что выбрано в «Новая группа источников».</summary>
public sealed record NewGroup(string Name, IReadOnlyCollection<int> SourceIds);

/// <summary>
/// «Новая группа источников»: название и галочки источников. Источник из другой группы
/// подписан «сейчас в «…»» — галочка переносит его в новую.
/// </summary>
public partial class NewGroupDialog : DialogBase
{
    private readonly List<(int Id, CheckBox Box)> _boxes = [];

    public NewGroupDialog() : this([], []) { }

    public NewGroupDialog(IReadOnlyList<SourceItem> sources, IReadOnlyList<GroupItem> groups)
    {
        InitializeComponent();
        NoSources.IsVisible = sources.Count == 0;
        foreach (var source in sources)
        {
            var note = groups.FirstOrDefault(g => g.Id == source.GroupId) is { } g ? $"сейчас в «{g.Name}»" : "";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock { Text = source.Name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
            var hint = new TextBlock { Text = note, Margin = new Avalonia.Thickness(6, 0, 8, 0) };
            hint.Classes.Add("small");
            hint.Classes.Add("muted");
            Grid.SetColumn(hint, 1);
            row.Children.Add(hint);
            var box = new CheckBox
            {
                Content = row, Height = 30, Margin = new Avalonia.Thickness(8, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _boxes.Add((source.Id, box));
            SourceList.Children.Add(box);
        }
    }

    public override string Title => "Новая группа источников";

    public override void OnShown() => NameBox.Focus();

    private void OnNameChanged(object? sender, TextChangedEventArgs e) =>
        CreateButton.IsEnabled = (NameBox.Text ?? "").Trim().Length > 0;

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? "").Trim();
        if (name.Length == 0) return;
        Close(new NewGroup(name, _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id).ToList()));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

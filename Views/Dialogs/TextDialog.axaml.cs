using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Диалог с одним полем — результат строка, null — отмена.</summary>
public partial class TextDialog : DialogBase
{
    private readonly string _title;

    public TextDialog() : this("", "", "") { }

    public TextDialog(string title, string label, string value)
    {
        InitializeComponent();
        _title = title;
        LabelText.Text = label;
        ValueBox.Text = value;
    }

    public override string Title => _title;

    public override void OnShown()
    {
        ValueBox.Focus();
        ValueBox.SelectAll();
    }

    private void OnChanged(object? sender, TextChangedEventArgs e) =>
        OkButton.IsEnabled = (ValueBox.Text ?? "").Trim().Length > 0;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled) OnOk(sender, e);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close((ValueBox.Text ?? "").Trim());
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Interactivity;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Подтверждение внутри окна; результат — true по главной кнопке.</summary>
public partial class ConfirmPanel : DialogBase
{
    private readonly string _title;

    public ConfirmPanel() : this("", "", "OK") { }

    public ConfirmPanel(string title, string message, string okText)
    {
        InitializeComponent();
        _title = title;
        MessageText.Text = message;
        OkButton.Content = okText;
    }

    public override string Title => _title;

    public override void OnShown() => OkButton.Focus();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

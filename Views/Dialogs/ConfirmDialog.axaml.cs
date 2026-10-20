using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Окно «да или нет»: сообщение и две кнопки. Результат ShowDialog — true по главной кнопке.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() : this("", "OK") { }

    public ConfirmDialog(string message, string okText, string cancelText = "Отмена")
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Content = cancelText;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

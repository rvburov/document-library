using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PdfLibrary.Controls;

namespace PdfLibrary.Views.Dialogs;

/// <summary>
/// Слой диалогов главного окна: затемнение и карточка по центру, как в макете. Диалог один за
/// раз; Esc и крестик — «Отмена». Окно под затемнением недоступно, пока диалог открыт.
/// </summary>
public sealed class DialogHost : Panel
{
    private readonly Border _card;
    private readonly TextBlock _title;
    private readonly ContentControl _body;
    private DialogBase? _current;

    public DialogHost()
    {
        IsVisible = false;
        Background = Brushes.Transparent;

        var scrim = new Border();
        scrim.Bind(Border.BackgroundProperty, this.GetResourceObservable("BrushScrim"));
        Children.Add(scrim);

        _title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = new Icon { Width = 16, Height = 16 }, Width = 26, Height = 26 };
        ((Icon)close.Content).Bind(Icon.DataProperty, close.GetResourceObservable("IconClose"));
        close.Bind(ThemeProperty, close.GetResourceObservable("IconButton"));
        ToolTip.SetTip(close, "Закрыть");
        close.Click += (_, _) => _current?.Close();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(_title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _body = new ContentControl();
        var stack = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        stack.Children.Add(header);
        stack.Children.Add(_body);

        _card = new Border
        {
            Width = 460,
            MaxWidth = 460,
            Padding = new Thickness(20, 16),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            BoxShadow = BoxShadows.Parse("0 20 50 0 #47000000"),
            Child = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        _card.Bind(Border.BackgroundProperty, this.GetResourceObservable("BrushBg"));
        Children.Add(_card);

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _current is null) return;
            _current.Close();
            e.Handled = true;
        };
    }

    public bool IsOpen => _current is not null;

    /// <summary>Показать диалог и дождаться результата.</summary>
    public async Task<object?> ShowAsync(DialogBase dialog)
    {
        _current?.Close();
        _current = dialog;
        _title.Text = dialog.Title;
        _body.Content = dialog;
        IsVisible = true;
        var result = dialog.Run();
        Dispatcher.UIThread.Post(dialog.OnShown, DispatcherPriority.Loaded);
        try { return await result; }
        finally
        {
            if (ReferenceEquals(_current, dialog))
            {
                _current = null;
                _body.Content = null;
                IsVisible = false;
            }
        }
    }
}

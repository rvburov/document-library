using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

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

        // Заголовок и крестик: крупный жирный текст, кнопка
        // класса icon с обводочной иконкой.
        _title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        _title.Bind(TextBlock.FontSizeProperty, _title.GetResourceObservable("FontSizeMedium"));
        _title.Bind(TextBlock.FontWeightProperty, _title.GetResourceObservable("FontWeightBold"));
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        glyph.Bind(Avalonia.Controls.Shapes.Path.DataProperty, glyph.GetResourceObservable("IconClose"));
        glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeThicknessProperty, glyph.GetResourceObservable("IconStrokeMedium"));
        var close = new Button
        {
            Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Top,
            Content = new Viewbox { Width = 16, Height = 16, Child = new Canvas { Width = 24, Height = 24, Children = { glyph } } },
        };
        close.Classes.Add("icon");
        glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, close.GetObservable(TemplatedControl.ForegroundProperty));
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
            Width = 520,
            MaxWidth = 520,
            Padding = new Thickness(20, 16),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            BoxShadow = BoxShadows.Parse("0 20 50 0 #47000000"),
            Child = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
        };
        _card.Bind(Border.BackgroundProperty, this.GetResourceObservable("BrushBg"));
        _card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("BrushSeparator"));
        _card.Bind(Border.BorderThicknessProperty, this.GetResourceObservable("BorderLine"));
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

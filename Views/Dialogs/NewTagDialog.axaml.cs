using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Dialogs;

/// <summary>Что выбрано в «Новая метка».</summary>
public sealed record NewTag(string Name, string Color, bool ApplyToSelected);

/// <summary>
/// «Новая метка»: название, цвет из семи и — если в ленте выбраны документы — галочка «Сразу
/// поставить N выбранным документам».
/// </summary>
public partial class NewTagDialog : DialogBase
{
    /// <summary>Цвета меток — набор макета.</summary>
    public static readonly (string Color, string Label)[] Colors =
    [
        ("#34C759", "Зеленый"), ("#FF9500", "Оранжевый"), ("#FF3B30", "Красный"), ("#5E5CE6", "Индиго"),
        ("#0A84FF", "Синий"), ("#AF52DE", "Фиолетовый"), ("#8E8E93", "Серый"),
    ];

    private string _color = "#5E5CE6";

    public NewTagDialog() : this(0) { }

    public NewTagDialog(int selected)
    {
        InitializeComponent();
        if (selected > 0)
        {
            ApplyBox.IsVisible = true;
            ApplyBox.Content = $"Сразу поставить {selected} " +
                               Plural.Of(selected, "выбранному документу", "выбранным документам", "выбранным документам");
        }
        BuildSwatches();
    }

    public override string Title => "Новая метка";

    public override void OnShown() => NameBox.Focus();

    private void BuildSwatches()
    {
        Swatches.Children.Clear();
        foreach (var (color, label) in Colors)
        {
            var on = color == _color;
            var swatch = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.Parse(color)),
                BorderThickness = new Thickness(on ? 2 : 0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            swatch.Theme = Res.Theme("ButtonCore");
            swatch.Background = new SolidColorBrush(Color.Parse(color));
            if (on) swatch.BorderBrush = Res.Brush("BrushPrimaryText");
            ToolTip.SetTip(swatch, label);
            swatch.Click += (_, _) => { _color = color; BuildSwatches(); };
            Swatches.Children.Add(swatch);
        }
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e) =>
        CreateButton.IsEnabled = (NameBox.Text ?? "").Trim().Length > 0;

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? "").Trim();
        if (name.Length == 0) return;
        Close(new NewTag(name, _color, ApplyBox.IsVisible && ApplyBox.IsChecked == true));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

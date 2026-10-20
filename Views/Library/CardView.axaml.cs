using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PdfLibrary.Services;

namespace PdfLibrary.Views.Library;

/// <summary>Карточка ленты. Действия уходят ленте, в которой карточка стоит; в примере на экране
/// настроек ленты нет, и нажатия ничего не делают.</summary>
public partial class CardView : UserControl
{
    public CardView() => InitializeComponent();

    private CardViewModel? Card => DataContext as CardViewModel;

    private void OnReadCard(object? sender, RoutedEventArgs e)
    {
        if (Card is { } card) this.FindAncestorOfType<FeedView>()?.RequestRead(card);
    }

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (e.Source is Visual c && c.FindAncestorOfType<CheckBox>(includeSelf: true) is not null) return;
        if (Card is { IsMissing: false } card) this.FindAncestorOfType<FeedView>()?.RequestRead(card);
    }

    private void OnOpenCard(object? sender, RoutedEventArgs e)
    {
        if (Card is not { } card || this.FindAncestorOfType<FeedView>() is null) return;
        if (DocActions.OpenExternal(card.Doc.FullPath) is { } error) MainWindow.NoticeOf(this, error, isError: true);
    }
}

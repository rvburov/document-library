using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PdfLibrary.Database.DocIndex;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Найденный текст в карточке: фрагмент из кеша с маркерами совпадений
/// (<see cref="DocTextIndex.HitOpen"/>/<see cref="DocTextIndex.HitClose"/>) — совпадения на
/// желтой подложке, остальное обычным текстом. Строк столько, сколько разрешает размер карточки
/// (<c>MaxLines</c>), лишнее обрезается многоточием.
/// </summary>
public sealed class SnippetText : TextBlock
{
    public static readonly StyledProperty<string?> SnippetProperty =
        AvaloniaProperty.Register<SnippetText, string?>(nameof(Snippet));

    public static readonly StyledProperty<IBrush?> HighlightProperty =
        AvaloniaProperty.Register<SnippetText, IBrush?>(nameof(Highlight));

    static SnippetText()
    {
        SnippetProperty.Changed.AddClassHandler<SnippetText>((t, _) => t.Build());
        HighlightProperty.Changed.AddClassHandler<SnippetText>((t, _) => t.Build());
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Snippet
    {
        get => GetValue(SnippetProperty);
        set => SetValue(SnippetProperty, value);
    }

    public IBrush? Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    private void Build()
    {
        var inlines = new InlineCollection();
        foreach (var (text, hit) in DocTextIndex.SplitSnippet(Snippet))
        {
            var run = new Run(text);
            if (hit)
            {
                run.Background = Highlight;
                run.Foreground = Res.Brush("BrushPrimaryText");
            }
            inlines.Add(run);
        }
        Inlines = inlines;
    }
}

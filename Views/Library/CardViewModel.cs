using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Database.DocIndex;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Library;

/// <summary>Метка на карточке: точка цвета метки и название.</summary>
public sealed record TagChip(string Name, IBrush Color);

/// <summary>
/// Модель строки карточки поверх документа библиотеки. ВСЕ видимые значения карточка берет привязками к этой модели, а не читает
/// при постройке: метки, прочитанный текст и миниатюра приезжают позже ленты, и карточка
/// обновляется на месте, без пересборки.
/// </summary>
public sealed partial class CardViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private List<(string Text, bool Hit)> _snippetParts = [];

    public CardViewModel(DocItem doc, LibraryViewModel owner)
    {
        Doc = doc;
        _owner = owner;
    }

    public DocItem Doc { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrushValue))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    private bool _isHovered;

    /// <summary>Найденный текст с маркерами совпадений; пусто — нет поиска или нашлось по названию.</summary>
    [ObservableProperty] private string _snippet = "";

    [ObservableProperty] private Bitmap? _thumbnail;

    /// <summary>Сниппет перерисован — строка карточки собирает Inlines заново.</summary>
    public event Action? SnippetChanged;

    partial void OnIsSelectedChanged(bool value) => _owner.OnCardSelectionChanged(this, value);

    partial void OnSnippetChanged(string value)
    {
        _snippetParts = DocTextIndex.SplitSnippet(value);
        OnPropertyChanged(nameof(HasSnippet));
        SnippetChanged?.Invoke();
    }

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(HasBadge));
    }

    // ── Карточка: фон и рамка ────────────────────────────────────────────────

    /// <summary>Фон карточки: выделение заметнее наведения .</summary>
    public IBrush BackgroundBrush => IsSelected
        ? new SolidColorBrush(Color.FromArgb(50, 128, 128, 128))
        : IsHovered ? Brush("BrushInactiveBg", "#FAFAFA") : Brush("BrushBg", "#FFFFFF");

    public IBrush BorderBrushValue => IsSelected
        ? Brush("BrushAccent", "#007AFF")
        : Brush("BrushSeparator", "#E5E5EA");

    /// <summary>
    /// Полоса слева от плитки: у источника своего цвета нет, и полоса серая.
    /// </summary>
    public IBrush StripeBrush => Brush("BrushSecondaryText", "#8E8E93");

    // ── Тексты ───────────────────────────────────────────────────────────────

    public string Title => Doc.Title;
    public string TitleValue => Doc.Title;
    public IBrush TitleColor => Brush("BrushPrimaryText", "#1C1C1E");

    /// <summary>«Источник · папка · файл» — подпись под названием.</summary>
    public string SubtitleValue
    {
        get
        {
            if (!_owner.ShowPath) return "";
            var source = _owner.SourceName(Doc.SourceId);
            return Doc.Folder.Length > 0
                ? $"{source} · {Doc.Folder.Replace("/", " · ")} · {Doc.FileName}"
                : $"{source} · {Doc.FileName}";
        }
    }

    public bool HasSubtitle => SubtitleValue.Length > 0;

    // ── Плитка формата ───────────────────────────────────────────────────────

    /// <summary>Подпись на ленте плитки — «PDF».</summary>
    public string FormatLabel => "PDF";
    public IBrush FormatFill   => Brush("BrushFormatPdfLight", "#F9E8E6");
    public IBrush FormatStroke => Brush("BrushFormatPdf", "#D0342C");

    /// <summary>Миниатюры еще нет — окошко страницы залито серым.</summary>
    public bool HasBadge => Thumbnail is null;
    public bool HasThumbnail => Thumbnail is not null;

    // ── Правая колонка: чипы и метрики ───────────────────────────────────────

    public string TypeText => Doc.DocType;
    public bool   HasType  => _owner.ShowType && Doc.DocType.Length > 0;
    public IBrush TypeBg   => Brush("BrushAccentLight", "#E5F0FF");
    public IBrush TypeFg   => Brush("BrushAccent", "#007AFF");

    public bool   IsMissing   => Doc.Missing;
    public string MissingText => "файл не найден";
    public IBrush MissingBg   => Brush("BrushRedLight", "#FFE9E7");
    public IBrush MissingFg   => Brush("BrushRed", "#FF3B30");

    /// <summary>Серый чип про текст: не прочитан, изменен, нет текстового слоя, не прочитался.</summary>
    public string UnreadText => Doc.Missing ? "" : Doc.Changed ? "изменен · перечитать" : Doc.TextState switch
    {
        DocTextState.NotRead     => "текст не прочитан",
        DocTextState.NoText      => "нет текстового слоя",
        DocTextState.Unsupported => "поиск только по названию",
        DocTextState.Failed      => "не удалось прочитать",
        _                        => "",
    };
    public bool   IsUnread => UnreadText.Length > 0;
    public IBrush UnreadBg => Brush("BrushSecondaryBg", "#F2F2F7");
    public IBrush UnreadFg => Brush("BrushSecondaryText", "#8E8E93");

    /// <summary>Метрики: «4,2 МБ · 112 страниц»; две величины.</summary>
    public string MetaText
    {
        get
        {
            if (!_owner.ShowMeta || Doc.Missing) return "";
            var parts = new List<string>(2) { Sizes.Text(Doc.Size) };
            if (Doc.Pages > 0) parts.Add($"{Doc.Pages} {Plural.Of(Doc.Pages, "страница", "страницы", "страниц")}");
            return string.Join(" · ", parts);
        }
    }
    public bool HasMeta => MetaText.Length > 0;

    /// <summary>Форматы листов чертежа: «A1 × 1, A3 × 2» — строкой под метриками.</summary>
    public string SheetsText => _owner.ShowMeta && !Doc.Missing ? Doc.Sheets : "";
    public bool HasSheets => SheetsText.Length > 0;

    public IReadOnlyList<TagChip> Tags => _owner.TagChips(Doc);
    public bool HasTags => Doc.Tags.Count > 0;

    // ── Действия ─────────────────────────────────────────────────────────────

    public bool CanOpen => !Doc.Missing;

    /// <summary>Строка действий гаснет прозрачностью: место занято, высота карточек одна.</summary>
    public double ReadOpacity => CanOpen ? 1 : 0;

    public bool HasSnippet => _snippetParts.Count > 0;

    /// <summary>
    /// Собрать строку сниппета: обычные куски — как есть, совпадения — с желтой подсветкой.
    /// Inlines привязкой не выражаются, поэтому карточка зовет это сама — при постройке и по
    /// событию <see cref="SnippetChanged"/>.
    /// </summary>
    public void RenderSnippet(TextBlock target)
    {
        target.Inlines?.Clear();
        if (_snippetParts.Count == 0) return;

        var hitBg = Brush("BrushHighlight", "#FFF0B3");
        var plain = Brush("BrushSecondaryText", "#8E8E93");
        var text  = Brush("BrushPrimaryText", "#1C1C1E");

        foreach (var (chunk, hit) in _snippetParts)
            target.Inlines?.Add(new Run(chunk)
            {
                Foreground = hit ? text : plain,
                Background = hit ? hitBg : null,
                FontWeight = hit ? FontWeight.SemiBold : FontWeight.Normal,
            });
    }

    /// <summary>Документ изменился (прочитан, метки, переезд, тема) — перечитать все подписи.</summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        SnippetChanged?.Invoke();
    }

    /// <summary>Загрузить миниатюру, если ее еще нет. Зовется, когда карточка появилась на экране.</summary>
    public async void EnsureThumbnail()
    {
        if (Thumbnail is not null || Doc.Missing || !_owner.ShowThumbnails) return;
        var ready = ThumbnailLoader.Peek(Doc.FullPath, Doc.Mtime, Doc.Size);
        if (ready is not null) { Thumbnail = ready; return; }
        Thumbnail = await ThumbnailLoader.GetAsync(Doc.FullPath, Doc.Mtime, Doc.Size);
    }

    private static IBrush Brush(string key, string fallback) =>
        Application.Current is { } app &&
        app.TryFindResource(key, app.ActualThemeVariant, out var res) && res is IBrush b
            ? b : new SolidColorBrush(Color.Parse(fallback));
}

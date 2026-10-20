using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;
using PdfLibrary.Utils;

namespace PdfLibrary.Views.Library;

/// <summary>Метка на карточке: точка цвета метки и название.</summary>
public sealed record TagChip(string Name, IBrush Color);

/// <summary>
/// Размеры карточки по настройке «Размер карточки»: плитка, лента под ней, сколько строк
/// найденного текста видно и наименьшая высота карточки. Числа — из макета.
/// </summary>
public sealed record CardMetrics(double TileWidth, double TileHeight, double TapeHeight, int SnippetLines,
                                 double MinHeight, int SnippetWords)
{
    public static readonly CardMetrics Small  = new(56, 40, 16, 1, 84, 14);
    public static readonly CardMetrics Medium = new(84, 60, 20, 3, 112, 32);
    public static readonly CardMetrics Large  = new(120, 86, 24, 6, 160, 60);

    public static CardMetrics Of(CardSize size) => size switch
    {
        CardSize.Small => Small,
        CardSize.Large => Large,
        _              => Medium,
    };

    /// <summary>Высота окошка миниатюры над лентой.</summary>
    public double ImageHeight => TileHeight;
}

/// <summary>
/// Карточка ленты — обертка документа для показа. Тексты собираются здесь, а не в разметке:
/// «источник · папка · файл», «3,1 МБ · 48 стр. · A4 × 48», лента плитки «A4 · 48 с.».
/// </summary>
public sealed partial class CardViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;

    public CardViewModel(DocItem doc, LibraryViewModel owner)
    {
        Doc = doc;
        _owner = owner;
    }

    public DocItem Doc { get; }

    /// <summary>Найденный текст с маркерами совпадений; пусто — нет поиска или нашлось по названию.</summary>
    [ObservableProperty] private string _snippet = "";

    [ObservableProperty] private bool _isSelected;

    /// <summary>Карточка открыта в панели чтения — подсвечена в списке панели.</summary>
    [ObservableProperty] private bool _isCurrent;

    [ObservableProperty] private Bitmap? _thumbnail;

    partial void OnIsSelectedChanged(bool value) => _owner.OnCardSelectionChanged(this, value);

    public string Title => Doc.Title;

    public string PathLine
    {
        get
        {
            var source = _owner.SourceName(Doc.SourceId);
            return Doc.Folder.Length > 0
                ? $"{source} · {Doc.Folder.Replace("/", " / ")} · {Doc.FileName}"
                : $"{source} · {Doc.FileName}";
        }
    }

    public bool HasSnippet => Snippet.Length > 0;

    partial void OnSnippetChanged(string value) => OnPropertyChanged(nameof(HasSnippet));

    public string TypeText => Doc.DocType;
    public bool HasType => Doc.DocType.Length > 0;

    public bool IsMissing => Doc.Missing;
    public bool IsUnread  => Doc.IsUnread;
    public bool IsChanged => !Doc.Missing && Doc.Changed;
    public bool IsScan    => !Doc.Missing && Doc.IsScan;
    public bool IsFailed  => !Doc.Missing && Doc.TextState == DocTextState.Failed;

    public double ActionsOpacity => Doc.Missing ? 0.4 : 1;

    public IReadOnlyList<TagChip> Tags => _owner.TagChips(Doc);
    public bool HasTags => Doc.Tags.Count > 0;

    /// <summary>«3,1 МБ · 48 стр. · A4 × 48»; у пропавшего — «путь недоступен».</summary>
    public string Meta
    {
        get
        {
            if (Doc.Missing) return "путь недоступен";
            var parts = new List<string> { Sizes.Text(Doc.Size) };
            if (Doc.Pages > 0) parts.Add($"{Doc.Pages} стр.");
            if (Doc.Sheets.Length > 0) parts.Add(Doc.Sheets);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Лента под миниатюрой: «A4 · 48 с.»; до чтения — «PDF», у пропавшего — «нет файла».</summary>
    public string Tape
    {
        get
        {
            if (Doc.Missing) return "нет файла";
            if (Doc.Pages <= 0) return "PDF";
            return Doc.Sheet.Length > 0 ? $"{Doc.Sheet} · {Doc.Pages} с." : $"{Doc.Pages} с.";
        }
    }

    /// <summary>Плитка без миниатюры: формат крупно…</summary>
    public string TileFormat => Doc.Missing ? "—" : Doc.Sheet.Length > 0 ? Doc.Sheet : "PDF";

    /// <summary>…и число страниц под ним.</summary>
    public string TilePages => Doc.Pages > 0 ? $"{Doc.Pages} с." : "";

    /// <summary>Строка списка панели чтения: «48 стр. · 3,1 МБ».</summary>
    public string ShortMeta
    {
        get
        {
            if (Doc.Missing) return "файл не найден";
            return Doc.Pages > 0 ? $"{Doc.Pages} стр. · {Sizes.Text(Doc.Size)}" : Sizes.Text(Doc.Size);
        }
    }

    // ── Настройки «Лента и карточки» ─────────────────────────────────────────

    public CardMetrics Metrics => _owner.Metrics;
    public bool ShowThumbnail => _owner.ShowThumbnails;
    public bool ShowTileText => !_owner.ShowThumbnails;
    public bool ShowPath => _owner.ShowPath;
    public bool ShowMeta => _owner.ShowMeta;
    public bool ShowTypeChip => _owner.ShowType && HasType;

    /// <summary>Документ изменился (прочитан, метки, переезд) — перечитать все подписи.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Загрузить миниатюру, если ее еще нет. Зовется, когда карточка появилась на экране.</summary>
    public async void EnsureThumbnail()
    {
        if (Thumbnail is not null || Doc.Missing || !ShowThumbnail) return;
        var ready = ThumbnailLoader.Peek(Doc.FullPath, Doc.Mtime, Doc.Size);
        if (ready is not null) { Thumbnail = ready; return; }
        Thumbnail = await ThumbnailLoader.GetAsync(Doc.FullPath, Doc.Mtime, Doc.Size);
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PdfLibrary.Views.Library;

/// <summary>
/// Лента карточек: блок поиска с подвалом
/// и нитью чтения, карточки с плиткой формата, полоса прокрутки отдельной колонкой.
///
/// Шаблон строки строится в C#, а не в XAML: размеры и
/// кисти берутся из ресурсов и должны переживать смену масштаба шрифта, а все видимые значения
/// идут привязками к модели строки — метки, прочитанный текст и миниатюра приезжают позже ленты.
/// </summary>
public partial class DocListView : UserControl
{
    /// <summary>Документ отдают системе — «Открыть» в карточке.</summary>
    public event EventHandler<CardViewModel>? DocActivated;

    /// <summary>
    /// Документ просят прочитать в правой панели — «Прочитать» в карточке или двойной клик.
    /// Это ГЛАВНОЕ действие документа: чтение с подсветкой найденного, не выходя из программы.
    /// </summary>
    public event EventHandler<CardViewModel>? DocReadRequested;

    private LibraryViewModel? _vm;

    /// <summary>Пример в настройках: карточка не выделяется и не открывается.</summary>
    private bool _preview;

    /// <summary>
    /// Карточка для примера в настройках «Лента и карточки» — та же постройка, что у ленты,
    /// чтобы пример не расходился с лентой. Щелчки по ней ничего не делают.
    /// </summary>
    public static Control PreviewCard(CardViewModel card, LibraryViewModel owner) =>
        new DocListView { _vm = owner, _preview = true }.BuildCard(card);
    private CardViewModel? _anchor;

    public DocListView()
    {
        InitializeComponent();
        Focusable = true;

        CardsHost.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            CardViewModel card => BuildCard(card),
            _                  => new Control(),
        }, supportsRecycling: false);

        SearchBox.TextChanged += (_, _) =>
        {
            if (_vm is not null && _vm.Query != SearchText) _vm.Query = SearchText;
            SyncCommandRow();
        };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Find(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Clear(); e.Handled = true; }
        };
        FindBtn.Click  += (_, _) => Find();
        ClearBtn.Click += (_, _) => Clear();
        StatusAction.Tapped += (_, e) => { e.Handled = true; _vm?.RunStatusAction(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm is { HasSelection: true }) { _vm.ClearSelection(); e.Handled = true; }
        };

        SyncCommandRow();
    }

    /// <summary>Подключить к модели ленты: карточки, строка состояния, нить чтения.</summary>
    public void Attach(LibraryViewModel vm)
    {
        _vm = vm;
        SearchBox.Text = vm.Query;
        vm.PropertyChanged += OnVmChanged;
        vm.CardsRebuildRequested += Rebuild;
        ShowCards();
        ShowStatus();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.Cards):
                ShowCards();
                break;
            case nameof(LibraryViewModel.StatusText):
            case nameof(LibraryViewModel.StatusAction):
            case nameof(LibraryViewModel.ReadFraction):
            case nameof(LibraryViewModel.ReadingNow):
                ShowStatus();
                break;
            case nameof(LibraryViewModel.Query):
                if (_vm is not null && SearchText != _vm.Query) SearchBox.Text = _vm.Query;
                break;
        }
    }

    /// <summary>Поставить курсор в строку поиска (Ctrl+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Текст запроса.</summary>
    public string SearchText => SearchBox.Text ?? "";

    private void ShowCards()
    {
        if (_vm is null) return;
        CardsHost.ItemsSource = _vm.Cards;
        EmptyLabel.Text       = _vm.EmptyText;
        EmptyLabel.IsVisible  = _vm.Cards.Count == 0;
    }

    /// <summary>Собрать карточки заново: сменились размер, шрифт, миниатюры или тема.</summary>
    public void Rebuild()
    {
        var offset = CardsScroll.Offset;
        CardsHost.ItemsSource = null;
        ShowCards();
        Dispatcher.UIThread.Post(() => CardsScroll.Offset = offset, DispatcherPriority.Background);
    }

    // ── Блок поиска ──────────────────────────────────────────────────────────

    // Тот же запрос можно нажать еще раз: повтор — это «обновить выдачу», и он нужен после
    // чтения документов.
    private void Find()
    {
        if (_vm is null || SearchText.Trim().Length == 0) return;
        _vm.Query = SearchText;
        _vm.ApplySearch();
    }

    private void Clear()
    {
        SearchBox.Text = "";
        SearchBox.Focus();
        SyncCommandRow();
        _vm?.ClearSearch();
    }

    // Крестик, кнопка и цвет якоря — одной функцией: три состояния одного ряда не должны
    // выставляться порознь.
    private void SyncCommandRow()
    {
        ClearBtn.IsVisible = SearchText.Length > 0;
        FindBtn.IsEnabled  = SearchText.Trim().Length > 0;
        SearchIcon.Stroke  = SearchText.Length > 0
            ? Res("BrushAccent",        "#007AFF")
            : Res("BrushSecondaryText", "#8E8E93");
    }

    /// <summary>
    /// Кисть из палитры с запасным значением. Именно <c>TryFindResource</c>, а НЕ
    /// <c>FindResource</c>: метод зовется и из конструктора, когда контрол еще не в дереве.
    /// </summary>
    private IBrush Res(string key, string fallbackHex) =>
        Find(key) is IBrush b ? b : new SolidColorBrush(Color.Parse(fallbackHex));

    // Ресурс по ключу: сначала по дереву контрола, вне дерева — у программы. Вне дерева карточку
    // строит пример в настройках (PreviewCard): там поиск по дереву ничего не находит.
    private object? Find(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var res) ? res
        : Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out res) ? res
        : null;

    // Подвал блока: строка состояния, действие справа и нить хода чтения по нижней кромке.
    private void ShowStatus()
    {
        if (_vm is null) return;
        StatusLabel.Text = _vm.StatusText;
        ToolTip.SetTip(StatusLabel, _vm.StatusText);
        StatusAction.Text      = _vm.StatusAction;
        StatusAction.IsVisible = _vm.StatusAction.Length > 0;

        if (!_vm.ReadingNow) { ReadThread.IsVisible = false; return; }
        var part = Math.Clamp(_vm.ReadFraction, 0, 1);
        ReadThread.ColumnDefinitions[0].Width = new GridLength(part, GridUnitType.Star);
        ReadThread.ColumnDefinitions[1].Width = new GridLength(1 - part, GridUnitType.Star);
        ReadThread.IsVisible = true;
    }

    /// <summary>Показать ошибку раздела — тот же баннер, что в диалогах.</summary>
    public void ShowNotice(string text) => NoticeBanner.Show(text);

    public void HideNotice() => NoticeBanner.Hide();

    // ── Карточка документа ───────────────────────────────────────────────────

    private Control BuildCard(CardViewModel vm)
    {
        if (vm is null || _vm is null) return new Control();
        vm.EnsureThumbnail();

        var bold      = Find("FontWeightBold") is FontWeight fw ? fw : FontWeight.SemiBold;
        var secondary = Res("BrushSecondaryText", "#8E8E93");
        var accent    = Res("BrushAccent", "#007AFF");
        var radius    = Find("RadiusMedium") is CornerRadius cr ? cr : new CornerRadius(8);
        var small     = Find("FontSizeSmall") is double sz ? sz : 10;

        Control Bound<T>(T c, params (AvaloniaProperty Prop, string Name)[] binds) where T : Control
        {
            foreach (var (prop, name) in binds) c.Bind(prop, new Binding(name) { Source = vm });
            return c;
        }

        // Колонка 0 — плитка формата: окошко страницы сверху, лента формата снизу. Миниатюры
        // выключены настройкой — окошка нет вовсе: плитка залита цветом формата.
        var withThumbs = _vm.ShowThumbnails;
        var shape      = _vm.Shape;

        var sheet = new StackPanel();

        if (withThumbs)
        {
            var thumb = (Image)Bound(new Image
            {
                Width   = shape.BoxWidth,
                Height  = shape.BoxHeight,
                Stretch = Stretch.UniformToFill,
            },
            (Image.SourceProperty,     nameof(CardViewModel.Thumbnail)),
            (Visual.IsVisibleProperty, nameof(CardViewModel.HasThumbnail)));

            var pageFill = (Border)Bound(new Border(),
                (Visual.IsVisibleProperty, nameof(CardViewModel.HasBadge)));
            pageFill.Background = Res("BrushSecondaryBg", "#F2F2F7");

            var pageBox = new Panel();
            pageBox.Children.Add(pageFill);
            pageBox.Children.Add(thumb);

            sheet.Children.Add(new Border
            {
                Width  = shape.BoxWidth,
                Height = shape.BoxHeight,
                Child  = pageBox,
            });
        }

        // Надпись формата растет вместе с лентой — токеном типографики, а не числом.
        var labelSize = Find(shape.LabelFontKey) is double d ? d : shape.LabelFontFallback;

        var tapeText = (TextBlock)Bound(new TextBlock
        {
            FontWeight          = FontWeight.SemiBold,
            FontSize            = labelSize,
            TextTrimming        = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        },
        (TextBlock.TextProperty,       nameof(CardViewModel.FormatLabel)),
        (TextBlock.ForegroundProperty, nameof(CardViewModel.FormatStroke)));

        var tape = (Border)Bound(new Border
        {
            Width  = shape.BoxWidth,
            Height = withThumbs ? shape.TapeHeight : shape.TileHeight,
            Child  = tapeText,
        },
        (Border.BackgroundProperty, nameof(CardViewModel.FormatFill)));

        sheet.Children.Add(tape);
        // Скругленный клип, а НЕ ClipToBounds у рамки: тот режет по прямоугольнику.
        Controls.Behaviors.RoundedClip.SetRadius(sheet, new CornerRadius(3));

        // Рамка вокруг ВСЕГО документа — окошка и ленты разом, цветом формата.
        var preview = (Border)Bound(new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness   = new Thickness(1),
            CornerRadius      = new CornerRadius(4),
            Child             = sheet,
        },
        (Border.BorderBrushProperty, nameof(CardViewModel.FormatStroke)));

        // Колонка 2 — название, подпись «источник · папка · имя файла», сниппет и действия.
        var title = (TextBlock)Bound(new TextBlock
        {
            FontWeight   = bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        },
        (TextBlock.TextProperty,       nameof(CardViewModel.TitleValue)),
        (TextBlock.ForegroundProperty, nameof(CardViewModel.TitleColor)));

        var subtitle = (TextBlock)Bound(new TextBlock
        {
            Foreground   = secondary,
            FontSize     = small,
            TextTrimming = TextTrimming.CharacterEllipsis,
        },
        (TextBlock.TextProperty,   nameof(CardViewModel.SubtitleValue)),
        (Visual.IsVisibleProperty, nameof(CardViewModel.HasSubtitle)));

        var snippet = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxLines     = shape.SnippetLines,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize     = small,
            Margin       = new Thickness(0, 2, 0, 0),
        };
        snippet.Bind(Visual.IsVisibleProperty, new Binding(nameof(CardViewModel.HasSnippet)) { Source = vm });
        vm.RenderSnippet(snippet);
        vm.SnippetChanged += () => vm.RenderSnippet(snippet);

        // Два действия у документа: «Прочитать» открывает правую панель, «Открыть» отдает файл
        // системе. Кликабельные строки, а не кнопки: действия второстепенны рядом с карточкой.
        var readRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 14,
            Margin      = new Thickness(0, 6, 0, 0),
        };
        readRow.Children.Add(ActionLink("IconBookOpen", "Прочитать", accent,
            () => DocReadRequested?.Invoke(this, vm)));
        readRow.Children.Add(ActionLink("IconOpenExternal", "Открыть", secondary,
            () => DocActivated?.Invoke(this, vm)));

        // Место под строку действий занято ВСЕГДА — высота у всех карточек одна.
        readRow.Bind(Visual.OpacityProperty, new Binding(nameof(CardViewModel.ReadOpacity)) { Source = vm });
        readRow.Bind(InputElement.IsHitTestVisibleProperty,
                     new Binding(nameof(CardViewModel.CanOpen)) { Source = vm });

        var textTop = new StackPanel { Spacing = 2 };
        textTop.Children.Add(title);
        textTop.Children.Add(subtitle);
        textTop.Children.Add(snippet);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(textTop, 0);
        Grid.SetRow(readRow, 2);
        left.Children.Add(textTop);
        left.Children.Add(readRow);

        // Колонка 4 — чипы состояния и метрики файла.
        var right = new StackPanel
        {
            Spacing             = 5,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        identity.Children.Add(TagChips(vm));
        identity.Children.Add(BoundChip(vm, nameof(CardViewModel.TypeText),
                                             nameof(CardViewModel.TypeBg),
                                             nameof(CardViewModel.TypeFg),
                                             nameof(CardViewModel.HasType)));
        right.Children.Add(identity);
        right.Children.Add(BoundChip(vm, nameof(CardViewModel.UnreadText),
                                         nameof(CardViewModel.UnreadBg),
                                         nameof(CardViewModel.UnreadFg),
                                         nameof(CardViewModel.IsUnread)));
        right.Children.Add(BoundChip(vm, nameof(CardViewModel.MissingText),
                                         nameof(CardViewModel.MissingBg),
                                         nameof(CardViewModel.MissingFg),
                                         nameof(CardViewModel.IsMissing)));

        right.Children.Add(Bound(new TextBlock
        {
            Foreground          = secondary,
            FontSize            = small,
            HorizontalAlignment = HorizontalAlignment.Right,
        },
        (TextBlock.TextProperty,   nameof(CardViewModel.MetaText)),
        (Visual.IsVisibleProperty, nameof(CardViewModel.HasMeta))));

        right.Children.Add(Bound(new TextBlock
        {
            Foreground          = secondary,
            FontSize            = small,
            HorizontalAlignment = HorizontalAlignment.Right,
        },
        (TextBlock.TextProperty,   nameof(CardViewModel.SheetsText)),
        (Visual.IsVisibleProperty, nameof(CardViewModel.HasSheets))));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,12,Auto") };
        var stripe = (Border)Bound(new Border
        {
            Width = 4, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
        },
        (Border.BackgroundProperty, nameof(CardViewModel.StripeBrush)));

        var previewHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        previewHost.Children.Add(stripe);
        previewHost.Children.Add(preview);

        Grid.SetColumn(previewHost, 0);
        Grid.SetColumn(left, 2);
        Grid.SetColumn(right, 4);
        grid.Children.Add(previewHost);
        grid.Children.Add(left);
        grid.Children.Add(right);

        var card = new Border
        {
            CornerRadius = radius,
            Padding      = new Thickness(14, 10),
            Margin       = new Thickness(0, 0, 0, 8),
            // Высота карточки — из таблицы размеров, а не по содержимому: все строки ленты
            // обязаны быть одной высоты. MinHeight, а не Height: при крупном шрифте текст
            // обязан помещаться.
            MinHeight    = shape.CardHeight,
            Child        = grid,
        };
        card.Bind(Border.BorderThicknessProperty, card.GetResourceObservable("BorderLine"));
        card.Bind(Border.BackgroundProperty,
                  new Binding(nameof(CardViewModel.BackgroundBrush)) { Source = vm });
        card.Bind(Border.BorderBrushProperty,
                  new Binding(nameof(CardViewModel.BorderBrushValue)) { Source = vm });

        card.PointerEntered += (_, _) => vm.IsHovered = true;
        card.PointerExited  += (_, _) => vm.IsHovered = false;
        card.Tapped         += (_, e) => OnCardTapped(vm, e);
        // Двойной клик — то же, что «Прочитать».
        card.DoubleTapped   += (_, e) =>
        {
            e.Handled = true;
            DocReadRequested?.Invoke(this, vm);
        };
        return card;
    }

    /// <summary>
    /// Действие карточки: иконка плюс подпись, клик целиком по паре. Останавливает событие
    /// своим <c>Handled</c> — иначе клик добрался бы до карточки и вдобавок сменил выделение.
    /// </summary>
    private static Control ActionLink(string icon, string text, IBrush brush, Action run)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
            Cursor      = new Cursor(StandardCursorType.Hand),
            Background  = Brushes.Transparent,
        };
        row.Children.Add(IconGlyph(icon, brush));
        row.Children.Add(new TextBlock
        {
            Text              = text,
            Foreground        = brush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Tapped += (_, e) => { e.Handled = true; run(); };
        return row;
    }

    private static Control IconGlyph(string key, IBrush brush)
    {
        var size = (double)Application.Current!.FindResource("IconSizeMedium")!;
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data            = (Geometry)Application.Current!.FindResource(key)!,
            Stroke          = brush,
            StrokeThickness = (double)Application.Current!.FindResource("IconStrokeMedium")!,
            StrokeLineCap   = PenLineCap.Round,
            StrokeJoin      = PenLineJoin.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size, Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas,
        };
    }

    // Чип карточки: пилюля с текстом. Все привязано к модели строки — и текст, и обе кисти, и
    // видимость. Строится один раз на всю жизнь контейнера.
    private static Border BoundChip(CardViewModel vm, string textProp, string bgProp, string fgProp,
                                    string visibleProp)
    {
        var label = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140 };
        label.Bind(TextBlock.TextProperty,       new Binding(textProp) { Source = vm });
        label.Bind(TextBlock.ForegroundProperty, new Binding(fgProp)   { Source = vm });

        var chip = new Border
        {
            CornerRadius = new CornerRadius(99),
            Padding      = new Thickness(10, 3),
            Child        = label,
        };
        chip.Bind(Border.BackgroundProperty, new Binding(bgProp) { Source = vm });
        chip.Bind(Visual.IsVisibleProperty,  new Binding(visibleProp) { Source = vm });
        return chip;
    }

    // Метки документа — пилюли того же вида, что чип типа: точка цвета метки и название на
    // серой подложке — вид чипа, чтобы правая колонка оставалась одним рядом пилюль.
    private Control TagChips(CardViewModel vm)
    {
        var host = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }),
            ItemTemplate = new FuncDataTemplate<TagChip>((tag, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                row.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 7, Height = 7, Fill = tag?.Color, VerticalAlignment = VerticalAlignment.Center,
                });
                row.Children.Add(new TextBlock
                {
                    Text = tag?.Name, FontWeight = FontWeight.SemiBold, MaxWidth = 120,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Res("BrushSecondaryText", "#8E8E93"),
                });
                return new Border
                {
                    CornerRadius = new CornerRadius(99),
                    Padding      = new Thickness(9, 3),
                    Background   = Res("BrushSecondaryBg", "#F2F2F7"),
                    Child        = row,
                };
            }),
        };
        host.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CardViewModel.Tags)) { Source = vm });
        host.Bind(Visual.IsVisibleProperty, new Binding(nameof(CardViewModel.HasTags)) { Source = vm });
        return host;
    }

    // ── Выделение ────────────────────────────────────────────────────────────
    // Клик выбирает одну карточку, Shift (или Ctrl) — добавляет и снимает,
    // Ctrl+Shift — диапазон от последнего клика.

    private void OnCardTapped(CardViewModel vm, TappedEventArgs e)
    {
        if (_vm is null || _preview) return;
        Focus();

        var mods = e.KeyModifiers;
        var add  = mods.HasFlag(KeyModifiers.Shift) || mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        if (mods.HasFlag(KeyModifiers.Shift) && mods.HasFlag(KeyModifiers.Control) && _anchor is not null)
            _vm.SelectRange(_anchor, vm);
        else if (add)
        {
            vm.IsSelected = !vm.IsSelected;
            _anchor = vm;
        }
        else
        {
            _vm.SelectOnly(vm);
            _anchor = vm;
        }
    }
}

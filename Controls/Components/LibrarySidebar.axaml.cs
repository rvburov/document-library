using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfLibrary.Views.Library;

namespace PdfLibrary.Controls.Components;

/// <summary>Что лежит в строке боковой панели ленты.</summary>
public enum LibraryRowKind { Group, Source, Tags, Tag }

/// <summary>
/// Боковая панель ленты — дерево источников: группы раскрываются
/// стрелкой, источники отмечаются галочкой (отмеченные — в ленте), клик по строке источника
/// переключает галочку. Ниже — узел «Метки»: клик по метке оставляет в ленте только ее
/// документы, повторный клик снимает фильтр.
///
/// Данные — строки <see cref="LibraryViewModel.SourceRows"/> и <see cref="LibraryViewModel.TagRows"/>;
/// создание источника, группы и метки панель отдает наверх событием: диалоги открывает лента.
/// </summary>
public partial class LibrarySidebar : UserControl
{
    /// <summary>Пункт меню «Создать»: "source", "group" или "tag".</summary>
    public event Action<string>? CreateRequested;

    /// <summary>Правая кнопка мыши по источнику — меню действий с ним (id источника, якорь).</summary>
    public event Action<int, Control>? SourceMenuRequested;

    private LibraryViewModel? _vm;
    private readonly ObservableCollection<LibrarySidebarRow> _rows = [];
    private bool _tagsExpanded = true;
    private int? _selectedSource;

    public LibrarySidebar()
    {
        InitializeComponent();
        RowsHost.ItemsSource = _rows;

        CreateBtn.Click += (_, _) => ShowCreateMenu();
        ExpandBtn.IsCheckedChanged += (_, _) =>
        {
            if (_vm is null || _syncExpand) return;
            var expand = ExpandBtn.IsChecked == true;
            _tagsExpanded = expand;
            foreach (var group in _vm.Library.Groups.ToList())
                if (group.Expanded != expand) _vm.Library.SetGroupExpanded(group.Id, expand);
            Rebuild();
        };
        ToggleAllBtn.Click += (_, _) => _vm?.ToggleAllShown();
        CheckBtn.Click += (_, _) => _vm?.Library.CheckAll();
    }

    public void Attach(LibraryViewModel vm)
    {
        _vm = vm;
        vm.SourceRows.CollectionChanged += OnRowsChanged;
        vm.TagRows.CollectionChanged += OnRowsChanged;
        Rebuild();
    }

    /// <summary>Перечитать строки — например, после смены темы: кисти строк берутся кодом.</summary>
    public void Refresh() => Rebuild();

    // Модель пересобирает строки целиком (Clear + Add): строим свои строки один раз на пачку.
    private bool _rebuildPosted;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildPosted) return;
        _rebuildPosted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _rebuildPosted = false;
            Rebuild();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private bool _syncExpand;

    private void Rebuild()
    {
        if (_vm is not { } vm) return;

        var fresh = new List<LibrarySidebarRow>();
        foreach (var source in vm.SourceRows)
            fresh.Add(new LibrarySidebarRow(source.IsGroup ? LibraryRowKind.Group : LibraryRowKind.Source,
                                            source.Id, source.Name, source.Depth)
            {
                Expanded = source.Expanded,
                Checked = source.IsChecked,
                Status = source.Status,
                StatusFailed = source.Unavailable,
                Selected = !source.IsGroup && _selectedSource == source.Id,
            });

        if (vm.TagRows.Count > 0)
        {
            fresh.Add(new LibrarySidebarRow(LibraryRowKind.Tags, 0, "Метки", 0) { Expanded = _tagsExpanded });
            if (_tagsExpanded)
                foreach (var tag in vm.TagRows)
                    fresh.Add(new LibrarySidebarRow(LibraryRowKind.Tag, tag.Id, tag.Name, 1)
                    {
                        TagColor = tag.Color,
                        Selected = tag.IsActive,
                    });
        }

        for (var i = 0; i < fresh.Count; i++) fresh[i].IsFirstRow = i == 0;

        // Строки переносятся на месте, пока их столько же: контейнеры не строятся заново, и
        // наведение остается под указателем.
        if (fresh.Count == _rows.Count)
            for (var i = 0; i < fresh.Count; i++) _rows[i].Rebind(fresh[i]);
        else
        {
            _rows.Clear();
            foreach (var row in fresh) _rows.Add(row);
        }

        EmptyHint.IsVisible = vm.SourceRows.Count == 0 && vm.TagRows.Count == 0;

        _syncExpand = true;
        var expanded = (vm.Library.Groups.Count == 0 || vm.AllGroupsExpanded) && (vm.TagRows.Count == 0 || _tagsExpanded);
        ExpandBtn.IsChecked = expanded;
        ExpandIcon.IsVisible = !expanded;
        CollapseIcon.IsVisible = expanded;
        ToolTip.SetTip(ExpandBtn, expanded ? "Свернуть все" : "Развернуть все");
        _syncExpand = false;
        ExpandBtn.IsEnabled = vm.Library.Groups.Count > 0 || vm.TagRows.Count > 0;

        ToolTip.SetTip(ToggleAllBtn, vm.AllSourcesShown ? "Выключить все" : "Включить все");
    }

    // ── Меню «Создать» ───────────────────────────────────────────────────────

    private void ShowCreateMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("IconFolderPlus", "Источник", "source"));
        menu.Items.Add(CreateItem("IconFolders", "Группа источников", "group"));
        menu.Items.Add(CreateItem("IconTag", "Метка", "tag"));
        // Подсказка кнопки над открытым меню закрывала бы первый пункт.
        ToolTip.SetIsOpen(CreateBtn, false);
        menu.Open(CreateBtn);
    }

    private MenuItem CreateItem(string icon, string text, string kind)
    {
        var item = new MenuItem { Header = MenuIcons.Header(icon, text) };
        item.Click += (_, _) => CreateRequested?.Invoke(kind);
        return item;
    }

    private void OnEmptyContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control anchor) return;
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("IconFolderPlus", "Добавить источник", "source"));
        menu.Items.Add(CreateItem("IconFolders", "Создать группу источников", "group"));
        menu.Items.Add(CreateItem("IconTag", "Создать метку", "tag"));
        menu.Open(anchor);
        e.Handled = true;
    }

    // ── Взаимодействие со строками ───────────────────────────────────────────

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySidebarRow row } || _vm is not { } vm) return;
        switch (row.Kind)
        {
            case LibraryRowKind.Group:
                if (vm.SourceRows.FirstOrDefault(r => r.IsGroup && r.Id == row.Id) is { } group) vm.FlipGroup(group);
                break;
            case LibraryRowKind.Tags:
                _tagsExpanded = !_tagsExpanded;
                Rebuild();
                break;
            case LibraryRowKind.Tag:
                if (vm.TagRows.FirstOrDefault(t => t.Id == row.Id) is { } tag) vm.PickTag(tag);
                break;
            case LibraryRowKind.Source:
                // Клик по строке переключает галочку, а подсветка говорит, с
                // каким источником работают клавиши и меню.
                _selectedSource = row.Id;
                ToggleSource(row);
                break;
        }
        e.Handled = true;
    }

    private void OnChevronTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySidebarRow row } || !row.ShowChevron) return;
        OnRowTapped(sender, e);
    }

    private void OnCheckboxTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySidebarRow { Kind: LibraryRowKind.Source } row }) return;
        ToggleSource(row);
        e.Handled = true;
    }

    private void ToggleSource(LibrarySidebarRow row)
    {
        if (_vm?.SourceRows.FirstOrDefault(r => !r.IsGroup && r.Id == row.Id) is not { } source) return;
        source.IsChecked = !source.IsChecked;
        _vm.ToggleRow(source);
    }

    private void OnRowEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: LibrarySidebarRow row }) row.Hovered = true;
    }

    private void OnRowExit(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: LibrarySidebarRow row }) row.Hovered = false;
    }

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySidebarRow row } anchor) return;
        e.Handled = true;
        if (row.Kind == LibraryRowKind.Group) { ShowGroupMenu(row, anchor); return; }
        if (row.Kind != LibraryRowKind.Source) return;
        _selectedSource = row.Id;
        Rebuild();
        SourceMenuRequested?.Invoke(row.Id, anchor);
    }
    // У группы в дереве галочки нет — она раскрывается. Включить или выключить разом
    // все ее источники можно из меню группы.
    private void ShowGroupMenu(LibrarySidebarRow row, Control anchor)
    {
        if (_vm?.SourceRows.FirstOrDefault(r => r.IsGroup && r.Id == row.Id) is not { } group) return;
        var menu = new ContextMenu();
        foreach (var (icon, text, on) in new[]
                 {
                     ("IconSquareCheck", "Включить все источники группы", true),
                     ("IconEyeClosed",   "Выключить все источники группы", false),
                 })
        {
            var item = new MenuItem { Header = MenuIcons.Header(icon, text) };
            item.Click += (_, _) =>
            {
                group.IsChecked = on;
                _vm.ToggleRow(group);
            };
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }
}

/// <summary>
/// Строка боковой панели ленты: слоты стрелки и галочки, высота строки таблицы и белая
/// линия-разделитель, цвета наведения и выбора.
/// </summary>
public partial class LibrarySidebarRow : ObservableObject
{
    private const double IndentStep = 22;

    private static IBrush Brush(string key, string fallbackHex) =>
        Application.Current is { } app &&
        app.TryFindResource(key, app.ActualThemeVariant, out var res) && res is IBrush b
            ? b
            : new SolidColorBrush(Color.Parse(fallbackHex));

    // Геометрия иконки из общего словаря Assets/Styles/Icons.axaml по ключу x:Key.
    private static Geometry? Icon(string key) =>
        Application.Current!.TryFindResource(key, out var res) && res is Geometry g ? g : null;

    public LibrarySidebarRow(LibraryRowKind kind, int id, string name, int depth)
    {
        Kind  = kind;
        Id    = id;
        Name  = name;
        Depth = depth;
    }

    public LibraryRowKind Kind { get; private set; }
    public int Id { get; private set; }
    public string Name { get; private set; }
    public int Depth { get; private set; }
    public bool Expanded { get; set; }
    public IBrush? TagColor { get; set; }

    [ObservableProperty] private bool _hovered;
    [ObservableProperty] private bool _checked;
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _statusFailed;

    private bool _isFirstRow;
    public bool IsFirstRow
    {
        get => _isFirstRow;
        set { if (_isFirstRow == value) return; _isFirstRow = value;
              OnPropertyChanged(nameof(LineGridRow)); OnPropertyChanged(nameof(LineRowHeight)); }
    }

    /// <summary>Перенести строку на данные новой сборки: шаблон не строится заново.</summary>
    public void Rebind(LibrarySidebarRow fresh)
    {
        Kind         = fresh.Kind;
        Id           = fresh.Id;
        Name         = fresh.Name;
        Depth        = fresh.Depth;
        Expanded     = fresh.Expanded;
        TagColor     = fresh.TagColor;
        Checked      = fresh.Checked;
        Selected     = fresh.Selected;
        Status       = fresh.Status;
        StatusFailed = fresh.StatusFailed;
        _isFirstRow  = fresh.IsFirstRow;
        OnPropertyChanged(string.Empty);   // все свойства разом — привязки строки перечитываются
    }

    public bool   HasStatus   => Status.Length > 0;
    public IBrush StatusBrush => StatusFailed ? Brush("BrushRed", "#FF3B30") : Brush("BrushSecondaryText", "#8E8E93");

    // Контент строки = высота ячейки таблицы (RowHeightTable); строка со статусом растет.
    public double RowContentHeight => HasStatus ? double.NaN : CellHeight;
    public int        LineGridRow   => IsFirstRow ? 0 : 1;
    public GridLength LineRowHeight => IsFirstRow ? new GridLength(0) : GridLength.Auto;

    private static double CellHeight =>
        Application.Current!.TryFindResource("RowHeightTable", out var v) && v is double d ? d : 36;

    private bool Expandable => Kind is LibraryRowKind.Group or LibraryRowKind.Tags;
    private bool Checkable  => Kind == LibraryRowKind.Source;

    public Thickness IndentMargin => new(Depth * IndentStep, 0, 0, 0);
    public double    ChevronSlot  => Expandable ? IndentStep : 0;
    public double    CheckboxSlot => Checkable ? IndentStep : 0;
    public bool      ShowCheckbox => Checkable;
    public bool      ShowChevron  => Expandable;

    public Geometry? ChevronGeometry => Expanded ? Icon("IconChevronDown") : Icon("IconChevronRight");
    public Geometry? IconGeometry => Kind switch
    {
        LibraryRowKind.Group  => Icon("IconFolders"),
        LibraryRowKind.Source => Icon("IconFolder"),
        _                     => Icon("IconTag"),
    };

    // Галочка серая: у источника своего цвета нет.
    private static IBrush Grey => Brush("BrushSecondaryText", "#8E8E93");
    public IBrush CheckFill   => Checked ? Grey : Brushes.Transparent;
    public IBrush CheckBorder => Grey;

    // Иконка метки — ее цветом: метку узнают по цвету, как точку на карточке.
    public IBrush IconBrush => Kind == LibraryRowKind.Tag && TagColor is { } color ? color
        : Selected ? Brush("BrushAccent", "#007AFF") : Grey;
    public IBrush NameBrush => Selected ? Brush("BrushAccent", "#007AFF") : Brush("BrushPrimaryText", "#1C1C1E");

    public IBrush RowBackground =>
        Selected ? Brush("BrushAccentLight", "#E5F0FF") :
        Hovered  ? Brush("BrushSecondaryBg", "#F2F2F7") : Brushes.Transparent;

    partial void OnHoveredChanged(bool value) => OnPropertyChanged(nameof(RowBackground));
    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(RowContentHeight));
    }
    partial void OnStatusFailedChanged(bool value) => OnPropertyChanged(nameof(StatusBrush));
    partial void OnCheckedChanged(bool value) => OnPropertyChanged(nameof(CheckFill));
    partial void OnSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(IconBrush));
        OnPropertyChanged(nameof(NameBrush));
    }
}

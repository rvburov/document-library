using Avalonia.Controls;
using PdfLibrary.Controls.Behaviors;
using PdfLibrary.Core.Library;
using Serilog;
using PdfLibrary.Views.Library;

namespace PdfLibrary.Controls.Components;

/// <summary>
/// Навбар ленты: заголовок слева, у правого края порядок,
/// «Тип документа» и кнопка панели чтения.
///
/// Строки поиска здесь нет намеренно: она стоит блоком над самой лентой (DocListView). Навбар
/// держит то, что относится к разделу целиком, а поиск переписывает ленту и потому живет над ней.
///
/// Выпадашка типов не своя, а общий <see cref="ColumnFilterPanel"/> — чеклист с поиском
/// значений, количествами справа и кнопкой «Готово».
/// </summary>
public partial class NavbarLibrary : UserControl
{
    /// <summary>Нажали toggle правой панели чтения: true — открыть, false — закрыть.</summary>
    public event EventHandler<bool>? ReaderToggled;

    private LibraryViewModel? _vm;

    /// <summary>Открытая панель типов — по ней узнаем, чье закрытие гасит подсветку кнопки.</summary>
    private Flyout? _typesFlyout;

    private bool _syncSort;

    public NavbarLibrary()
    {
        InitializeComponent();
        TypeBtn.Click += (_, _) => ShowTypes();
        SortSelector.ItemsSource = FeedSort.Options.Select(o => o.Label).ToList();
        SortSelector.SelectionChanged += (_, _) =>
        {
            if (!_syncSort && _vm is not null && SortSelector.SelectedIndex >= 0)
                _vm.Sort = FeedSort.Options[SortSelector.SelectedIndex].Key;
        };

        ReaderBtn.IsCheckedChanged += (_, _) =>
        {
            UpdateReaderIcon();
            ReaderToggled?.Invoke(this, ReaderBtn.IsChecked == true);
        };
    }

    public void Attach(LibraryViewModel vm)
    {
        _vm = vm;
        SyncSort();
        UpdateTypeLabel();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.Sort)) SyncSort();
            else if (e.PropertyName == nameof(LibraryViewModel.TypeLabel)) UpdateTypeLabel();
        };
    }

    /// <summary>
    /// Отразить состояние панели чтения на кнопке: панель открывают и кнопкой, и «Прочитать»
    /// в карточке, и кнопка обязана показывать правду в обоих случаях.
    ///
    /// Присваивание <c>IsChecked</c> ПОДНИМАЕТ <c>IsCheckedChanged</c>, а с ним и
    /// <c>ReaderToggled</c>: эхо гасит тот, кто знает правду, — обработчик окна сверяется с
    /// состоянием самой панели.
    /// </summary>
    public void SetReaderOpen(bool isOpen)
    {
        if (ReaderBtn.IsChecked == isOpen) return;
        ReaderBtn.IsChecked = isOpen;   // вызовет IsCheckedChanged → UpdateReaderIcon
    }

    private void SyncSort()
    {
        if (_vm is null) return;
        _syncSort = true;
        SortSelector.SelectedIndex = Math.Max(0, Array.FindIndex(FeedSort.Options, o => o.Key == _vm.Sort));
        _syncSort = false;
    }

    private void UpdateReaderIcon()
    {
        var open = ReaderBtn.IsChecked == true;
        ReaderOpenIcon.IsVisible  = !open;
        ReaderCloseIcon.IsVisible = open;
        ToolTip.SetTip(ReaderBtn, open ? "Закрыть панель чтения" : "Открыть панель чтения");
    }

    private void ShowTypes()
    {
        if (_vm is not { } vm) return;

        var flyout = ColumnFilterPanel.Show(this, TypeBtn, new ColumnFilterSpec
        {
            Key        = "docType",
            AscLabel   = "",
            DescLabel  = "",
            Sortable   = false,
            Filter     = vm.ChosenTypes,
            Display    = raw => raw,
            LoadValues = () => Task.FromResult(vm.TypeCounts()),
            LoadFailed = error => Log.Error(error, "Не удалось собрать типы документов"),
            Apply      = result =>
            {
                if (result.FilterKnown) vm.SetTypes(result.Filter);
            },
        });

        // Пока панель открыта, кнопка держит синюю рамку — ровно как раскрытый ComboBox.
        // Штатный :flyout-open тут не работает: флайаут не присвоен Button.Flyout, а
        // показывается ShowAt, поэтому состояние отмечаем классом сами.
        //
        // Сверка с _typesFlyout нужна из-за клика по самой кнопке при открытой панели: она
        // сперва закрывается по клику мимо, и без сверки «свое» закрытие сняло бы подсветку
        // уже со следующей открытой панели.
        _typesFlyout = flyout;
        if (!TypeBtn.Classes.Contains("open")) TypeBtn.Classes.Add("open");
        flyout.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_typesFlyout, flyout)) return;
            _typesFlyout = null;
            TypeBtn.Classes.Remove("open");
        };
    }

    // Подпись кнопки говорит состояние фильтра: снят — название, сужен — сколько типов выбрано.
    private void UpdateTypeLabel() => TypeLabel.Text = _vm?.TypeLabel ?? "Тип документа";
}

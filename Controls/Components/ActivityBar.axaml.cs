using Avalonia.Controls;
using Avalonia.Media;

namespace PdfLibrary.Controls.Components;

/// <summary>
/// Панель разделов: кнопка боковой
/// панели, разделы «Библиотека» и «Настройки», значок фоновой работы. Разделов в программе два,
/// других в программе нет.
/// </summary>
public partial class ActivityBar : UserControl
{
    public event EventHandler<bool>? SidebarToggled;
    public event EventHandler<string>? ModuleChanged;

    /// <summary>Нажали значок фоновой работы — показать ход чтения.</summary>
    public event EventHandler? WorkClicked;

    public ActivityBar()
    {
        InitializeComponent();

        WorkBtn.Click += (_, _) => WorkClicked?.Invoke(this, EventArgs.Empty);

        SidebarBtn.IsCheckedChanged += (_, _) =>
        {
            UpdateSidebarIcon();
            SidebarToggled?.Invoke(this, SidebarBtn.IsChecked == true);
        };

        LibraryBtn.Click  += (_, _) => SwitchModule("library");
        SettingsBtn.Click += (_, _) => SwitchModule("settings");
        SetModuleActive("library");
        UpdateWork(0, 0, failed: false);
    }

    /// <summary>
    /// Показать или спрятать значок и перерисовать кольцо. Дуга рисуется штриховкой круга:
    /// StrokeDashArray в Avalonia задается в ТОЛЩИНАХ обводки, поэтому длина окружности
    /// переводится в них делением на толщину. <paramref name="total"/> 0 — значка нет.
    /// </summary>
    public void UpdateWork(int done, int total, bool failed, string tip = "")
    {
        WorkBtn.IsVisible = total > 0;
        if (total <= 0) return;
        if (tip.Length > 0) ToolTip.SetTip(WorkBtn, tip);

        WorkFailedDot.IsVisible = failed;
        // TryFindResource с запасным значением, а не FindResource: метод зовется и из
        // конструктора, когда панель еще не в дереве, — там поиск вернул бы UnsetValue.
        WorkArc.Stroke = this.TryFindResource(failed ? "BrushRed" : "BrushAccent", out var brush)
                         && brush is IBrush b
            ? b
            : new SolidColorBrush(Color.Parse(failed ? "#FF3B30" : "#007AFF"));

        const double Radius    = 14.5;   // половина стороны 29 из разметки
        const double Thickness = 2;
        var units = 2 * Math.PI * Radius / Thickness;
        var part  = Math.Clamp((double)done / total, 0, 1);
        WorkArc.StrokeDashArray = [units * part, units * (1 - part)];
    }

    public void SetSidebarOpen(bool isOpen)
    {
        SidebarBtn.IsChecked = isOpen;
        UpdateSidebarIcon();
    }

    /// <summary>Кнопка боковой панели доступна: на экране первого запуска сворачивать нечего.</summary>
    public void SetSidebarEnabled(bool enabled) => SidebarBtn.IsEnabled = enabled;

    // Открыт → panel-right-open (←, свернуть); закрыт → panel-left-open (→, развернуть)
    private void UpdateSidebarIcon()
    {
        var open = SidebarBtn.IsChecked == true;
        PanelOpenIcon.IsVisible  = !open;
        PanelCloseIcon.IsVisible = open;
        ToolTip.SetTip(SidebarBtn, open ? "Свернуть боковую панель" : "Развернуть боковую панель");
    }

    public void SetModuleActive(string module)
    {
        LibraryBtn.IsChecked  = module == "library";
        SettingsBtn.IsChecked = module == "settings";
    }

    private void SwitchModule(string module)
    {
        SetModuleActive(module);
        ModuleChanged?.Invoke(this, module);
    }
}

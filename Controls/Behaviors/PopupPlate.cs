using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace PdfLibrary.Controls.Behaviors;

/// <summary>
/// Плашка выпадающей панели — одна на все чеклисты программы («Тип документа», метки
/// выбранных документов). Одно место для чисел подгонки: правка вида — одна.
///
/// Вертикальный зазор задает не отсюда, а стиль <c>FlyoutPresenter.filterFlyout</c>
/// (Assets/Styles/Controls.axaml): он часть вида плашки, а не ее позиции.
/// </summary>
public static class PopupPlate
{
    /// <summary>
    /// Сдвиг вправо относительно якоря. Раскладка выравнивает правые края точно, но попап-окно
    /// на macOS всегда
    /// получает системную тень (отключить нельзя, <c>SetWindowManagerAddShadowHint</c> там
    /// пустой), и на глаз она уводит край плашки внутрь. Число подобрано по живому экрану;
    /// менять его — здесь, одной правкой на все панели.
    /// </summary>
    public const double Nudge = 2;

    /// <summary>Пустой флайаут-плашка у нижнего края якоря, выровненный по его правому краю.</summary>
    public static Flyout Filter()
    {
        var flyout = new Flyout
        {
            Placement        = PlacementMode.BottomEdgeAlignedRight,
            HorizontalOffset = Nudge,
        };
        flyout.FlyoutPresenterClasses.Add("filterFlyout");
        return flyout;
    }
}

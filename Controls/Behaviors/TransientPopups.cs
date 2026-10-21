using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PdfLibrary.Controls.Behaviors;

/// <summary>
/// Учет открытых всплывающих панелей, которые обязаны исчезнуть, когда окно теряет фокус.
///
/// <b>Зачем.</b> Флайаут рендерится отдельным окном поверх всех остальных. На macOS оно
/// остается висеть над чужой программой, если его не закрыть (ошибка Avalonia) — человек
/// переключился в другое окно, а поверх него стоит наш фильтр.
///
/// <b>Почему реестр, а не обход дерева.</b> Панель отмечается одной строкой в месте показа, а
/// окно закрывает все разом и ничего не ищет — новая панель не заводит дыру молча.
///
/// Всплывашки-уведомления (<c>Toast</c>) сюда не попадают намеренно: они не забирают фокус,
/// живут секунды и гаснут по своему таймеру, а закрытие «на деактивации» съедало бы сообщение
/// при открытии собственного модального окна — на macOS оно тоже приходит как потеря фокуса.
///
/// Все вызовы — с UI-потока, синхронизация не нужна.
/// </summary>
public static class TransientPopups
{
    private static readonly List<FlyoutBase> Open = [];

    /// <summary>
    /// Отметить показанную панель. Снимать с учета не нужно — реестр сам забывает ее по
    /// закрытию, чьим бы оно ни было (клик мимо, «Готово», Esc, деактивация окна).
    /// </summary>
    public static void Track(FlyoutBase flyout)
    {
        if (Open.Contains(flyout)) return;
        Open.Add(flyout);
        flyout.Closed += Forget;
    }

    private static void Forget(object? sender, EventArgs e)
    {
        if (sender is not FlyoutBase flyout) return;
        flyout.Closed -= Forget;
        Open.Remove(flyout);
    }

    /// <summary>Закрыть все открытые панели. Зовет окно при потере фокуса.</summary>
    public static void CloseAll()
    {
        // Идем по копии: Hide() поднимает Closed, а тот правит сам список.
        foreach (var flyout in Open.ToList()) flyout.Hide();
        // Подчищаем на случай, если чей-то Closed не пришел: реестр не должен копить
        // ссылки на панели, которых уже нет.
        Open.Clear();
    }

    /// <summary>Сколько панелей сейчас на учете. Для тестов.</summary>
    public static int Count => Open.Count;
}

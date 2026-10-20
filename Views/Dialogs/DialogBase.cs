using Avalonia.Controls;

namespace PdfLibrary.Views.Dialogs;

/// <summary>
/// Содержимое панели-диалога поверх главного окна (макет: «Добавить источник», «Новая группа
/// источников», «Новая метка»). Рамку, заголовок и крестик рисует <see cref="DialogHost"/>, а
/// диалог дает свое тело и закрывается <see cref="Close"/> с результатом.
/// </summary>
public abstract class DialogBase : UserControl
{
    private TaskCompletionSource<object?>? _done;

    /// <summary>Заголовок панели.</summary>
    public abstract string Title { get; }

    internal Task<object?> Run()
    {
        _done = new TaskCompletionSource<object?>();
        return _done.Task;
    }

    /// <summary>Закрыть с результатом; null — «Отмена» или крестик.</summary>
    public void Close(object? result = null) => _done?.TrySetResult(result);

    /// <summary>Фокус на первое поле после показа.</summary>
    public virtual void OnShown() { }
}

namespace PdfLibrary.Core.Documents;

/// <summary>
/// Что известно про текст документа — им подписан серый чип карточки и считаются числа в строке
/// состояния поиска. Приходит из кеша <c>library-cache.db</c> (<c>DocTextIndex.Survey</c>).
/// </summary>
public enum DocTextState
{
    /// <summary>Кеш еще не спрашивали — чипа нет.</summary>
    Unknown = 0,
    /// <summary>Документ не прочитан или изменился после чтения: внутри него поиск пока не ищет.</summary>
    NotRead,
    /// <summary>Текст прочитан — поиск по содержимому работает.</summary>
    Read,
    /// <summary>Прочитан, но текста нет: скан без текстового слоя. Перечитывание не поможет.</summary>
    NoText,
    /// <summary>Не PDF — ищется только по названию.</summary>
    Unsupported,
    /// <summary>Прочитать не удалось: файл битый, занят, защищен паролем или тяжелее порога.</summary>
    Failed,
}

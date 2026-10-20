namespace PdfLibrary.Utils;

/// <summary>
/// Оценка оставшегося времени долгой операции: линейная экстраполяция по доле
/// выполненного, сглаженная экспоненциальным средним. Формулировка — как в макете: «осталось
/// около 6 минут»; она одна везде, где программа чем-то занята.
///
/// Сглаживание нужно из-за рваной скорости: первые страницы читаются из холодного кеша,
/// дальше быстрее — «сырая» экстраполяция скакала бы с «20 минут» на «2 минуты» и обратно.
/// Класс не знает про часы: время передает вызывающий (<see cref="System.Diagnostics.Stopwatch"/>
/// в бою, фиксированные значения в тестах).
/// </summary>
public sealed class EtaEstimator(double smoothing = 0.3)
{
    /// <summary>Пока сделано меньше — оценка мусорная (старт всегда медленнее).</summary>
    private const double MinFraction = 0.02;

    /// <summary>И пока прошло меньше секунды: делить на околонулевое время бессмысленно.</summary>
    private const double MinSeconds = 1.0;

    private double _ema = -1;

    /// <summary>
    /// Обновить оценку. <paramref name="fraction"/> — доля выполненного (0..1).
    /// Возвращает готовую строку («осталось около 3 минут») или null, если оценивать рано
    /// либо работа уже закончена — в этом случае строку просто не показывают.
    /// </summary>
    public string? Update(double fraction, TimeSpan elapsed)
    {
        if (fraction < MinFraction || fraction >= 1 || elapsed.TotalSeconds < MinSeconds) return null;

        var raw = elapsed.TotalSeconds * (1 - fraction) / fraction;
        _ema = _ema < 0 ? raw : _ema * (1 - smoothing) + raw * smoothing;
        return Format(_ema);
    }

    /// <summary>Секунды → фраза для инфо-строки прогресса.</summary>
    public static string Format(double remainingSeconds)
    {
        if (remainingSeconds < 50) return "осталось меньше минуты";
        var minutes = (int)Math.Ceiling(remainingSeconds / 60);
        if (minutes < 60) return $"осталось около {minutes} {Plural.GenitiveMinutes(minutes)}";
        if (minutes < 90) return "осталось около часа";
        var hours = (int)Math.Round(minutes / 60.0);
        return $"осталось около {hours} {Plural.GenitiveHours(hours)}";
    }
}

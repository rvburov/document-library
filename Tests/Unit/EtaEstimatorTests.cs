using PdfLibrary;
using PdfLibrary.Utils;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Оценка оставшегося времени (<see cref="EtaEstimator"/>) для долгих операций —
/// выгрузки CSV и монтирования баз. Класс не смотрит на часы: время подает вызывающий,
/// поэтому поведение проверяется точно, без ожиданий и таймеров.
/// </summary>
public sealed class EtaEstimatorTests
{
    [Fact]
    public void No_estimate_until_there_is_something_to_extrapolate()
    {
        var eta = new EtaEstimator();

        Assert.Null(eta.Update(0, TimeSpan.FromSeconds(10)));            // ничего не сделано
        Assert.Null(eta.Update(0.01, TimeSpan.FromSeconds(10)));         // меньше порога 2%
        Assert.Null(eta.Update(0.5, TimeSpan.FromMilliseconds(200)));    // прошло меньше секунды
        Assert.Null(eta.Update(1, TimeSpan.FromSeconds(10)));            // уже готово
    }

    /// <summary>Половина за минуту → минута до конца: «около 1 минуты».</summary>
    [Fact]
    public void Half_way_extrapolates_the_remaining_half()
    {
        var eta = new EtaEstimator();
        Assert.Equal("осталось около 1 минуты", eta.Update(0.5, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Less_than_a_minute_is_phrased_without_numbers()
    {
        var eta = new EtaEstimator();
        // 90% за 90 секунд → ~10 секунд остатка.
        Assert.Equal("осталось меньше минуты", eta.Update(0.9, TimeSpan.FromSeconds(90)));
    }

    /// <summary>
    /// Скачок скорости не должен выбрасывать оценку целиком: сглаживание тянет ее
    /// к новому значению постепенно, иначе строка прыгала бы «20 мин → 2 мин → 20 мин».
    /// </summary>
    [Fact]
    public void Smoothing_damps_a_sudden_speed_change()
    {
        var eta = new EtaEstimator(smoothing: 0.3);

        // Ровный ход: 10% за 100 с → остаток 900 с (15 мин).
        Assert.Equal("осталось около 15 минут", eta.Update(0.1, TimeSpan.FromSeconds(100)));

        // Резкое ускорение: сырая оценка стала 100 с, но показанная — между старой и новой.
        var text = eta.Update(0.5, TimeSpan.FromSeconds(100));
        Assert.Equal("осталось около 11 минут", text);   // 0.7*900 + 0.3*100 = 660 с

        // Дальше оценка продолжает сходиться к быстрой: 0.7*660 + 0.3*100 = 492 с.
        Assert.Equal("осталось около 9 минут", eta.Update(0.5, TimeSpan.FromSeconds(100)));
    }

    [Theory]
    [InlineData(0, "осталось меньше минуты")]
    [InlineData(49, "осталось меньше минуты")]
    [InlineData(50, "осталось около 1 минуты")]
    [InlineData(61, "осталось около 2 минут")]
    [InlineData(360, "осталось около 6 минут")]
    [InlineData(1260, "осталось около 21 минуты")]
    [InlineData(3600, "осталось около часа")]
    [InlineData(7200, "осталось около 2 часов")]
    [InlineData(75600, "осталось около 21 часа")]
    public void Format_matches_the_mockup_wording(double seconds, string expected)
    {
        Assert.Equal(expected, EtaEstimator.Format(seconds));
    }
}

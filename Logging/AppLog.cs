using Serilog;
using Serilog.Events;
using PdfLibrary.Utils;

namespace PdfLibrary.Logging;

/// <summary>
/// Диагностический лог приложения: Serilog пишет структурированные записи (уровень,
/// сообщение, исключение) в отдельную базу <c>logs.db</c> рядом с library.db (см.
/// <see cref="AppSettings.LogsDbPath"/>). Файл один на программу — поэтому логгер статический
/// (как AppSettings), DI не используется.
///
/// Читают журнал в программе — «Настройки → Диагностика» (<see cref="LogReader"/>), снаружи —
/// любой программой просмотра SQLite. crash.log (<see cref="CrashLog"/>) остается независимым
/// резервом на случай сбоя до или после инициализации Serilog.
///
/// Таблица <c>logs</c> создается приемником автоматически при первой записи; столбцы —
/// Timestamp/Level/RenderedMessage/Exception/Properties (схему задает Serilog.Sinks.SQLite,
/// ее же читает Logging/LogReader).
/// </summary>
public static class AppLog
{
    // Внутренние ошибки приемника за сеанс — не больше стольких строк в crash.log: сбой
    // приемника (диск полон, файл занят) повторяется на каждой записи и залил бы резерв.
    private const int SelfLogLimit = 20;
    private static int _selfLogCount;

    /// <summary>Настроить и запустить глобальный логгер. Вызывать один раз на старте
    /// (Program.Main), после создания папки данных (AppSettings.EnsureCreated).</summary>
    public static void Init()
    {
        // Внутренние ошибки самого Serilog (logs.db заблокирован, поврежден, диск полон) иначе
        // теряются молча — журнал выглядит пустым ровно тогда, когда нужен. Направляем их в
        // независимый crash.log (см. CrashLog), чтобы отказ приемника был виден хоть где-то.
        Serilog.Debugging.SelfLog.Enable(message =>
        {
            if (IsSinkChatter(message)) return;
            if (Interlocked.Increment(ref _selfLogCount) <= SelfLogLimit)
                CrashLog.Append("serilog", message);
        });

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.SQLite(
                sqliteDbPath: AppSettings.LogsDbPath,
                tableName: "logs",
                restrictedToMinimumLevel: LogEventLevel.Information,
                storeTimestampInUtc: true,
                // batchSize=1: писать каждую запись сразу (~0.1 c), а не копить пачкой по
                // 100 или ждать таймер сброса ~10 c. Для диагностики объем мал — эффективность
                // пакетов не нужна, зато свежие ошибки видны в «Диагностике» мгновенно и
                // почти не теряются при аварийном завершении (окно до сброса ничтожно).
                batchSize: 1,
                // Автоочистка: записи старше 30 дней приемник удаляет сам — logs.db не разрастется.
                retentionPeriod: TimeSpan.FromDays(30))
            .CreateLogger();
    }

    /// <summary>
    /// Отчеты приемника SQLite о штатной работе: через тот же SelfLog он сообщает о каждой
    /// записанной пачке («Sending batch of 1 logs») и об автоочистке («0 records deleted»). Это не
    /// сбои: при пачке в одну запись они заняли бы весь лимит crash.log за первые секунды.
    /// </summary>
    internal static bool IsSinkChatter(string message) =>
        message.Contains("Sending batch of", StringComparison.Ordinal) ||
        message.Contains("records deleted", StringComparison.Ordinal) ||
        message.Contains("Deleting log entries older than", StringComparison.Ordinal);

    /// <summary>Дописать буфер и закрыть приемник при выходе (Program.Main, finally).</summary>
    public static void Shutdown() => Log.CloseAndFlush();
}

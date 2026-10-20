using PdfLibrary.Utils;

namespace PdfLibrary.Logging;

/// <summary>
/// Независимый резерв диагностики — текстовый <c>crash.log</c> рядом с данными приложения.
/// Работает без Serilog и до него: сбой на старте, падение с фонового потока, когда буфер
/// приемника еще не сброшен, и отказ самого приемника (<c>logs.db</c> заблокирован или
/// поврежден — тогда Serilog молча роняет записи, и журнал выглядит пустым ровно тогда,
/// когда нужен; его внутренние ошибки <see cref="AppLog"/> направляет сюда через SelfLog).
/// Дозапись, ротации нет; время локальное (приемник Serilog пишет UTC — учитывать при сверке).
/// </summary>
public static class CrashLog
{
    public static string Path => System.IO.Path.Combine(AppSettings.AppDataDir, "crash.log");

    // Дозапись File.AppendAllText — «перейти в конец и записать»: две записи из разных потоков
    // разом затирают друг друга, и в файле остаются обрывки строк.
    private static readonly Lock Gate = new();

    /// <summary>Дописать запись. Best-effort: сбой записи не должен маскировать исходную ошибку.</summary>
    public static void Append(string source, string text)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source} v{AppSettings.AppVersion}{Environment.NewLine}" +
                        $"{text}{Environment.NewLine}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.AppDataDir);
                File.AppendAllText(Path, entry);
            }
        }
        catch { /* некуда писать — значит, некуда */ }
    }
}

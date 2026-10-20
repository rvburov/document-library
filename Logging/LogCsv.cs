using System.Text;

namespace PdfLibrary.Logging;

/// <summary>
/// Выгрузка журнала в CSV для обращения в поддержку: UTF-8 с меткой порядка байт и точка с
/// запятой между полями — так файл открывается табличным редактором с русскими настройками без
/// мастера импорта. Значения с разделителем, кавычкой или переводом строки берутся в кавычки.
/// </summary>
public static class LogCsv
{
    /// <summary>Записать заголовок и строки; возвращает число строк.</summary>
    public static int Write(string path, IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(';', LogReader.CsvHeaders.Select(Quote)));
        var count = 0;
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(';', row.Select(Quote)));
            count++;
        }
        return count;
    }

    /// <summary>Поле CSV: в кавычках, если внутри разделитель, кавычка или перевод строки.</summary>
    public static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}

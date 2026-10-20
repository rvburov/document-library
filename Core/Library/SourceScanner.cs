using System.Diagnostics;
using System.IO.Enumeration;

namespace PdfLibrary.Core.Library;

/// <summary>PDF, найденный в папке источника: путь внутри нее, размер и время правки (тики UTC).</summary>
public readonly record struct ScannedFile(string RelativePath, long Size, long Mtime);

/// <summary>Итог проверки папки.</summary>
/// <param name="Available">Папка ответила. <c>false</c> — недоступна: сервер выключен, сеть
/// отвалилась, папку удалили; документы источника тогда НЕ помечаются пропавшими.</param>
/// <param name="Complete">Обход дошел до конца. Прерванный (отмена, сбой на середине) не дает
/// права считать пропавшим то, до чего не дошел.</param>
public sealed record ScanResult(bool Available, bool Complete, List<ScannedFile> Files, string? Error,
                                TimeSpan Elapsed);

/// <summary>
/// Проверка папки источника: какие PDF в ней лежат сейчас. Обход рекурсивный, со всеми
/// подпапками; время правки и размер приходят вместе с листингом папки, к каждому файлу
/// отдельно диск не спрашивается — на сетевой папке это разница в разы.
///
/// Скрытые и системные файлы пропускаются (корзины, служебные копии), как и файлы по маскам
/// «Пропускать файлы» (<c>~$*.pdf</c> — временные копии открытых документов). Папки без прав
/// чтения пропускаются молча: человеку важны документы, до которых он может добраться.
/// </summary>
public static class SourceScanner
{
    /// <summary>Глубина вложенности папок. Защита от петель ссылок на папки, которые обход иначе
    /// проходил бы бесконечно.</summary>
    public const int MaxDepth = 64;

    /// <summary>Маски «Пропускать файлы» из строки настройки: через запятую или точку с запятой.</summary>
    public static IReadOnlyList<string> ParseMasks(string? text) =>
        (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(m => m.Length > 0)
                    .ToList();

    /// <summary>Пропускается ли файл по маскам (сравнение имени без учета регистра).</summary>
    public static bool Skipped(string fileName, IReadOnlyList<string> masks)
    {
        foreach (var mask in masks)
            if (FileSystemName.MatchesSimpleExpression(mask, fileName, ignoreCase: true)) return true;
        return false;
    }

    public static ScanResult Scan(string root, IReadOnlyList<string> skipMasks, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var files = new List<ScannedFile>();

        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return new ScanResult(false, false, files, "папка не найдена", watch.Elapsed);
        }
        catch (Exception ex)
        {
            return new ScanResult(false, false, files, ex.Message, watch.Elapsed);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible    = true,
            MatchCasing           = MatchCasing.CaseInsensitive,
            MatchType             = MatchType.Simple,
            MaxRecursionDepth     = MaxDepth,
            // Скрытое и системное — как по умолчанию; символьные ссылки не пропускаем: облачные
            // папки отдают файлы именно ими, и такой документ должен попасть в ленту.
            AttributesToSkip      = FileAttributes.Hidden | FileAttributes.System,
        };

        try
        {
            var full = Path.GetFullPath(root);
            foreach (var file in new DirectoryInfo(full).EnumerateFiles("*.pdf", options))
            {
                ct.ThrowIfCancellationRequested();
                if (Skipped(file.Name, skipMasks)) continue;

                var relative = Path.GetRelativePath(full, file.FullName).Replace('\\', '/');
                // FileInfo из перечисления уже заполнен: длина и время пришли листингом.
                files.Add(new ScannedFile(relative, file.Length, file.LastWriteTimeUtc.Ticks));
            }
            return new ScanResult(true, true, files, null, watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new ScanResult(true, false, files, "проверка остановлена", watch.Elapsed);
        }
        catch (Exception ex)
        {
            // Сеть отвалилась посреди обхода: найденное верно, но список неполон, и пропавшим
            // ничего не считается (Complete = false).
            return new ScanResult(true, false, files, ex.Message, watch.Elapsed);
        }
    }
}

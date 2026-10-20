using System.Diagnostics;
using PdfLibrary.Utils;
using Serilog;

namespace PdfLibrary.Services;

/// <summary>
/// Действия с файлами документов: открыть в другой программе («Открыть»), показать в папке,
/// скопировать выбранные в папку. Сами PDF программа не меняет никогда.
/// </summary>
public static class DocActions
{
    /// <summary>
    /// Отдать PDF программе для печати, правки или подписи: выбранной в «Общем» или той, что
    /// открывает PDF в системе. Возвращает текст ошибки или null.
    /// </summary>
    public static string? OpenExternal(string path)
    {
        try
        {
            if (!File.Exists(path)) return "Файл не найден: " + path;
            var program = AppSettings.OpenWith;
            if (program.Length > 0)
            {
                var psi = new ProcessStartInfo(program) { UseShellExecute = false };
                psi.ArgumentList.Add(path);
                Process.Start(psi)?.Dispose();
            }
            else ShellOpen(path);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Документ не открылся в другой программе: {Path:l}", path);
            return $"Не удалось открыть файл: {ex.Message}";
        }
    }

    /// <summary>Открыть папку в проводнике (Finder, файловом менеджере); у файла — с выделением файла.</summary>
    public static string? ShowInFolder(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    psi.ArgumentList.Add("/select," + path);
                    Process.Start(psi)?.Dispose();
                    return null;
                }
                if (OperatingSystem.IsMacOS())
                {
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add("-R");
                    psi.ArgumentList.Add(path);
                    Process.Start(psi)?.Dispose();
                    return null;
                }
                path = Path.GetDirectoryName(path) ?? path;
            }
            if (!Directory.Exists(path)) return "Папка недоступна: " + path;
            ShellOpen(path);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Папка не открылась: {Path:l}", path);
            return $"Не удалось открыть папку: {ex.Message}";
        }
    }

    private static void ShellOpen(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return;
        }
        var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>
    /// Скопировать файлы в папку. Тезка в папке не перезаписывается: копия получает имя
    /// «Отчет (2).pdf». Возвращает, сколько скопировано, и ошибки по файлам.
    /// </summary>
    public static (int Copied, List<string> Errors) CopyTo(IReadOnlyCollection<string> files, string folder,
                                                          IProgress<double>? progress = null,
                                                          CancellationToken ct = default)
    {
        var copied = 0;
        var errors = new List<string>();
        var done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Copy(file, FreeName(folder, Path.GetFileName(file)));
                copied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
            progress?.Report(++done / (double)files.Count);
        }
        if (errors.Count > 0)
            Log.Warning("Копирование в папку {Folder:l}: скопировано {Copied}, не вышло {Failed}: {First:l}",
                        folder, copied, errors.Count, errors[0]);
        return (copied, errors);
    }

    /// <summary>Свободное имя в папке: «Отчет.pdf», занято — «Отчет (2).pdf», «Отчет (3).pdf»…</summary>
    public static string FreeName(string folder, string name)
    {
        var target = Path.Combine(folder, name);
        if (!File.Exists(target)) return target;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext  = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            target = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(target)) return target;
        }
    }
}

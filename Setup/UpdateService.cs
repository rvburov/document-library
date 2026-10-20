using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PdfLibrary.Utils;
using Serilog;

namespace PdfLibrary.Setup;

/// <summary>Что показала проверка папки обновлений.</summary>
public enum UpdateCheck { UpToDate, Available, Error }

/// <summary>Итог проверки: что сказать человеку и, если есть обновление, откуда его брать.</summary>
public sealed record UpdateInfo(UpdateCheck Result, string Message, string? Artifact = null, Version? Remote = null);

/// <summary>
/// Обновление из папки — как в SmartBase: в общей папке сети лежит <c>PdfLibrary-Setup</c> (или
/// <c>PdfLibrary</c>) новой версии, программа сравнивает его со своей версией, копирует во
/// временную папку и запускает копию с <c>--apply-update</c>. Копия ждет выхода старого процесса,
/// заменяет файл программы собой и запускает программу заново (<see cref="Program"/>).
///
/// Сравнение: версия из файла (на Windows — из ресурса .exe); у нативных файлов Linux и macOS
/// версии в файле нет — тогда размер (другая сборка) и время правки (новее на сервере с допуском
/// 2 с). Так можно раздавать сборки без смены номера версии.
/// </summary>
public static class UpdateService
{
    /// <summary>Проверить папку обновлений (в фоне — сетевой путь может отвечать долго).</summary>
    public static Task<UpdateInfo> CheckAsync(string folder) => Task.Run(() => Check(folder));

    public static UpdateInfo Check(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
                return new(UpdateCheck.Error, "Укажите папку обновлений.");
            if (!Directory.Exists(folder))
                return new(UpdateCheck.Error, "Папка обновлений недоступна — проверьте путь и подключение к сети.");
            if (!ResolveRemote(folder, out var artifact, out var timeFile, out var missing))
                return new(UpdateCheck.Error, missing);

            var cur = Current;
            if (Version.TryParse(FileVersionInfo.GetVersionInfo(timeFile).FileVersion, out var remoteFull))
            {
                var remote = Normalize(remoteFull);
                if (remote > cur)
                    return new(UpdateCheck.Available, $"Доступна версия {remote} (у вас {cur}).", artifact, remote);
                if (remote < cur)
                    return new(UpdateCheck.UpToDate, $"Установлена последняя версия, {cur}");
            }

            // Версия недоступна или та же — ловим другую сборку по размеру и дате файла.
            var localExe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(localExe) && File.Exists(localExe))
            {
                var local  = new FileInfo(localExe);
                var remote = new FileInfo(timeFile);
                if (remote.Length != local.Length || remote.LastWriteTimeUtc > local.LastWriteTimeUtc.AddSeconds(2))
                    return new(UpdateCheck.Available, $"Доступна новая сборка версии {cur}.", artifact, cur);
            }
            return new(UpdateCheck.UpToDate, $"Установлена последняя версия, {cur}");
        }
        catch (Exception ex)
        {
            return new(UpdateCheck.Error, $"Не удалось проверить обновление: {ex.Message}");
        }
    }

    /// <summary>Текущая версия: Major.Minor.Build — как в «О программе».</summary>
    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    private static Version Normalize(Version v) => new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);

    // Файл обновления в папке: на Windows и Linux — файл, на macOS — .app-бандл и его внутренний
    // бинарь (по нему сравниваются размер и дата).
    private static bool ResolveRemote(string folder, out string artifact, out string timeFile, out string error)
    {
        artifact = timeFile = error = "";
        if (InstallService.IsMac)
        {
            var setupB = Path.Combine(folder, InstallService.SetupBundleName);
            var plainB = Path.Combine(folder, InstallService.AppBundleName);
            var bundle = Directory.Exists(setupB) ? setupB : Directory.Exists(plainB) ? plainB : null;
            if (bundle is null)
            {
                error = $"В папке нет {InstallService.SetupBundleName} (или {InstallService.AppBundleName}).";
                return false;
            }
            artifact = bundle;
            timeFile = InstallService.MacInnerBinary(bundle);
            return true;
        }

        var setup = Path.Combine(folder, InstallService.SetupExeName);
        var plain = Path.Combine(folder, InstallService.AppExeName);
        var file  = File.Exists(setup) ? setup : File.Exists(plain) ? plain : null;
        if (file is null)
        {
            error = $"В папке нет {InstallService.SetupExeName} (или {InstallService.AppExeName}).";
            return false;
        }
        artifact = timeFile = file;
        return true;
    }

    /// <summary>
    /// Скачать обновление во временную папку и запустить самозамену; программа после этого
    /// закрывается. Возвращает текст ошибки или null — замена запущена.
    /// </summary>
    public static async Task<string?> ApplyAsync(string artifact, IProgress<double>? progress = null)
    {
        var processPath = Environment.ProcessPath;
        var target = InstallService.IsMac ? InstallService.BundleRoot(processPath) : processPath;
        if (string.IsNullOrEmpty(target))
            return "Обновлять можно только установленную программу: путь к ней не определен.";

        var tempName = OperatingSystem.IsWindows() ? $"{AppSettings.AppName}.update.exe"
                     : InstallService.IsMac        ? $"{AppSettings.AppName}.update.app"
                     : $"{AppSettings.AppName}.update";
        var temp = Path.Combine(Path.GetTempPath(), tempName);

        try
        {
            if (InstallService.IsMac)
            {
                if (Directory.Exists(temp)) InstallService.DeleteRecursive(temp);
                await InstallService.CopyTreeAsync(artifact, temp, p => progress?.Report(p)).ConfigureAwait(false);
                InstallService.MakeExecutable(InstallService.MacInnerBinary(temp));
            }
            else
            {
                await CopyAsync(artifact, temp, progress).ConfigureAwait(false);
                InstallService.MakeExecutable(temp);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Не удалось загрузить обновление из {Source:l}", artifact);
            return $"Не удалось загрузить обновление: {ex.Message}";
        }

        // На Windows — версия из скачанного файла: не даем «обновиться» на файл старее текущего
        // (файл в папке мог смениться между проверкой и загрузкой).
        if (OperatingSystem.IsWindows()
            && Version.TryParse(FileVersionInfo.GetVersionInfo(temp).FileVersion, out var downloaded)
            && Normalize(downloaded) < Current)
            return "Скачанный файл оказался старее текущей версии — обновление отменено.";

        try
        {
            var helper = InstallService.IsMac ? InstallService.MacInnerBinary(temp) : temp;
            var psi = new ProcessStartInfo { FileName = helper, UseShellExecute = false };
            psi.ArgumentList.Add("--apply-update");
            psi.ArgumentList.Add("--target");
            psi.ArgumentList.Add(target);
            psi.ArgumentList.Add("--pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(psi)?.Dispose();
            Log.Information("Обновление начато: {Source:l} → {Target:l}, программа закрывается для самозамены",
                            artifact, target);
            Program.ExitReason = "перезапуск для обновления";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Не удалось запустить обновление из {Temp:l}", temp);
            return $"Не удалось запустить обновление: {ex.Message}";
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
        return null;
    }

    // Копирование с проверкой размера: оборванная копия с сетевой папки не должна стать программой.
    private static async Task CopyAsync(string source, string target, IProgress<double>? progress)
    {
        var total = new FileInfo(source).Length;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true))
        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                done += read;
                progress?.Report(total > 0 ? (double)done / total : 1);
            }
        }
        if (new FileInfo(target).Length != total)
            throw new IOException("файл скопирован не полностью");
    }

    /// <summary>
    /// Автообновление при старте: есть новая версия в папке — ставим без вопросов («Новая версия
    /// ставится сама при следующем запуске программы»). Нет папки или она молчит — тихо: старт не
    /// должен спотыкаться о выключенный сервер. След — в журнале.
    /// </summary>
    public static async Task CheckOnStartupAsync(Window owner)
    {
        var info = await CheckAsync(AppSettings.UpdateFolder).ConfigureAwait(false);
        if (info.Result == UpdateCheck.Error)
        {
            Log.Warning("Автообновление: {Message:l}", info.Message);
            return;
        }
        if (info.Result != UpdateCheck.Available || info.Artifact is null) return;

        // Устанавливается только установленная копия: запуск из папки сборки обновлять нечем.
        if (InstallService.IsRunningInstalledCopy() != true)
        {
            Log.Information("Автообновление: {Message:l} — пропущено, программа запущена не из папки установки",
                            info.Message);
            return;
        }

        Log.Information("Автообновление: {Message:l}", info.Message);
        var error = await ApplyAsync(info.Artifact).ConfigureAwait(false);
        if (error is not null) Log.Warning("Автообновление не выполнено: {Error:l}", error);
    }
}

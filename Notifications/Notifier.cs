using System.Diagnostics;
using Serilog;

namespace PdfLibrary.Notifications;

/// <summary>
/// Системные уведомления: «Новые документы в источниках» и «Чтение документов закончено»
/// (Настройки → Общее). Windows и Linux — библиотека DesktopNotifications (менеджер создает
/// <see cref="Program"/>), macOS — <c>osascript</c>: бэкенд библиотеки для macOS — заглушка.
///
/// Сбой уведомления программу не останавливает: баннер — вежливость, а не работа. След
/// остается в журнале, но не чаще раза за сеанс — иначе сломанный D-Bus писал бы строку на
/// каждое чтение.
/// </summary>
public static class Notifier
{
    private static bool _failedLogged;

    public static void Show(string title, string body)
    {
        try
        {
            if (Program.NotificationManager is { } manager)
            {
                _ = manager.ShowNotification(new DesktopNotifications.Notification { Title = title, Body = body });
                return;
            }
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("osascript") { UseShellExecute = false };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add($"display notification {AppleQuote(body)} with title {AppleQuote(title)}");
                Process.Start(psi)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (_failedLogged) return;
            _failedLogged = true;
            Log.Warning(ex, "Системное уведомление не показано");
        }
    }

    // Строка AppleScript: в двойных кавычках, внутренние кавычки и обратные слеши экранированы.
    private static string AppleQuote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

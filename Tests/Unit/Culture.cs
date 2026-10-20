using System.Globalization;
using System.Runtime.CompilerServices;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Обстановка тестов до первого обращения к программе:
/// <list type="bullet">
/// <item>русская культура, как у программы (<c>Program.Main</c> ставит ее сама): числа в
///       подписях — «3,1 МБ», «4 251 документ» — не должны зависеть от языка системы;</item>
/// <item>своя временная папка данных (<c>PDFLIBRARY_DATA</c>): тесты не пишут ни в настоящую
///       библиотеку, ни в .localdata отладочного запуска.</item>
/// </list>
/// </summary>
internal static class Culture
{
    [ModuleInitializer]
    internal static void Prepare()
    {
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = ru;
        CultureInfo.DefaultThreadCurrentUICulture = ru;
        CultureInfo.CurrentCulture = ru;
        CultureInfo.CurrentUICulture = ru;

        var data = Path.Combine(Path.GetTempPath(), "pl-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Environment.SetEnvironmentVariable(PdfLibrary.Utils.AppSettings.DataDirVariable, data);
    }
}

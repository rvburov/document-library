using System.Text.RegularExpressions;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Сторож правила «программе сеть не нужна». Сетевые папки — это файлы, их открывает файловая
/// система; ни клиентов HTTP, ни сокетов, ни разрешения имен в программе быть не должно.
///
/// Рубежей два. Анализатор BannedApiAnalyzers (<c>BannedSymbols.txt</c>, нарушение — ошибка
/// сборки) ловит тип, использованный без своего имени: через var, псевдоним, наследника. Этот
/// тест читает исходники и ловит то, что анализатор пропустит: снятие запрета строкой
/// <c>#pragma warning disable RS0030</c> и сетевые пространства имен в <c>using</c>.
/// </summary>
public sealed class NoNetworkTests
{
    private static readonly Regex Forbidden = new(
        @"#pragma\s+warning\s+disable\s+RS0030"
        + @"|\busing\s+System\.Net(?:\.Http|\.Sockets|\.WebSockets|\.Mail)?\s*;"
        + @"|\bSystem\.Net\.(?:Http|Sockets|WebSockets|Mail)\."
        + @"|\b(?:HttpClient|TcpClient|UdpClient|TcpListener|HttpListener|WebClient|ClientWebSocket)\b",
        RegexOptions.Compiled);

    [Fact]
    public void Program_code_has_no_network()
    {
        var offenders = RepoFiles.Sources(RepoFiles.Root())
            .SelectMany(s => s.Text.Split('\n').Select((line, i) => (s.Path, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && Forbidden.IsMatch(l.Text))
            .Select(l => $"{l.Path}:{l.Line}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "В коде программы появилась сеть или снят запрет RS0030: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Banned_symbols_list_covers_http_sockets_and_dns()
    {
        var text = File.ReadAllText(Path.Combine(RepoFiles.Root(), "BannedSymbols.txt"));
        foreach (var type in new[]
                 {
                     "System.Net.Http.HttpClient", "System.Net.Sockets.Socket", "System.Net.Sockets.TcpClient",
                     "System.Net.Dns", "System.Net.HttpListener", "System.Net.WebClient",
                 })
            Assert.Contains("T:" + type + ";", text);
        // Пустая строка или строка из одного «#» анализатор считает записью без символа (RS0031).
        Assert.DoesNotContain("\n\n", text.Replace("\r", ""));
    }
}

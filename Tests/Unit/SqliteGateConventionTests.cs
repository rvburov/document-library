using System.Text.RegularExpressions;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Сторож правила «подключение SQLite — только через <c>SqliteGate</c>»
/// (<c>Database/SqliteGate.cs</c>). Пул Microsoft.Data.Sqlite изредка отдает одно подключение
/// двум владельцам (dotnet/efcore#39008), и замок пула спасает, только если через него
/// открываются ВСЕ подключения этого пула и через него же пул чистят. Подключение мимо замка
/// снова пускает гонку, а тестом ее ловит лишь стресс на тысячах открытий
/// (<c>SqliteGateTests</c>), — поэтому правило проверяется по тексту, как
/// <see cref="PdfGateConventionTests"/>.
///
/// Мимо замка — это <c>new SqliteConnection(…)</c> (и с полным именем, и <c>SqliteConnection x =
/// new(…)</c>), очистка пула самим <c>SqliteConnection</c> и <c>UseSqlite</c> не с подключением
/// из <c>SqliteGate.NewConnection</c>: из строки EF создал бы подключение сам.
/// </summary>
public sealed class SqliteGateConventionTests
{
    /// <summary>Сам шлюз — единственное место, где подключения создаются и пулы чистятся
    /// напрямую. Tests/ не сканируется: тесты и стенды открывают файлы сами.</summary>
    private static readonly string GateFile = Path.Combine("Database", "SqliteGate.cs");

    private static readonly Regex Bypass = new(
        @"new\s+(?:Microsoft\.Data\.Sqlite\.)?SqliteConnection\s*\("          // new SqliteConnection(…)
        + @"|\bSqliteConnection\??\s+\w+\s*=\s*new\s*\("                      // SqliteConnection x = new(…)
        + @"|\bSqliteConnection\s*\.\s*Clear(?:All)?Pools?\s*\("               // SqliteConnection.ClearPool(…)
        + @"|UseSqlite\s*\((?!\s*SqliteGate\.NewConnection\s*\()",              // UseSqlite(строка)
        RegexOptions.Compiled);

    [Fact]
    public void Connections_are_made_only_through_the_gate()
    {
        var offenders = Sources(RepoRoot())
            .Where(s => !s.Path.Equals(GateFile, StringComparison.OrdinalIgnoreCase))
            .Where(s => Bypass.IsMatch(s.Text))
            .Select(s => s.Path)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Подключение SQLite создается или пул чистится мимо SqliteGate — так пул снова может " +
            "отдать одно подключение двум владельцам. Берите SqliteGate.NewConnection, " +
            "SqliteGate.ClearPool и SqliteGate.ClearAllPools: " + string.Join(", ", offenders));
    }

    private static string RepoRoot() => RepoFiles.Root();

    private static IEnumerable<(string Path, string Text)> Sources(string root) => RepoFiles.Sources(root);
}

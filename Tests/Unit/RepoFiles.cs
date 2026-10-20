namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Исходники программы для тестов-правил: они читают код как текст и проверяют то, чего
/// компилятор не видит (все вызовы PDFium через замок, подключения SQLite через шлюз, ни одного
/// сетевого типа, ни одной буквы «е с точками» в текстах).
/// </summary>
internal static class RepoFiles
{
    /// <summary>Папки с кодом программы (тесты, кеш пакетов и вывод сборки сюда не входят).</summary>
    public static readonly string[] SourceDirs =
        ["Controls", "Core", "Database", "Logging", "Notifications", "Services", "Setup", "Utils", "Views"];

    /// <summary>Корень репозитория — папка с PdfLibrary.sln.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdfLibrary.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);   // тест бесполезен, если корень репозитория не найден
        return dir!.FullName;
    }

    /// <summary>Файлы программы с данным расширением: путь от корня и текст.</summary>
    public static IEnumerable<(string Path, string Text)> Sources(string root, string pattern = "*.cs")
    {
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly))
            yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));

        foreach (var dir in SourceDirs)
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));
        }
    }
}

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Сторож правила AGENTS.md «буквы „е с точками“ нет нигде в текстах»: ни в подписях окон, ни в
/// сообщениях, ни в комментариях и документации. Сам символ остается только там, где без него не
/// работает свертка при поиске: человек может набрать эту букву с клавиатуры.
/// </summary>
public sealed class NoYoLetterTests
{
    // Места свертки: подсветка найденного, написания запроса для индекса, тип документа.
    private static readonly string[] Allowed =
    [
        Path.Combine("Core", "Documents", "DocHighlight.cs"),
        Path.Combine("Database", "DocIndex", "DocTextIndex.cs"),
        Path.Combine("Core", "Documents", "DocTypes.cs"),
    ];

    private const char Small = '\u0451';
    private const char Capital = '\u0401';

    [Fact]
    public void Texts_have_no_yo_letter()
    {
        var root = RepoFiles.Root();
        var files = new[] { "*.cs", "*.axaml" }.SelectMany(p => RepoFiles.Sources(root, p))
            .Concat(Docs(root));

        var offenders = files
            .Where(f => !Allowed.Contains(f.Path))
            .Where(f => f.Text.Contains(Small) || f.Text.Contains(Capital))
            .Select(f => f.Path)
            .ToList();

        Assert.True(offenders.Count == 0, "Буква «е с точками» в текстах: " + string.Join(", ", offenders));
    }

    private static IEnumerable<(string Path, string Text)> Docs(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly))
            yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));
        var docs = Path.Combine(root, "docs");
        if (!Directory.Exists(docs)) yield break;
        foreach (var file in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
            yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));
    }
}

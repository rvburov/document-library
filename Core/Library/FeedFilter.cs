using System.Globalization;
using PdfLibrary.Core.Documents;

namespace PdfLibrary.Core.Library;

/// <summary>Порядок ленты — пункты выпадающего списка в навбаре.</summary>
public static class FeedSort
{
    public const string Relevance = "rel";
    public const string Modified  = "mod";
    public const string NameAsc   = "az";
    public const string NameDesc  = "za";
    public const string Size      = "size";

    /// <summary>Пункты списка: ключ и подпись.</summary>
    public static readonly (string Key, string Label)[] Options =
    [
        (Relevance, "По релевантности"),
        (Modified,  "По изменению файла"),
        (NameAsc,   "По названию ↑"),
        (NameDesc,  "По названию ↓"),
        (Size,      "По размеру"),
    ];
}

/// <summary>Найденное у документа для ленты: фрагмент текста и место в выдаче.</summary>
/// <param name="Score">Чем выше, тем раньше при порядке «По релевантности».</param>
public readonly record struct FeedHit(string Snippet, bool InText, bool ByName, int Score);

/// <summary>Что показывать в ленте: галочки источников, типы, метка, настройки и поиск.</summary>
/// <param name="Sources">Источники с галочкой в боковой панели.</param>
/// <param name="HiddenTypes">Типы, снятые в фильтре «Тип документа»; «Без типа» — <see cref="DocTypes.None"/>.</param>
/// <param name="Tag">Метка, выбранная в боковой панели; null — без фильтра по метке.</param>
/// <param name="ShowMissing">Показывать документы, которых нет на диске.</param>
/// <param name="ShowScans">Показывать сканы без текстового слоя.</param>
/// <param name="Hits">Итог поиска; null — поиска нет, показываются все документы.</param>
public sealed record FeedQuery(IReadOnlySet<int> Sources, IReadOnlySet<string> HiddenTypes, int? Tag,
                               bool ShowMissing, bool ShowScans, string Sort,
                               IReadOnlyDictionary<DocItem, FeedHit>? Hits);

/// <summary>
/// Сборка ленты из документов в памяти: фильтры, поиск и порядок. Чистая логика без UI —
/// лента, список панели чтения и стенды зовут одно и то же.
/// </summary>
public static class FeedFilter
{
    private static readonly CompareInfo Russian = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

    /// <summary>Тип документа для фильтра: пустой тип — «Без типа».</summary>
    public static string TypeOf(DocItem doc) => doc.DocType.Length > 0 ? doc.DocType : DocTypes.None;

    /// <summary>Документ проходит все фильтры, кроме типа (типам — свои числа в фильтре).</summary>
    public static bool PassesExceptType(DocItem doc, FeedQuery q)
    {
        if (!q.Sources.Contains(doc.SourceId)) return false;
        if (doc.Missing && !q.ShowMissing) return false;
        if (doc.IsScan && !q.ShowScans) return false;
        if (q.Tag is { } tag && !doc.Tags.Contains(tag)) return false;
        if (q.Hits is { } hits && !hits.ContainsKey(doc)) return false;
        return true;
    }

    /// <summary>Лента: отфильтрованные и упорядоченные документы.</summary>
    public static List<DocItem> Apply(IEnumerable<DocItem> docs, FeedQuery q)
    {
        var list = docs.Where(d => PassesExceptType(d, q) && !q.HiddenTypes.Contains(TypeOf(d))).ToList();
        list.Sort(Comparer(q));
        return list;
    }

    /// <summary>
    /// Сколько документов каждого типа — числа в фильтре «Тип документа». Считаются по всем
    /// остальным фильтрам, но без самого фильтра типа: снятый тип остается в списке со своим числом.
    /// </summary>
    public static List<(string Type, int Count)> TypeCounts(IEnumerable<DocItem> docs, FeedQuery q) =>
        docs.Where(d => PassesExceptType(d, q))
            .GroupBy(TypeOf)
            .Select(g => (g.Key, g.Count()))
            .OrderBy(t => t.Key == DocTypes.None ? 1 : 0)
            .ThenByDescending(t => t.Item2)
            .ThenBy(t => t.Key, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true))
            .ToList();

    private static Comparison<DocItem> Comparer(FeedQuery q)
    {
        int ByTitle(DocItem a, DocItem b) =>
            Russian.Compare(a.Title, b.Title, CompareOptions.IgnoreCase) is var c && c != 0
                ? c
                : string.CompareOrdinal(a.FullPath, b.FullPath);

        return q.Sort switch
        {
            FeedSort.Modified => (a, b) => b.Mtime.CompareTo(a.Mtime) is var c && c != 0 ? c : ByTitle(a, b),
            FeedSort.NameAsc  => ByTitle,
            FeedSort.NameDesc => (a, b) => ByTitle(b, a),
            FeedSort.Size     => (a, b) => b.Size.CompareTo(a.Size) is var c && c != 0 ? c : ByTitle(a, b),
            // Без поиска релевантности нет — свежие документы первыми.
            _ => q.Hits is { } hits
                ? (a, b) =>
                {
                    var c = hits[b].Score.CompareTo(hits[a].Score);
                    return c != 0 ? c : b.Mtime.CompareTo(a.Mtime) is var m && m != 0 ? m : ByTitle(a, b);
                }
                : (a, b) => b.Mtime.CompareTo(a.Mtime) is var c && c != 0 ? c : ByTitle(a, b),
        };
    }
}

using PdfLibrary.Core.Documents;
using PdfLibrary.Core.Library;

namespace PdfLibrary.Tests.Unit;

/// <summary>Сборка ленты (<see cref="FeedFilter"/>): галочки источников, тип, метка, настройки, поиск, порядок.</summary>
public sealed class FeedFilterTests
{
    private static DocItem Doc(int id, int source, string title, string type = "", long mtime = 0, long size = 0,
                               bool missing = false, DocTextState state = DocTextState.Read, params int[] tags)
    {
        var doc = new DocItem
        {
            Id = id, SourceId = source, FileName = title + ".pdf", FullPath = $"/s{source}/{title}.pdf",
            DocType = type, Mtime = mtime, Size = size, Missing = missing, TextState = state,
        };
        foreach (var t in tags) doc.Tags.Add(t);
        return doc;
    }

    private static FeedQuery Query(IEnumerable<int> sources, string sort = FeedSort.Modified,
                                   IReadOnlySet<string>? hidden = null, int? tag = null, bool missing = true,
                                   bool scans = true, IReadOnlyDictionary<DocItem, FeedHit>? hits = null) =>
        new(sources.ToHashSet(), hidden ?? new HashSet<string>(), tag, missing, scans, sort, hits);

    private static readonly DocItem A = Doc(1, 1, "Альфа", "Чертеж", mtime: 30, size: 100);
    private static readonly DocItem B = Doc(2, 1, "Бета", "ГОСТ", mtime: 10, size: 300, tags: 7);
    private static readonly DocItem C = Doc(3, 2, "Гамма", "", mtime: 20, size: 200);
    private static readonly DocItem Gone = Doc(4, 1, "Пропавший", "Чертеж", mtime: 40, missing: true);
    private static readonly DocItem Scan = Doc(5, 2, "Скан", "Паспорт", mtime: 5, state: DocTextState.NoText);
    private static readonly DocItem[] All = [A, B, C, Gone, Scan];

    [Fact]
    public void Unchecked_sources_are_hidden() =>
        Assert.Equal([A, B, Gone], FeedFilter.Apply(All, Query([1])).OrderBy(d => d.Id));

    [Fact]
    public void Default_order_is_freshest_first() =>
        Assert.Equal([Gone, A, C, B, Scan], FeedFilter.Apply(All, Query([1, 2])));

    [Fact]
    public void Name_order_is_alphabetical_both_ways()
    {
        Assert.Equal(["Альфа", "Бета", "Гамма", "Пропавший", "Скан"],
                     FeedFilter.Apply(All, Query([1, 2], FeedSort.NameAsc)).Select(d => d.Title));
        Assert.Equal(["Скан", "Пропавший", "Гамма", "Бета", "Альфа"],
                     FeedFilter.Apply(All, Query([1, 2], FeedSort.NameDesc)).Select(d => d.Title));
    }

    [Fact]
    public void Size_order_is_biggest_first() =>
        Assert.Equal([B, C, A], FeedFilter.Apply(All, Query([1, 2], FeedSort.Size)).Take(3));

    [Fact]
    public void Missing_and_scans_follow_the_settings()
    {
        var shown = FeedFilter.Apply(All, Query([1, 2], missing: false, scans: false));
        Assert.DoesNotContain(Gone, shown);
        Assert.DoesNotContain(Scan, shown);
    }

    [Fact]
    public void Tag_filter_keeps_only_tagged() =>
        Assert.Equal([B], FeedFilter.Apply(All, Query([1, 2], tag: 7)));

    [Fact]
    public void Hidden_types_drop_out_and_empty_type_is_named()
    {
        var shown = FeedFilter.Apply(All, Query([1, 2], hidden: new HashSet<string> { "Чертеж", DocTypes.None }));
        Assert.DoesNotContain(A, shown);
        Assert.DoesNotContain(C, shown);
        Assert.Contains(B, shown);
    }

    /// <summary>Числа фильтра типов не зависят от самого фильтра типов: снятый тип остается со своим числом.</summary>
    [Fact]
    public void Type_counts_ignore_the_type_filter_itself()
    {
        var counts = FeedFilter.TypeCounts(All, Query([1, 2], hidden: new HashSet<string> { "Чертеж" }));
        Assert.Contains(("Чертеж", 2), counts);
        Assert.Equal(DocTypes.None, counts[^1].Type);   // «Без типа» — последним
    }

    [Fact]
    public void Search_keeps_only_found_and_orders_by_score()
    {
        var hits = new Dictionary<DocItem, FeedHit>
        {
            [B] = new("…", true, false, 100),
            [C] = new("", false, true, 1_000_000),
        };
        Assert.Equal([C, B], FeedFilter.Apply(All, Query([1, 2], FeedSort.Relevance, hits: hits)));
    }

    [Fact]
    public void Relevance_without_search_is_freshest_first() =>
        Assert.Equal(FeedFilter.Apply(All, Query([1, 2])), FeedFilter.Apply(All, Query([1, 2], FeedSort.Relevance)));
}

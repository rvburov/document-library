using PdfLibrary.Core.Models;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Дерево «Год → Месяц → День» для чеклиста фильтра по дате (<see cref="DateFilterTree"/>) —
/// чистая логика: группировка сырых значений, суммы счетчиков, порядок под сортировку
/// колонки, поиск по веткам, третье состояние галочки у родителей.
/// </summary>
public sealed class DateFilterTreeTests
{
    // Разбор как в журналах: сырое значение — сама дата.
    private static DateTime? ParseDay(string raw) =>
        DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                          System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static IReadOnlyList<DateFilterNode> Build(params (string Raw, int Count)[] values) =>
        DateFilterTree.Build(values, ParseDay);

    // ── Группировка ───────────────────────────────────────────────────────────

    [Fact]
    public void Groups_values_by_year_month_day()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-03", 1), ("2026-09-23", 2), ("2027-01-05", 1));

        Assert.Equal(["2026", "2027"], roots.Select(n => n.Label));

        var y2026 = roots[0];
        Assert.Equal(DateNodeKind.Year, y2026.Kind);
        Assert.Equal(["Август", "Сентябрь"], y2026.Children.Select(n => n.Label));
        Assert.Equal(["2", "3"], y2026.Children[0].Children.Select(n => n.Label));
        Assert.Equal(["23"], y2026.Children[1].Children.Select(n => n.Label));
    }

    [Fact]
    public void Levels_are_year_zero_month_one_day_two()
    {
        var roots = Build(("2026-08-02", 1));

        Assert.Equal(0, roots[0].Level);
        Assert.Equal(1, roots[0].Children[0].Level);
        Assert.Equal(2, roots[0].Children[0].Children[0].Level);
    }

    [Fact]
    public void Counts_sum_up_the_tree()
    {
        var roots = Build(("2026-08-02", 3), ("2026-08-03", 2), ("2026-09-23", 5));

        Assert.Equal(10, roots[0].Count);                    // год
        Assert.Equal(5,  roots[0].Children[0].Count);        // август
        Assert.Equal(3,  roots[0].Children[0].Children[0].Count);
    }




    // ── Значения без даты ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_and_unparsable_values_go_to_a_single_undated_node_at_the_end()
    {
        var roots = Build(("2026-08-02", 1), ("", 2), ("не дата", 3));

        var last = roots[^1];
        Assert.Equal(DateNodeKind.Undated, last.Kind);
        Assert.Equal("(Пусто)", last.Label);
        Assert.Equal(5, last.Count);
        Assert.Equal(2, last.Raws.Count);
        Assert.Empty(last.Children);
    }

    [Fact]
    public void No_undated_node_when_every_value_is_a_date()
    {
        var roots = Build(("2026-08-02", 1));

        Assert.DoesNotContain(roots, n => n.Kind == DateNodeKind.Undated);
    }

    [Fact]
    public void Empty_input_gives_empty_tree()
    {
        Assert.Empty(DateFilterTree.Build([], ParseDay));
    }

    // ── Порядок ───────────────────────────────────────────────────────────────

    [Fact]
    public void Newest_first_reverses_years_months_and_days()
    {
        var roots = DateFilterTree.Build(
            [("2026-08-02", 1), ("2026-09-23", 1), ("2027-01-05", 1)], ParseDay, newestFirst: true);

        Assert.Equal(["2027", "2026"], roots.Select(n => n.Label));
        Assert.Equal(["Сентябрь", "Август"], roots[1].Children.Select(n => n.Label));
    }

    [Fact]
    public void Days_inside_a_month_are_ordered_numerically_not_as_text()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-10", 1), ("2026-08-09", 1));

        // Как текст «10» встало бы перед «2».
        Assert.Equal(["2", "9", "10"], roots[0].Children[0].Children.Select(n => n.Label));
    }

    [Fact]
    public void Undated_node_stays_last_even_when_newest_first()
    {
        var roots = DateFilterTree.Build([("", 1), ("2026-08-02", 1)], ParseDay, newestFirst: true);

        Assert.Equal(DateNodeKind.Undated, roots[^1].Kind);
    }

    // ── Поиск ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_by_year_keeps_the_whole_year()
    {
        var roots = Build(("2026-08-02", 1), ("2026-09-23", 1), ("2027-01-05", 1));

        var found = DateFilterTree.Search(roots, "2026");

        Assert.Single(found);
        Assert.Equal(2, found[0].Children.Count);
    }

    [Fact]
    public void Search_by_month_keeps_the_whole_month()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-03", 1), ("2026-09-23", 1));

        var found = DateFilterTree.Search(roots, "август");

        Assert.Single(found[0].Children);
        Assert.Equal(2, found[0].Children[0].Children.Count);
    }

    [Fact]
    public void Search_by_day_keeps_the_day_with_its_parents()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-23", 1), ("2026-09-23", 1));

        var found = DateFilterTree.Search(roots, "23");

        Assert.Single(found);                       // год остался один
        Assert.Equal(2, found[0].Children.Count);   // и август, и сентябрь — в каждом свое «23»
        Assert.All(found[0].Children, m => Assert.Single(m.Children));
    }

    [Fact]
    public void Search_by_full_date_finds_the_day()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-23", 1));

        var found = DateFilterTree.Search(roots, "23.08.2026");

        Assert.Equal("23", found[0].Children[0].Children[0].Label);
    }

    [Fact]
    public void Search_narrows_counts_to_what_is_shown()
    {
        var roots = Build(("2026-08-02", 4), ("2026-08-23", 1));

        var found = DateFilterTree.Search(roots, "23");

        Assert.Equal(1, found[0].Count);
    }

    [Fact]
    public void Search_without_matches_gives_nothing()
    {
        var roots = Build(("2026-08-02", 1));

        Assert.Empty(DateFilterTree.Search(roots, "декабрь"));
    }

    [Fact]
    public void Empty_query_returns_the_whole_tree()
    {
        var roots = Build(("2026-08-02", 1));

        Assert.Same(roots, DateFilterTree.Search(roots, "   "));
    }

    // ── Состояние галочки ─────────────────────────────────────────────────────

    [Fact]
    public void Parent_is_checked_when_all_of_its_days_are_checked()
    {
        var roots = Build(("2026-08-02", 1), ("2026-08-03", 1));
        var all   = new HashSet<string> { "2026-08-02", "2026-08-03" };

        Assert.True(DateFilterTree.IsFullyChecked(roots[0], all));
        Assert.True(DateFilterTree.IsFullyChecked(roots[0].Children[0], all));
    }

    [Fact]
    public void Parent_is_unchecked_when_at_least_one_day_is_unchecked()
    {
        // Промежуточного состояния нет: галочка ветки читается как «(Выделить все)»
        // для нее — снят хоть один день, значит у месяца и года галочки нет.
        var roots = Build(("2026-08-02", 1), ("2026-08-03", 1));
        var some  = new HashSet<string> { "2026-08-02" };

        Assert.False(DateFilterTree.IsFullyChecked(roots[0], some));
        Assert.False(DateFilterTree.IsFullyChecked(roots[0].Children[0], some));
        Assert.True(DateFilterTree.IsFullyChecked(roots[0].Children[0].Children[0], some));
        Assert.False(DateFilterTree.IsFullyChecked(roots[0].Children[0].Children[1], some));
    }


    [Fact]
    public void Parent_is_unchecked_when_nothing_is_checked()
    {
        var roots = Build(("2026-08-02", 1));

        Assert.False(DateFilterTree.IsFullyChecked(roots[0], new HashSet<string>()));
    }

    // ── Обход ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_walks_parents_before_children()
    {
        var roots = Build(("2026-08-02", 1), ("2026-09-23", 1));

        var labels = DateFilterTree.Flatten(roots).Select(n => n.Label).ToList();

        Assert.Equal(["2026", "Август", "2", "Сентябрь", "23"], labels);
    }
}

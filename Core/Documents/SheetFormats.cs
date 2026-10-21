namespace PdfLibrary.Core.Documents;

/// <summary>
/// Форматы листов документа по размерам страниц: «A4 × 48», «A1 × 1, A3 × 2». Строка идет в
/// подвал карточки и в строку состояния панели чтения, самый крупный формат — на плитку карточки
/// («A1 · 3 с.»).
///
/// Форматы — ГОСТ 2.301: основные A0–A4, A5 и производные A4×3 … A0×3. Допуск 30 мм с каждой
/// стороны: такой допуск проверен на инженерных папках — страницы, сохраненные с полями, обрезкой или в дюймовом размере, попадают в свой
/// формат, а не в «нестандартный». Ориентация не важна: альбомный A3 — тот же A3.
/// Нестандартный лист называется размером в миллиметрах: «148×100».
/// </summary>
public sealed class SheetFormats
{
    /// <summary>Допуск сравнения со стандартным размером, мм.</summary>
    public const double ToleranceMm = 30;

    // Порядок важен: сначала основные форматы, потом производные. Производный A4×3 (297×630)
    // не должен перехватить страницу, которая ближе к основному.
    private static readonly (string Name, double W, double H)[] Standard =
    [
        ("A0", 841, 1189), ("A1", 594, 841), ("A2", 420, 594), ("A3", 297, 420), ("A4", 210, 297),
        ("A5", 148, 210),
        ("A0×2", 1189, 1682), ("A0×3", 1189, 2523), ("A1×3", 841, 1783), ("A1×4", 841, 2378),
        ("A2×3", 594, 1261), ("A2×4", 594, 1682), ("A2×5", 594, 2102),
        ("A3×3", 420, 891), ("A3×4", 420, 1189), ("A3×5", 420, 1486), ("A3×6", 420, 1783), ("A3×7", 420, 2080),
        ("A4×3", 297, 630), ("A4×4", 297, 841), ("A4×5", 297, 1051), ("A4×6", 297, 1261), ("A4×7", 297, 1471),
        ("A4×8", 297, 1682), ("A4×9", 297, 1982),
    ];

    // Формат → (сколько страниц, площадь листа). Площадь нужна порядку: крупные форматы первыми.
    private readonly Dictionary<string, (int Count, double Area)> _counts = new(StringComparer.Ordinal);

    /// <summary>Сколько страниц учтено.</summary>
    public int Pages { get; private set; }

    /// <summary>Учесть страницу. Размеры — в пунктах PDF (1/72 дюйма), как их отдает разборщик.</summary>
    public void Add(double widthPt, double heightPt)
    {
        var w = Math.Abs(widthPt)  * 25.4 / 72.0;
        var h = Math.Abs(heightPt) * 25.4 / 72.0;
        if (w < 1 || h < 1) return;

        var name = NameOf(w, h);
        var area = w * h;
        _counts[name] = _counts.TryGetValue(name, out var c) ? (c.Count + 1, c.Area) : (1, area);
        Pages++;
    }

    /// <summary>Формат страницы по размерам в миллиметрах.</summary>
    public static string NameOf(double widthMm, double heightMm)
    {
        double w = Math.Min(widthMm, heightMm), h = Math.Max(widthMm, heightMm);
        foreach (var (name, sw, sh) in Standard)
            if (Math.Abs(w - sw) <= ToleranceMm && Math.Abs(h - sh) <= ToleranceMm)
                return name;
        return $"{Math.Round(w):0}×{Math.Round(h):0}";
    }

    /// <summary>Сводка: «A4 × 48», «A1 × 1, A3 × 2». Крупные форматы первыми; пусто — страниц нет.</summary>
    public string Summary => string.Join(", ", Ordered().Select(kv => $"{kv.Key} × {kv.Value.Count}"));

    /// <summary>Самый крупный формат документа — для плитки: «A1». Пусто — страниц нет.</summary>
    public string Largest => Ordered().Select(kv => kv.Key).FirstOrDefault() ?? "";

    private IEnumerable<KeyValuePair<string, (int Count, double Area)>> Ordered() =>
        _counts.OrderByDescending(kv => kv.Value.Area).ThenBy(kv => kv.Key, StringComparer.Ordinal);
}

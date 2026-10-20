using System.Globalization;
using System.IO;
using System.Text;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// PDF, написанный руками, объект за объектом: у листа поворот <c>/Rotate</c>, обрезка и поток
/// содержимого как есть.
///
/// Сборщик PdfPig поворот листа ставит, но повернуть само содержимое не умеет — матрицы
/// <c>cm</c> у него нет. А обычный альбомный лист, сохраненный с поворотом, — это ровно поворот
/// листа ПЛЮС содержимое, повернутое навстречу: на экране текст стоит прямо. Без него проверялся
/// бы только лист, где текст лежит боком, то есть не тот, что встречается чаще. Файл выходит
/// крошечным, и смещения таблицы xref считаются здесь же.
///
/// Текст пишется латиницей шрифтом Helvetica из стандартных четырнадцати: кириллице понадобился
/// бы вложенный шрифт, а проверяется здесь место и целость слова, а не алфавит.
/// </summary>
internal static class HandPdf
{
    /// <summary>Обрезка листа в пунктах неповернутого листа.</summary>
    public readonly record struct Crop(double Left, double Bottom, double Right, double Top);

    /// <summary>Лист А4 целиком — так выглядит лист без обрезки.</summary>
    public static readonly Crop Full = new(0, 0, 595, 842);

    /// <summary>Лист: поток содержимого, поворот, обрезка (<c>null</c> — без обрезки) и размер
    /// в пунктах (стандартно A4: 595 × 842).</summary>
    public sealed record Sheet(string Content, int Rotate = 0, Crop? Cropped = null,
                               double Width = 595, double Height = 842);

    // Строка PDF: латиница — как есть, иначе UTF-16BE с меткой порядка байт шестнадцатеричными
    // цифрами — файл остается ASCII.
    private static string TextString(string text) =>
        text.All(c => c < 128)
            ? $"({text})"
            : "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";

    /// <summary>Записать документ из листов и вернуть путь к нему.</summary>
    public static string Write(string dir, string name, params Sheet[] sheets) =>
        Write(dir, name, null, sheets);

    /// <summary>Записать документ с названием в свойствах (<c>/Info /Title</c>).</summary>
    public static string Write(string dir, string name, string? title, params Sheet[] sheets)
    {
        // Номера объектов: 1 — каталог, 2 — дерево листов, у листа i — сам лист (3 + 2i) и его
        // содержимое (4 + 2i), последним — шрифт.
        var font    = 3 + 2 * sheets.Length;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", sheets.Select((_, i) => $"{3 + 2 * i} 0 R"))}] /Count {sheets.Length} >>",
        };
        for (var i = 0; i < sheets.Length; i++)
        {
            var sheet = sheets[i];
            var crop  = sheet.Cropped is { } c ? Inv($" /CropBox [{c.Left} {c.Bottom} {c.Right} {c.Top}]") : "";
            objects.Add(Inv($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {sheet.Width} {sheet.Height}]") + $"{crop} /Rotate {sheet.Rotate} " +
                        $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {4 + 2 * i} 0 R >>");
            objects.Add($"<< /Length {sheet.Content.Length} >>\nstream\n{sheet.Content}\nendstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var info = 0;
        if (title is not null)
        {
            objects.Add($"<< /Title {TextString(title)} /Producer (PdfLibrary tests) >>");
            info = objects.Count;
        }

        // Файл целиком ASCII: знак — это байт, и смещение объекта — длина текста перед ним.
        var pdf     = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        var infoRef = info > 0 ? $" /Info {info} 0 R" : "";
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{infoRef} >>\nstartxref\n{xref}\n%%EOF\n");

        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf.ToString()));
        return path;
    }

    /// <summary>
    /// Текст, который на ПОКАЗАННОМ листе стоит прямо: строка начинается в
    /// <paramref name="x"/> пунктах от левого края и в <paramref name="fromTop"/> от верхнего.
    /// Содержимое повернуто навстречу листу — так выглядит обычный альбомный лист, сохраненный с
    /// поворотом.
    /// </summary>
    public static string Upright(int rotate, Crop crop, double x, double fromTop, string text, double size = 14)
    {
        // Где эта точка показанного листа лежит на неповернутом, и куда смотрят оси строки:
        // ось строки должна выйти на экране вправо, верх букв — вверх.
        var (ux, uy, a, b, c, d) = rotate switch
        {
            90  => (crop.Left  + fromTop, crop.Bottom + x,        0,  1, -1,  0),
            180 => (crop.Right - x,       crop.Bottom + fromTop, -1,  0,  0, -1),
            270 => (crop.Right - fromTop, crop.Top    - x,        0, -1,  1,  0),
            _   => (crop.Left  + x,       crop.Top    - fromTop,  1,  0,  0,  1),
        };
        return Inv($"q {a} {b} {c} {d} {ux} {uy} cm BT /F1 {size} Tf 0 0 Td ({text}) Tj ET Q");
    }

    /// <summary>
    /// Текст, написанный как на неповернутом листе, в точке (<paramref name="x"/>,
    /// <paramref name="y"/>) его собственных координат. На повернутом листе он идет боком или
    /// вверх ногами — так выглядит лист, повернутый без поправки содержимого.
    /// </summary>
    public static string Plain(double x, double y, string text, double size = 14) =>
        Inv($"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET");

    /// <summary>Текст, идущий снизу вверх, — как надписи штампа на полях чертежа.</summary>
    public static string Rising(double x, double y, string text, double size = 12) =>
        Inv($"q 0 1 -1 0 {x} {y} cm BT /F1 {size} Tf 0 0 Td ({text}) Tj ET Q");

    /// <summary>Текст наискось, под углом <paramref name="degrees"/>, — как водяной знак поперек листа.</summary>
    public static string Slanted(double x, double y, double degrees, string text, double size = 24)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        return Inv($"q {cos:0.####} {sin:0.####} {-sin:0.####} {cos:0.####} {x} {y} cm BT /F1 {size} Tf 0 0 Td ({text}) Tj ET Q");
    }

    /// <summary>
    /// PDF из обычного текста: строки по 90 знаков, по 50 строк на лист А4. Текст пишется
    /// латиницей (<see cref="Latin"/>) — у шрифта из стандартных четырнадцати нет кириллицы, а
    /// проверяется здесь не алфавит, а то, что очередь прочитала документ.
    /// </summary>
    public static string WriteText(string dir, string name, string text)
    {
        var lines = new List<string>();
        foreach (var paragraph in Latin(text).Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length + word.Length + 1 > 90 && line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            lines.Add(line.ToString());
        }

        var sheets = lines.Chunk(50)
            .Select(page => new Sheet(string.Join("\n", page.Select((l, i) => Plain(40, 800 - 15 * i, Escape(l), 10)))))
            .ToArray();
        return Write(dir, name, sheets.Length > 0 ? sheets : [new Sheet("")]);
    }

    /// <summary>Кириллица латиницей — для текста PDF и для запроса к тому же тексту.</summary>
    public static string Latin(string text)
    {
        const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] la = ["a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "j", "k", "l", "m", "n", "o", "p", "r",
                       "s", "t", "u", "f", "h", "c", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"];
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var i = ru.IndexOf(char.ToLowerInvariant(ch));
            if (i < 0) { sb.Append(ch < 128 ? ch : ' '); continue; }
            var latin = la[i];
            sb.Append(char.IsUpper(ch) && latin.Length > 0 ? char.ToUpperInvariant(latin[0]) + latin[1..] : latin);
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    // Числа в PDF пишутся с точкой, что бы ни стояло в языке системы.
    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

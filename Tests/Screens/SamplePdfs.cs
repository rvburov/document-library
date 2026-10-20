using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace PdfLibrary.Tests.Screens;

/// <summary>
/// Документы стенда — те же, что в согласованном макете: руководство по насосу, чертеж общего
/// вида на листе A1, схема, спецификация, протокол, ГОСТ, СП, договор и скан паспорта без
/// текстового слоя. Русский текст пишется вложенным шрифтом TrueType из системы; нет такого
/// шрифта — латиницей (кадры тогда хуже похожи на жизнь, но проверки те же).
/// </summary>
internal static class SamplePdfs
{
    private static readonly string[] FontCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "C:/Windows/Fonts/arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
    ];

    private static readonly string? FontPath = FontCandidates.FirstOrDefault(File.Exists);

    /// <summary>Шрифт с кириллицей найден — документы стенда русские.</summary>
    public static bool Cyrillic => FontPath is not null;

    private const double Mm = 72.0 / 25.4;

    public static void Write(string root)
    {
        var project = Path.Combine(root, "Проектная документация");
        var archive = Path.Combine(root, "Сетевой архив");
        var downloads = Path.Combine(root, "Загрузки");

        Manual(Path.Combine(project, "Паспорта", "ЦНС-38-44_РЭ.pdf"));
        Drawing(Path.Combine(project, "КД", "АБВГ.301122.001 ВО.pdf"), "Насосная станция. Чертеж общего вида",
                "Насосная станция в составе: насос центробежный ЦНС 38-44 — 2 шт., электродвигатель АИР160S2 — 2 шт., " +
                "рама сварная, обвязка трубопроводами DN 80.", (841, 594), (420, 297), (420, 297));
        Drawing(Path.Combine(project, "КД", "АБВГ.301122.001 Г3.pdf"), "Схема гидравлическая принципиальная",
                "Н1, Н2 — насос центробежный ЦНС 38-44; КО1, КО2 — клапан обратный; ЗД1–ЗД4 — задвижка DN 80.",
                (594, 420));
        Text(Path.Combine(project, "КД", "АБВГ.301122.001.pdf"), "Спецификация АБВГ.301122.001",
             ["Поз. 1 — насос центробежный ЦНС 38-44, 2 шт.; поз. 2 — электродвигатель АИР160S2, 2 шт.;",
              "поз. 3 — муфта упругая втулочно-пальцевая, 2 шт."], 2);
        Text(Path.Combine(project, "Испытания", "Протокол_45-26.pdf"), "Протокол испытаний насосного агрегата № 45/26",
             ["Испытания насоса центробежного ЦНС 38-44 проведены на стенде предприятия: напор при",
              "номинальной подаче 38 м³/ч составил 44,6 м, вибрация в пределах нормы."], 6);
        Scan(Path.Combine(project, "Паспорта", "Паспорт_ЦНС38_1187.pdf"), 12);

        Text(Path.Combine(archive, "Нормативы", "ГОСТ_2.104-2006.pdf"), "ГОСТ 2.104-2006 ЕСКД. Основные надписи",
             ["Стандарт устанавливает формы, размеры и порядок заполнения основных надписей и",
              "дополнительных граф к ним в конструкторских документах."], 18);
        Text(Path.Combine(archive, "Нормативы", "СП_31.13330.2021.pdf"), "СП 31.13330.2021 Водоснабжение. Наружные сети и сооружения",
             ["Насосные станции проектируются с учетом совместной работы насосов, водоводов и",
              "регулирующих емкостей; центробежные насосы, как правило, устанавливаются под залив."], 24);

        Text(Path.Combine(downloads, "Договор_17-2026.pdf"), "Договор поставки № 17/2026",
             ["Поставщик обязуется передать в собственность покупателя насосы центробежные в",
              "количестве и сроки согласно спецификации."], 9);
    }

    private static void Manual(string path)
    {
        Text(path, "Насос центробежный ЦНС 38-44. Руководство по эксплуатации",
             ["Насос центробежный секционный ЦНС 38-44 предназначен для перекачивания воды с водородным",
              "показателем pH 7–8,5 и температурой до 45 °C. Работа центробежного насоса при закрытой",
              "задвижке на напорном трубопроводе более 2 минут не допускается. Насос применяется в",
              "системах водоснабжения промышленных предприятий, в установках повышения давления и для",
              "откачки воды из резервуаров. Перед пуском насоса корпус заполняется водой."], 8);
    }

    // Текстовый документ A4: заголовок, абзац на каждой странице, рамка-«рисунок».
    private static void Text(string path, string title, string[] paragraph, int pages)
    {
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = Line(title);
        var font = Font(builder);
        for (var p = 0; p < pages; p++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.SetStrokeColor(0x9A, 0xA3, 0xAE);
            page.AddText(Line(p == 0 ? title : $"{title} — лист {p + 1}"), 14, new PdfPoint(60, 780), font);
            var y = 740.0;
            foreach (var line in paragraph)
            {
                page.AddText(Line(line), 10, new PdfPoint(60, y), font);
                y -= 16;
            }
            page.DrawRectangle(new PdfPoint(60, 420), 470, 200, 1);
            page.AddText(Line($"Рисунок {p + 1}"), 9, new PdfPoint(260, 405), font);
            for (var k = 0; k < 10; k++)
                page.AddText(Line("Текст раздела: насос, задвижка, трубопровод, напор и подача."), 10,
                             new PdfPoint(60, 380 - 16 * k), font);
        }
        Save(path, builder);
    }

    // Чертеж: рамка, основная надпись, контуры — на листах заданных форматов (мм, альбомные).
    private static void Drawing(string path, string title, string note, params (double W, double H)[] sheets)
    {
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = Line(title);
        var font = Font(builder);
        foreach (var (w, h) in sheets)
        {
            var page = builder.AddPage(w * Mm, h * Mm);
            var (pw, ph) = (w * Mm, h * Mm);
            page.DrawRectangle(new PdfPoint(20 * Mm, 5 * Mm), pw - 25 * Mm, ph - 10 * Mm, 1.5);
            page.DrawRectangle(new PdfPoint(pw - 190 * Mm, 5 * Mm), 185 * Mm, 55 * Mm, 1);
            page.AddText(Line(title), 14, new PdfPoint(pw - 180 * Mm, 35 * Mm), font);
            page.AddText(Line("АБВГ.301122.001"), 12, new PdfPoint(pw - 180 * Mm, 20 * Mm), font);
            page.DrawRectangle(new PdfPoint(60 * Mm, ph * 0.35), pw * 0.4, ph * 0.45, 1);
            page.DrawRectangle(new PdfPoint(80 * Mm, ph * 0.5), pw * 0.1, ph * 0.15, 1);
            page.AddText(Line(note), 10, new PdfPoint(60 * Mm, ph * 0.25), font);
        }
        Save(path, builder);
    }

    // Скан: страницы без текстового слоя — только линии.
    private static void Scan(string path, int pages)
    {
        var builder = new PdfDocumentBuilder();
        for (var p = 0; p < pages; p++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.SetStrokeColor(0x8C, 0x87, 0x7C);
            for (var k = 0; k < 20; k++)
                page.DrawLine(new PdfPoint(70, 760 - 18 * k), new PdfPoint(520 - (k % 3) * 40, 760 - 18 * k), 3);
            page.DrawRectangle(new PdfPoint(70, 200), 450, 150, 1);
        }
        Save(path, builder);
    }

    private static PdfDocumentBuilder.AddedFont Font(PdfDocumentBuilder builder) =>
        FontPath is { } ttf
            ? builder.AddTrueTypeFont(File.ReadAllBytes(ttf))
            : builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

    // Без шрифта с кириллицей — латиницей, иначе стандартный шрифт уронил бы сборку документа.
    private static string Line(string text) => Cyrillic ? text : Translit(text);

    private static string Translit(string text)
    {
        const string ru = "абвгдежзийклмнопрстуфхцчшщъыьэюя";
        string[] la = ["a", "b", "v", "g", "d", "e", "zh", "z", "i", "j", "k", "l", "m", "n", "o", "p", "r",
                       "s", "t", "u", "f", "h", "c", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"];
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            var i = ru.IndexOf(char.ToLowerInvariant(ch));
            sb.Append(i < 0 ? (ch < 128 ? ch.ToString() : " ") : la[i]);
        }
        return sb.ToString();
    }

    private static void Save(string path, PdfDocumentBuilder builder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, builder.Build());
    }
}

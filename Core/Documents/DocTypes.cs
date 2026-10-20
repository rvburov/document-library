using System.Text.RegularExpressions;

namespace PdfLibrary.Core.Documents;

/// <summary>
/// Тип документа — голубая метка на карточке и пункты фильтра «Тип документа». Определяется
/// по пути файла, поэтому известен сразу после проверки папки, а после чтения уточняется по
/// названию из свойств PDF:
///
/// <list type="number">
/// <item><b>Код документа ЕСКД в имени файла</b> (ГОСТ 2.102, 2.601, 2.701): «АБВГ.301122.001 ВО» —
///       чертеж общего вида, «ЦНС-38-44_РЭ» — руководство по эксплуатации. Код стоит после
///       обозначения или последним словом имени.</item>
/// <item><b>Слова в имени файла</b>: «ГОСТ 2.104-2006», «СП 31.13330», «Договор № 17», «Протокол
///       испытаний».</item>
/// <item><b>Слова в названии</b> из свойств PDF: «Спецификация АБВГ.301122.001».</item>
/// <item><b>Папка</b>: файл из «Паспорта» — паспорт, из «Протоколы» — протокол.</item>
/// <item><b>Обозначение без кода</b>: «АБВГ.301122.001» — чертеж детали. Так же без кода
///       обозначается спецификация, поэтому этот признак — последний: название или папка
///       говорят точнее.</item>
/// </list>
///
/// Ничего не подошло — типа нет, карточка обходится без метки. Типы крупные («Чертеж», а не
/// «Чертеж общего вида»): фильтр на сотню видов документов был бы списком, в котором ничего не
/// найти, а человеку важно отобрать чертежи от руководств и нормативов.
/// </summary>
public static class DocTypes
{
    public const string Drawing       = "Чертеж";
    public const string Scheme        = "Схема";
    public const string Specification = "Спецификация";
    public const string List          = "Ведомость";
    public const string Elements      = "Перечень элементов";
    public const string Manual        = "Руководство";
    public const string Instruction   = "Инструкция";
    public const string Passport      = "Паспорт";
    public const string Logbook       = "Формуляр";
    public const string Label         = "Этикетка";
    public const string Conditions    = "ТУ";
    public const string Note          = "Пояснительная записка";
    public const string Calculation   = "Расчет";
    public const string TestProgram   = "Программа и методика";
    public const string Table         = "Таблица";
    public const string Gost          = "ГОСТ";
    public const string Sp            = "СП";
    public const string Snip          = "СНиП";
    public const string Sto           = "СТО";
    public const string Contract      = "Договор";
    public const string Protocol      = "Протокол";
    public const string Act           = "Акт";
    public const string Letter        = "Письмо";
    public const string Invoice       = "Счет";
    public const string Certificate   = "Сертификат";
    public const string Declaration   = "Декларация";
    public const string Report        = "Отчет";
    public const string Order         = "Приказ";
    public const string Estimate      = "Смета";
    public const string Catalog       = "Каталог";
    public const string Regulation    = "Регламент";

    /// <summary>Подпись пункта фильтра для документов без типа.</summary>
    public const string None = "Без типа";

    // Коды документов ЕСКД → тип. Коды схем (буква вида + цифра типа, ГОСТ 2.701) разбираются
    // отдельно — SchemeCode.
    private static readonly Dictionary<string, string> Codes = new(StringComparer.Ordinal)
    {
        ["ВО"] = Drawing, ["СБ"] = Drawing, ["ГЧ"] = Drawing, ["МЭ"] = Drawing, ["МЧ"] = Drawing,
        ["УЧ"] = Drawing, ["ТЧ"] = Drawing,
        ["Е1"] = Scheme, ["ЭМ"] = Scheme, ["МС"] = Scheme, ["АЭ"] = Scheme,
        ["ПЭ"] = Elements, ["ПЭ0"] = Elements, ["ПЭ1"] = Elements, ["ПЭ2"] = Elements, ["ПЭ3"] = Elements,
        ["ПЭ4"] = Elements, ["ПЭ5"] = Elements, ["ПЭ6"] = Elements, ["ПЭ7"] = Elements, ["ПС7"] = Elements,
        ["ВС"] = List, ["ВД"] = List, ["ВП"] = List, ["ДП"] = List, ["ВДЭ"] = List, ["ВЭД"] = List,
        ["ВЭ"] = List, ["ЗИ"] = List, ["ВЗ"] = List, ["ЭД"] = List, ["ВКЭ"] = List, ["ВКР"] = List,
        ["Д33"] = List, ["ТП"] = List,
        ["ТБ"] = Table,
        ["ТУ"] = Conditions,
        ["ПЗ"] = Note,
        ["РР"] = Calculation, ["РР1"] = Calculation, ["РР2"] = Calculation,
        ["РЭ"] = Manual, ["РК"] = Manual, ["РЛЭ"] = Manual, ["РО"] = Manual, ["ДЭ"] = Manual,
        ["ИС"] = Instruction, ["И10"] = Instruction, ["ИМ"] = Instruction, ["ИЭ"] = Instruction,
        ["ПС"] = Passport,
        ["ФО"] = Logbook,
        ["ЭТ"] = Label,
        ["ПМ"] = TestProgram,
    };

    // Части томов: «РЭ1», «ФО3», «ТУ2», «ИС4» — тот же документ, что и без номера.
    private static readonly Regex PartCode = new(@"^(РЭ|ФО|ТУ|ИС)\d{1,2}$", RegexOptions.Compiled);

    // Схема: буква вида (Э — электрическая, Г — гидравлическая, П — пневматическая, Х — газовая,
    // К — кинематическая, В — вакуумная, Л — оптическая, Р — энергетическая, С — комбинированная)
    // и цифра типа 0–7.
    private static readonly Regex SchemeCode = new(@"^[ЭГПХКВЛРС][0-7]$", RegexOptions.Compiled);

    // Обозначение ЕСКД: АБВГ.301122.001, с исполнением и кодом после него. Буквы кода могут
    // стоять через пробел, подчеркивание или вплотную.
    private static readonly Regex Designation = new(
        @"[А-ЯA-Z]{4}\.\d{6}\.\d{3}(?:-\d{2,3})?[\s_]*(?<code>[А-Я]{1,3}\d{0,2})?(?![А-Яа-я\d])",
        RegexOptions.Compiled);

    // Слова в имени файла и в названии. Свертка — нижний регистр и «е» вместо «е с точками»,
    // поэтому шаблоны — в свернутом виде. Порядок важен: первое совпадение решает.
    private static readonly (Regex Pattern, string Type)[] NameWords =
    [
        (Word(@"гост"), Gost),
        (Word(@"снип"), Snip),
        (Word(@"сп[\s_]*\d"), Sp),
        (Word(@"сто[\s_]*\d"), Sto),
        (Word(@"ту[\s_]*\d"), Conditions),
        (Word(@"технические условия"), Conditions),
        (Word(@"спецификаци"), Specification),
        (Word(@"чертеж"), Drawing),
        (Word(@"схем"), Scheme),
        (Word(@"паспорт"), Passport),
        (Word(@"формуляр"), Logbook),
        (Word(@"руководств"), Manual),
        (Word(@"инструкци"), Instruction),
        (Word(@"пояснительн"), Note),
        (Word(@"ведомост"), List),
        (Word(@"перечень элементов"), Elements),
        (Word(@"протокол"), Protocol),
        (Word(@"договор|контракт"), Contract),
        (Word(@"акт(?![а-я])"), Act),
        (Word(@"письм"), Letter),
        (Word(@"счет(?![а-я])|счет[\s_-]*фактур"), Invoice),
        (Word(@"сертификат"), Certificate),
        (Word(@"декларац"), Declaration),
        (Word(@"отчет"), Report),
        (Word(@"приказ"), Order),
        (Word(@"смет"), Estimate),
        (Word(@"каталог"), Catalog),
        (Word(@"регламент"), Regulation),
        (Word(@"расчет"), Calculation),
        (Word(@"программа и методика"), TestProgram),
    ];

    // Имена папок: «Паспорта», «Протоколы испытаний», «ГОСТы». Только слова, которые означают вид
    // документа; «Нормативы» или «КД» тип не задают — в них лежит что угодно.
    private static readonly (Regex Pattern, string Type)[] FolderWords =
    [
        (Word(@"гост"), Gost),
        (Word(@"снип"), Snip),
        (Word(@"спецификаци"), Specification),
        (Word(@"чертеж"), Drawing),
        (Word(@"схем"), Scheme),
        (Word(@"паспорт"), Passport),
        (Word(@"формуляр"), Logbook),
        (Word(@"руководств"), Manual),
        (Word(@"инструкци"), Instruction),
        (Word(@"протокол"), Protocol),
        (Word(@"договор|контракт"), Contract),
        (Word(@"акты(?![а-я])|акт(?![а-я])"), Act),
        (Word(@"письм"), Letter),
        (Word(@"сертификат"), Certificate),
        (Word(@"декларац"), Declaration),
        (Word(@"отчет"), Report),
        (Word(@"приказ"), Order),
        (Word(@"смет"), Estimate),
        (Word(@"каталог"), Catalog),
    ];

    private static Regex Word(string pattern) =>
        new(@"(?<![а-яa-z])(?:" + pattern + ")", RegexOptions.Compiled);

    /// <summary>
    /// Тип документа по пути файла и названию; пустая строка — тип не определился.
    /// <paramref name="relativeFolder"/> — папка файла внутри источника: тип по папке берется
    /// только из нее, а не из пути самого источника («D:\Договоры\Проект» не делает договором
    /// каждый чертеж проекта). <paramref name="title"/> — название из свойств PDF, если файл
    /// прочитан.
    /// </summary>
    public static string Of(string fileName, string? relativeFolder = null, string? title = null)
    {
        var name = StripExtension(fileName ?? "");

        var byCode = FromCode(name, out var bare);
        if (byCode.Length > 0) return byCode;

        var byName = ByWords(name, NameWords);
        if (byName.Length > 0) return byName;

        if (!string.IsNullOrWhiteSpace(title))
        {
            var byTitle = ByWords(title, NameWords);
            if (byTitle.Length > 0) return byTitle;
        }

        if (!string.IsNullOrEmpty(relativeFolder))
        {
            // Ближняя папка важнее дальней: «Проект\Паспорта» — паспорт.
            var parts = relativeFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var byFolder = ByWords(parts[i], FolderWords);
                if (byFolder.Length > 0) return byFolder;
            }
        }

        return bare ? Drawing : "";
    }

    /// <summary>Тип по коду документа ЕСКД в имени файла; пусто — кода нет.</summary>
    public static string FromCode(string name) => FromCode(name, out _);

    // bare — в имени обозначение без кода: чертеж детали или спецификация, решают другие признаки.
    private static string FromCode(string name, out bool bare)
    {
        bare = false;
        var m = Designation.Match(name);
        if (m.Success)
        {
            var code = m.Groups["code"].Value;
            if (code.Length == 0) { bare = true; return ""; }
            var type = TypeOfCode(code);
            if (type.Length > 0) return type;
        }

        // Код последним словом имени: «ЦНС-38-44_РЭ», «Насос 1Д200 ПС». Одна буква кодом не
        // считается — «И» или «А» в конце имени слишком часто бывают просто словом.
        var tail = name.Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (tail.Length >= 2 && tail.All(c => c is >= 'А' and <= 'Я' or >= '0' and <= '9'))
            return TypeOfCode(tail);
        return "";
    }

    private static string ByWords(string text, (Regex Pattern, string Type)[] words)
    {
        var folded = Fold(text);
        foreach (var (pattern, type) in words)
            if (pattern.IsMatch(folded)) return type;
        return "";
    }

    private static string TypeOfCode(string code)
    {
        if (Codes.TryGetValue(code, out var type)) return type;
        if (PartCode.Match(code) is { Success: true } part) return Codes[part.Groups[1].Value];
        if (SchemeCode.IsMatch(code)) return Scheme;
        return "";
    }

    private static string StripExtension(string name) =>
        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    // Нижний регистр, «е с точками» → «е», подчеркивания — пробелы.
    private static string Fold(string s) =>
        s.ToLowerInvariant().Replace('ё', 'е').Replace('_', ' ');
}

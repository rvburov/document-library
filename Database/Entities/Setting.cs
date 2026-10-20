namespace PdfLibrary.Database.Entities;

/// <summary>
/// Настройка программы — ключ и значение строкой. Хранятся только ОТЛИЧИЯ от стандарта: нет
/// строки — действует значение по умолчанию из кода (<c>AppSettings</c>).
/// </summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

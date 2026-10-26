using System.ComponentModel;
using PdfLibrary.Core.Models;

namespace PdfLibrary.Controls.DrawnGrid;

/// <summary>Строка служебной таблицы: значения читаются из исходной записи.</summary>
public sealed class TableRowVm : INotifyPropertyChanged
{
    public const string TitleKey = "__title__";
    private readonly Func<string, string> _named;
    public int RecordId { get; }
    public int RowNumber { get; }
    public bool IsAddRow => false;
    public bool IsLoading => false;
    public object? Source { get; }
    public RowStyle CellStyle(string key) => RowStyle.None;
    public string this[string key] => _named(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyCellsChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));

    public static TableRowVm Static(int rowNumber, Func<string, string> valueOf, object? source = null) =>
        new(rowNumber, valueOf, source);

    private TableRowVm(int rowNumber, Func<string, string> valueOf, object? source)
    {
        RecordId     = rowNumber;
        RowNumber    = rowNumber;
        _named       = valueOf;
        Source       = source;
    }

}

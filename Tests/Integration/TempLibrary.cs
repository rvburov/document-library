using PdfLibrary.Database;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// Временная библиотека: свой файл library.db во временной папке. Перед удалением файла пулы
/// подключений сбрасываются (<see cref="SqliteGate.ClearAllPools"/>): пул держит дескриптор и
/// после закрытия подключения, и на Windows файл иначе не удалить.
/// </summary>
public sealed class TempLibrary : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "pl-lib-" + Guid.NewGuid().ToString("N")[..8]);

    public TempLibrary()
    {
        Directory.CreateDirectory(Dir);
        Store = LibraryStore.Open(DbPath);
    }

    public string DbPath => Path.Combine(Dir, LibraryDbContext.FileName);

    public LibraryStore Store { get; private set; }

    /// <summary>Открыть ту же базу заново — как при следующем запуске программы.</summary>
    public LibraryStore Reopen()
    {
        SqliteGate.ClearAllPools();
        return Store = LibraryStore.Open(DbPath);
    }

    /// <summary>Путь к новой временной базе (файла еще нет).</summary>
    public static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"pl-db-{Guid.NewGuid():N}.db");

    /// <summary>Удалить файл базы вместе с WAL и разделяемой памятью.</summary>
    public static void Cleanup(string path)
    {
        SqliteGate.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            try { File.Delete(path + suffix); } catch { /* занят — уберет система */ }
    }

    public void Dispose()
    {
        SqliteGate.ClearAllPools();
        try { Directory.Delete(Dir, recursive: true); } catch { /* занят — уберет система */ }
    }
}

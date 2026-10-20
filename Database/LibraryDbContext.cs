using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PdfLibrary.Database.Entities;

namespace PdfLibrary.Database;

/// <summary>
/// База библиотеки <c>library.db</c>: то, что человек сделал руками, — источники, группы,
/// метки, — список найденных документов и настройки. Прочитанный текст живет отдельно, в
/// пересобираемом кеше <c>library-cache.db</c> (<c>DocTextIndex</c>).
///
/// Контекст короткоживущий: открыли, сделали дело, закрыли (<see cref="LibraryStore"/>).
/// Подключение — через <see cref="SqliteGate"/>, как все подключения SQLite программы.
/// </summary>
public class LibraryDbContext : DbContext
{
    /// <summary>Имя файла базы в папке данных.</summary>
    public const string FileName = "library.db";

    public DbSet<Source>      Sources      { get; set; } = null!;
    public DbSet<SourceGroup> SourceGroups { get; set; } = null!;
    public DbSet<Document>    Documents    { get; set; } = null!;
    public DbSet<Tag>         Tags         { get; set; } = null!;
    public DbSet<DocumentTag> DocumentTags { get; set; } = null!;
    public DbSet<Setting>     Settings     { get; set; } = null!;

    private readonly string _path;

    public LibraryDbContext(string path) => _path = path;

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite(SqliteGate.NewConnection($"Data Source={_path}"), contextOwnsConnection: true);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Source>(e =>
        {
            e.ToTable("sources");
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Path).IsRequired();
            e.HasIndex(x => x.Path).IsUnique();
            // Группу удалили — источники остаются и встают в панель сами по себе.
            e.HasOne(x => x.Group).WithMany(g => g.Sources).HasForeignKey(x => x.GroupId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<SourceGroup>(e =>
        {
            e.ToTable("source_groups");
            e.Property(x => x.Name).IsRequired();
        });

        model.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(x => x.RelativePath).IsRequired();
            // Один файл — одна строка в своем источнике. Ключ сравнивается побайтно: на Linux
            // «Отчет.pdf» и «отчет.pdf» — разные файлы.
            e.HasIndex(x => new { x.SourceId, x.RelativePath }).IsUnique();
            // Источник убрали из библиотеки — его документы и их метки уходят вместе с ним.
            e.HasOne(x => x.Source).WithMany(s => s.Documents).HasForeignKey(x => x.SourceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.Property(x => x.Name).IsRequired();
        });

        model.Entity<DocumentTag>(e =>
        {
            e.ToTable("document_tags");
            e.HasKey(x => new { x.DocumentId, x.TagId });
            e.HasIndex(x => x.TagId);
            e.HasOne(x => x.Document).WithMany(d => d.Tags).HasForeignKey(x => x.DocumentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(t => t.Documents).HasForeignKey(x => x.TagId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
        });
    }
}

/// <summary>Контекст для команд <c>dotnet ef</c> (миграции): база во временной папке.</summary>
public class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args) =>
        new(Path.Combine(Path.GetTempPath(), "pdflibrary-design.db"));
}

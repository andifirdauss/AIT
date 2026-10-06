using AiThread.Common.Entities;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiThread.Data.Memory;

// What MemoryService needs from long memory, whether it lives in the main database or in an external one.
public interface ILongMemoryDb : IAsyncDisposable
{
    DbSet<LongMemory> LongMemories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// Long memory in an external database. There is no Projects table there, so ProjectId is a plain column and the
// project name is looked up in the main database.
public sealed class LongMemoryDbContext(DbContextOptions<LongMemoryDbContext> options) : DbContext(options), ILongMemoryDb
{
    public DbSet<LongMemory> LongMemories => Set<LongMemory>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LongMemory>(e =>
        {
            e.ToTable("LongMemories");
            e.Ignore(x => x.Project);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength);
            e.Property(x => x.ProjectName).HasMaxLength(MemorySchema.ProjectNameLength);
            e.Property(x => x.Label).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.Type });
            e.HasIndex(x => new { x.ProjectKey, x.Type });
        });
    }
}

// Short memory in a relational database instead of Redis.
public sealed class ShortMemoryDbContext(DbContextOptions<ShortMemoryDbContext> options) : DbContext(options)
{
    public DbSet<ShortMemoryRow> ShortMemories => Set<ShortMemoryRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortMemoryRow>(e =>
        {
            e.ToTable("ShortMemories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength);
            e.Property(x => x.ProjectName).HasMaxLength(MemorySchema.ProjectNameLength);
            e.Property(x => x.Label).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.HasIndex(x => x.CreatedAt);
        });
    }
}

public sealed class ShortMemoryRow
{
    public Guid Id { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectKey { get; set; }
    public string? ProjectName { get; set; }
    public MemoryType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? SourceThreadId { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Stored as UTC both ways: PostgreSQL rejects a non-UTC DateTime for timestamptz, and reads must carry the "Z" suffix.
internal sealed class MemoryUtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

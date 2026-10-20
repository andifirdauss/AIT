using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIThread.Data.Memory;

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
            MemorySchema.ConfigureLabel(e);
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
            e.Property(x => x.Module).HasMaxLength(MemorySchema.ModuleLength);
            e.Property(x => x.ExtraModules).HasMaxLength(MemorySchema.ExtraModulesLength);
            e.Property(x => x.Layers).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Area).HasMaxLength(MemorySchema.ModuleLength);
            e.Property(x => x.Keywords).HasMaxLength(MemorySchema.KeywordsLength);
            e.Property(x => x.LegacyLabel).HasMaxLength(300);
            e.Property(x => x.ReviewReason).HasMaxLength(500);
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
    public string? Module { get; set; }
    public string? ExtraModules { get; set; }
    public string? Layers { get; set; }
    public MemoryKind? Kind { get; set; }
    public string? Area { get; set; }
    public string? Keywords { get; set; }
    public int? LabelVersion { get; set; }
    public string? LegacyLabel { get; set; }
    public string? ReviewReason { get; set; }
    public int? RelabelAttempts { get; set; }
}

// What the module registry, the code map and the merge log need, whether they live in the main database or in the
// external long memory database (they follow long memory, so a shared store shares them too).
public interface IMemoryCatalogDb : IAsyncDisposable
{
    DbSet<ProjectModule> ProjectModules { get; }
    DbSet<CodeMapEntry> CodeMapEntries { get; }
    DbSet<ModuleMergeLog> ModuleMergeLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// One mapping for both databases.
public static class MemoryCatalogModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectModule>(e =>
        {
            e.ToTable("ProjectModules");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Name).HasMaxLength(MemorySchema.ModuleLength).IsRequired();
            e.Property(x => x.Aliases).HasMaxLength(1000);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.MergedInto).HasMaxLength(MemorySchema.ModuleLength);
            e.HasIndex(x => new { x.ProjectKey, x.Name }).IsUnique();
        });

        modelBuilder.Entity<CodeMapEntry>(e =>
        {
            e.ToTable("CodeMapEntries");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Path).HasMaxLength(CodeMapEntry.MaxPathLength).IsRequired();
            e.Property(x => x.Module).HasMaxLength(MemorySchema.ModuleLength).IsRequired();
            e.Property(x => x.Layer).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Area).HasMaxLength(MemorySchema.ModuleLength);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.ProjectKey, x.Path }).IsUnique();
            e.HasIndex(x => new { x.ProjectKey, x.Module });
        });

        modelBuilder.Entity<ModuleMergeLog>(e =>
        {
            e.ToTable("ModuleMergeLogs");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.FromModule).HasMaxLength(MemorySchema.ModuleLength).IsRequired();
            e.Property(x => x.IntoModule).HasMaxLength(MemorySchema.ModuleLength).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectKey, x.Status });
        });
    }
}

// The module registry, code map and merge log in an external database, next to LongMemories.
public sealed class MemoryCatalogDbContext(DbContextOptions<MemoryCatalogDbContext> options) : DbContext(options), IMemoryCatalogDb
{
    public DbSet<ProjectModule> ProjectModules => Set<ProjectModule>();
    public DbSet<CodeMapEntry> CodeMapEntries => Set<CodeMapEntry>();
    public DbSet<ModuleMergeLog> ModuleMergeLogs => Set<ModuleMergeLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => MemoryCatalogModel.Configure(modelBuilder);
}

// Stored as UTC both ways: PostgreSQL rejects a non-UTC DateTime for timestamptz, and reads must carry the "Z" suffix.
internal sealed class MemoryUtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Data.Memory;

// What the unit test catalog needs, whether it lives in the main database or next to long memory in an external one
// (it follows long memory like the module list, so a shared store shares the catalog with every developer).
public interface IUnitTestDb : IAsyncDisposable
{
    DbSet<UnitTestCase> UnitTestCases { get; }
    DbSet<UnitTestRun> UnitTestRuns { get; }
    DbSet<UnitTestSettings> UnitTestSettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// One mapping for both databases, so a store can be copied to another without conversion.
public static class UnitTestModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnitTestCase>(e =>
        {
            e.ToTable("UnitTestCases");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.ProjectName).HasMaxLength(MemorySchema.ProjectNameLength);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Purpose).HasMaxLength(2000);
            e.Property(x => x.Scenario).HasMaxLength(4000);
            e.Property(x => x.Module).HasMaxLength(MemorySchema.ModuleLength);
            e.Property(x => x.Layers).HasMaxLength(100);
            e.Property(x => x.Keywords).HasMaxLength(MemorySchema.KeywordsLength);
            e.Property(x => x.TargetFiles).HasMaxLength(4000);
            e.Property(x => x.TargetSymbols).HasMaxLength(1000);
            e.Property(x => x.Framework).HasMaxLength(20).IsRequired();
            e.Property(x => x.TestProject).HasMaxLength(UnitTestCase.MaxPathLength);
            e.Property(x => x.TestFile).HasMaxLength(UnitTestCase.MaxPathLength).IsRequired();
            e.Property(x => x.ClassName).HasMaxLength(300);
            e.Property(x => x.MethodName).HasMaxLength(300).IsRequired();
            e.Property(x => x.Selector).HasMaxLength(UnitTestCase.MaxSelectorLength).IsRequired();
            e.Property(x => x.Requires).HasMaxLength(100);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.ObsoleteReason).HasMaxLength(1000);
            e.Property(x => x.ObsoleteBy).HasMaxLength(200);
            e.Property(x => x.QuarantineReason).HasMaxLength(1000);
            e.Property(x => x.RemovedBy).HasMaxLength(200);
            e.Property(x => x.LastResult).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastRunBy).HasMaxLength(200);
            e.Property(x => x.LastMessage).HasMaxLength(2000);
            e.Property(x => x.LastSkipReason).HasMaxLength(500);
            e.Property(x => x.LastInputHash).HasMaxLength(64);
            e.Property(x => x.SourceHash).HasMaxLength(64);
            e.Property(x => x.SourceCommit).HasMaxLength(64);
            e.Property(x => x.SourceBranch).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectKey, x.Number }).IsUnique();
            e.HasIndex(x => new { x.ProjectKey, x.TestFile });
        });

        modelBuilder.Entity<UnitTestRun>(e =>
        {
            e.ToTable("UnitTestRuns");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Phase).HasMaxLength(20).IsRequired();
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.SkipReason).HasMaxLength(500);
            e.Property(x => x.Machine).HasMaxLength(100);
            e.Property(x => x.RunBy).HasMaxLength(200);
            e.HasIndex(x => new { x.UnitTestCaseId, x.CreatedAt });
            e.HasIndex(x => x.ProjectKey);
        });

        modelBuilder.Entity<UnitTestSettings>(e =>
        {
            e.ToTable("UnitTestSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Authoring).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasIndex(x => x.ProjectKey).IsUnique();
        });
    }
}

// The unit test catalog in an external database, next to LongMemories.
public sealed class UnitTestDbContext(DbContextOptions<UnitTestDbContext> options) : DbContext(options), IUnitTestDb
{
    public DbSet<UnitTestCase> UnitTestCases => Set<UnitTestCase>();
    public DbSet<UnitTestRun> UnitTestRuns => Set<UnitTestRun>();
    public DbSet<UnitTestSettings> UnitTestSettings => Set<UnitTestSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => UnitTestModel.Configure(modelBuilder);
}

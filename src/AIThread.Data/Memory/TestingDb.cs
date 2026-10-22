using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Data.Memory;

// The test accounts and testing settings of the projects, in the main database or next to long memory in an external
// one (they follow long memory like the unit test catalog, so a shared store shares them with every developer).
public interface ITestingDb : IAsyncDisposable
{
    DbSet<TestAccount> TestAccounts { get; }
    DbSet<TestingSettings> TestingSettings { get; }
    DbSet<TestingScenario> TestingScenarios { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// One mapping for both databases, so a store can be copied to another without conversion.
public static class TestingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAccount>(e =>
        {
            e.ToTable("TestAccounts");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Username).HasMaxLength(TestAccount.MaxUsernameLength).IsRequired();
            e.Property(x => x.Role).HasMaxLength(TestAccount.MaxRoleLength);
            e.Property(x => x.Attributes).HasMaxLength(TestAccount.MaxAttributesLength);
            e.Property(x => x.Notes).HasMaxLength(TestAccount.MaxNotesLength);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectKey, x.Username }).IsUnique();
        });

        modelBuilder.Entity<TestingSettings>(e =>
        {
            e.ToTable("TestingSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.DevPassword).HasMaxLength(TestingSettings.MaxDevPasswordLength);
            e.Property(x => x.DevPasswordSource).HasMaxLength(20);
            e.Property(x => x.Impersonation).HasMaxLength(TestingSettings.MaxImpersonationLength);
            e.Property(x => x.SideEffectsAcknowledgedBy).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.Ignore(x => x.EffectiveDevPassword);
            e.HasIndex(x => x.ProjectKey).IsUnique();
        });

        modelBuilder.Entity<TestingScenario>(e =>
        {
            e.ToTable("TestingScenarios");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(TestingScenario.MaxSlugLength).IsRequired();
            e.Property(x => x.Title).HasMaxLength(TestingScenario.MaxTitleLength).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Level).HasMaxLength(10).IsRequired();
            e.Property(x => x.BaseUrl).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.LastRunBy).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectKey, x.Slug }).IsUnique();
        });
    }
}

// The testing data in an external database, next to LongMemories.
public sealed class TestingDbContext(DbContextOptions<TestingDbContext> options) : DbContext(options), ITestingDb
{
    public DbSet<TestAccount> TestAccounts => Set<TestAccount>();
    public DbSet<TestingSettings> TestingSettings => Set<TestingSettings>();
    public DbSet<TestingScenario> TestingScenarios => Set<TestingScenario>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => TestingModel.Configure(modelBuilder);
}

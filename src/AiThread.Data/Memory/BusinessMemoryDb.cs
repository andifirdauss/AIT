using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AiThread.Data.Memory;

// What the business wiki needs, whether it lives in the main database or in an external (shared) one.
public interface IBusinessMemoryDb : IAsyncDisposable
{
    DbSet<BusinessPage> BusinessPages { get; }
    DbSet<BusinessPageRevision> BusinessPageRevisions { get; }
    DbSet<BusinessAsset> BusinessAssets { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// One mapping for both databases, so a store can be copied to another without conversion. There is no Projects table
// in an external store; pages are keyed by ProjectKey.
public static class BusinessMemoryModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessPage>(e =>
        {
            e.ToTable("BusinessPages");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.ProjectName).HasMaxLength(MemorySchema.ProjectNameLength);
            e.Property(x => x.Module).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Labels).HasMaxLength(500).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
            e.Property(x => x.LastEditKind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.ProjectKey, x.Slug }).IsUnique();
        });

        modelBuilder.Entity<BusinessPageRevision>(e =>
        {
            e.ToTable("BusinessPageRevisions");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.ChangeNote).HasMaxLength(1000);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.EditKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceThreadIds).HasMaxLength(2000);
            e.HasIndex(x => new { x.PageId, x.Version });
        });

        modelBuilder.Entity<BusinessAsset>(e =>
        {
            e.ToTable("BusinessAssets");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.MediaType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Data).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Caption).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectKey, x.Sha256 });
            e.HasIndex(x => x.PageId);
        });
    }
}

// The business wiki in an external database.
public sealed class BusinessMemoryDbContext(DbContextOptions<BusinessMemoryDbContext> options) : DbContext(options), IBusinessMemoryDb
{
    public DbSet<BusinessPage> BusinessPages => Set<BusinessPage>();
    public DbSet<BusinessPageRevision> BusinessPageRevisions => Set<BusinessPageRevision>();
    public DbSet<BusinessAsset> BusinessAssets => Set<BusinessAsset>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => BusinessMemoryModel.Configure(modelBuilder);
}

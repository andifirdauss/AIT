using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Data.Memory;

// The git state of the threads' changes (ThreadChange), in the main database or next to long memory in an external one
// (it follows long memory like the unit test catalog, so a shared store shares it with every developer).
public interface IGitTrackingDb : IAsyncDisposable
{
    DbSet<ThreadChange> ThreadChanges { get; }
    DbSet<ProjectEnvironment> ProjectEnvironments { get; }
    DbSet<ChangePresence> ChangePresences { get; }
    DbSet<CommitIngestSettings> CommitIngestSettings { get; }
    DbSet<IngestedCommit> IngestedCommits { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// One mapping for both databases, so a store can be copied to another without conversion.
public static class GitTrackingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThreadChange>(e =>
        {
            e.ToTable("ThreadChanges");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.ProjectName).HasMaxLength(MemorySchema.ProjectNameLength);
            e.Property(x => x.Title).HasMaxLength(ThreadChange.MaxTitleLength);
            e.Property(x => x.GitAuthor).HasMaxLength(MemorySchema.GitAuthorLength);
            e.Property(x => x.GitBranch).HasMaxLength(MemorySchema.GitBranchLength);
            e.Property(x => x.GitBaseCommit).HasMaxLength(MemorySchema.GitCommitLength);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Commits).HasMaxLength(1000);
            e.Property(x => x.UpdatedBy).HasMaxLength(MemorySchema.GitAuthorLength);
            e.HasIndex(x => x.ProjectKey);
        });

        modelBuilder.Entity<ProjectEnvironment>(e =>
        {
            e.ToTable("ProjectEnvironments");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Name).HasMaxLength(ProjectEnvironment.MaxNameLength).IsRequired();
            e.Property(x => x.Ref).HasMaxLength(ProjectEnvironment.MaxRefLength).IsRequired();
            e.Property(x => x.UpdatedBy).HasMaxLength(MemorySchema.GitAuthorLength);
            e.HasIndex(x => new { x.ProjectKey, x.Name }).IsUnique();
        });

        modelBuilder.Entity<ChangePresence>(e =>
        {
            e.ToTable("ChangePresences");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(ProjectEnvironment.MaxNameLength).IsRequired();
            e.Property(x => x.Commit).HasMaxLength(MemorySchema.GitCommitLength);
            e.Property(x => x.SeenBy).HasMaxLength(MemorySchema.GitAuthorLength);
            e.HasIndex(x => new { x.ThreadId, x.Environment }).IsUnique();
            e.HasIndex(x => new { x.ProjectKey, x.Environment });
        });

        modelBuilder.Entity<CommitIngestSettings>(e =>
        {
            e.ToTable("CommitIngestSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Ref).HasMaxLength(ProjectEnvironment.MaxRefLength);
            e.Property(x => x.UpdatedBy).HasMaxLength(MemorySchema.GitAuthorLength);
            e.HasIndex(x => x.ProjectKey).IsUnique();
        });

        modelBuilder.Entity<IngestedCommit>(e =>
        {
            e.ToTable("IngestedCommits");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProjectKey).HasMaxLength(MemorySchema.ProjectKeyLength).IsRequired();
            e.Property(x => x.Commit).HasMaxLength(MemorySchema.GitCommitLength).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(ThreadChange.MaxTitleLength);
            e.Property(x => x.Author).HasMaxLength(MemorySchema.GitAuthorLength);
            e.Property(x => x.ClaimedBy).HasMaxLength(MemorySchema.GitAuthorLength);
            e.Property(x => x.Digest).HasMaxLength(IngestedCommit.MaxDigestLength);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProjectKey, x.Commit }).IsUnique();
            e.HasIndex(x => new { x.ProjectKey, x.Status, x.BusinessDigestedAt });
        });
    }
}

// The git tracking data in an external database, next to LongMemories.
public sealed class GitTrackingDbContext(DbContextOptions<GitTrackingDbContext> options) : DbContext(options), IGitTrackingDb
{
    public DbSet<ThreadChange> ThreadChanges => Set<ThreadChange>();
    public DbSet<ProjectEnvironment> ProjectEnvironments => Set<ProjectEnvironment>();
    public DbSet<ChangePresence> ChangePresences => Set<ChangePresence>();
    public DbSet<CommitIngestSettings> CommitIngestSettings => Set<CommitIngestSettings>();
    public DbSet<IngestedCommit> IngestedCommits => Set<IngestedCommit>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<MemoryUtcDateTimeConverter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => GitTrackingModel.Configure(modelBuilder);
}

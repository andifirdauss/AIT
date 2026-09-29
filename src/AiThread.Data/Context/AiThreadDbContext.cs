using AiThread.Common.Entities;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiThread.Data.Context;

public class AiThreadDbContext(DbContextOptions<AiThreadDbContext> options) : DbContext(options)
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AiNode> Nodes => Set<AiNode>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ThreadRun> Threads => Set<ThreadRun>();
    public DbSet<ThreadTask> ThreadTasks => Set<ThreadTask>();
    public DbSet<LongMemory> LongMemories => Set<LongMemory>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Everything is stored as UTC; restore the Kind on read so JSON carries the "Z" suffix.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>(e =>
        {
            e.ToTable("SystemSettings");
            e.Property(x => x.NineRouterBaseUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.ProxyApiKey).HasMaxLength(200);
            e.Property(x => x.PassthroughModel).HasMaxLength(200);
        });

        modelBuilder.Entity<AiNode>(e =>
        {
            e.ToTable("Nodes");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Backend).HasConversion<string>().HasMaxLength(20).HasDefaultValue(NodeBackend.Router);
            e.Property(x => x.Model).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.Role, x.IsActive });
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.SolutionPath).HasMaxLength(800).IsRequired();
            e.Property(x => x.RootPath).HasMaxLength(800).IsRequired();
            e.HasIndex(x => x.SolutionPath).IsUnique();
        });

        modelBuilder.Entity<ThreadRun>(e =>
        {
            e.ToTable("Threads");
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SessionId).HasMaxLength(100);
            e.Property(x => x.WorkingDirectory).HasMaxLength(800);
            e.Property(x => x.PlanSummary).HasMaxLength(2000);
            e.Property(x => x.PlannerModel).HasMaxLength(200);
            e.Property(x => x.WrapperModel).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany(p => p.Threads).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.SessionId);
        });

        modelBuilder.Entity<ThreadTask>(e =>
        {
            e.ToTable("ThreadTasks");
            e.Property(x => x.TaskKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.FilePath).HasMaxLength(800).IsRequired();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.Property(x => x.DependsOn).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NodeName).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(200);
            e.HasOne(x => x.ThreadRun).WithMany(t => t.Tasks).HasForeignKey(x => x.ThreadRunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LongMemory>(e =>
        {
            e.ToTable("LongMemories");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Label).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.Type });
        });

        modelBuilder.Entity<ActivityLog>(e =>
        {
            e.ToTable("ActivityLogs");
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.ThreadId);
        });
    }
}

internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

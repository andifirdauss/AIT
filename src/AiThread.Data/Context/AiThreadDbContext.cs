using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiThread.Data.Context;

public class AiThreadDbContext(DbContextOptions<AiThreadDbContext> options) : DbContext(options), ILongMemoryDb
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AiNode> Nodes => Set<AiNode>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ThreadRun> Threads => Set<ThreadRun>();
    public DbSet<SessionPack> SessionPacks => Set<SessionPack>();
    public DbSet<ThreadTask> ThreadTasks => Set<ThreadTask>();
    public DbSet<ThreadFileSnapshot> ThreadFileSnapshots => Set<ThreadFileSnapshot>();
    public DbSet<LongMemory> LongMemories => Set<LongMemory>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<TelegramChat> TelegramChats => Set<TelegramChat>();
    public DbSet<SkillSource> SkillSources => Set<SkillSource>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<NodeSkill> NodeSkills => Set<NodeSkill>();

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
            e.Property(x => x.SkillOrchestratorModel).HasMaxLength(200);
            e.OwnsOne(x => x.ShortMemoryStore, ConfigureMemoryStore);
            e.OwnsOne(x => x.LongMemoryStore, ConfigureMemoryStore);
            e.Navigation(x => x.ShortMemoryStore).IsRequired();
            e.Navigation(x => x.LongMemoryStore).IsRequired();
        });

        modelBuilder.Entity<AiNode>(e =>
        {
            e.ToTable("Nodes");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Model).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.Role, x.IsActive });
            // SQL Server refuses SET NULL on a self reference; NodeService clears references before deleting a node.
            e.HasOne<AiNode>().WithMany().HasForeignKey(x => x.FallbackNodeId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.RootPath).HasMaxLength(800).IsRequired();
            e.HasIndex(x => x.RootPath).IsUnique();
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
            e.Property(x => x.ReviewerModel).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany(p => p.Threads).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.SessionId);
        });

        modelBuilder.Entity<SessionPack>(e =>
        {
            e.ToTable("SessionPacks");
            e.Property(x => x.SessionId).HasMaxLength(100).IsRequired();
            e.Property(x => x.WorkingDirectory).HasMaxLength(800).IsRequired();
            e.Property(x => x.HandedToSessionId).HasMaxLength(100);
            e.HasIndex(x => new { x.WorkingDirectory, x.CreatedAt });
            e.HasIndex(x => x.HandedToSessionId);
        });

        modelBuilder.Entity<ThreadTask>(e =>
        {
            e.ToTable("ThreadTasks");
            e.Property(x => x.TaskKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.FilePath).HasMaxLength(800).IsRequired();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.Property(x => x.TargetPath).HasMaxLength(800);
            e.Property(x => x.DependsOn).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NodeName).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(200);
            e.HasOne(x => x.ThreadRun).WithMany(t => t.Tasks).HasForeignKey(x => x.ThreadRunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ThreadFileSnapshot>(e =>
        {
            e.ToTable("ThreadFileSnapshots");
            e.Property(x => x.FilePath).HasMaxLength(800).IsRequired();
            e.Property(x => x.OriginalHash).HasMaxLength(64);
            e.Property(x => x.AfterHash).HasMaxLength(64);
            e.HasOne(x => x.ThreadRun).WithMany().HasForeignKey(x => x.ThreadRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ThreadRunId, x.FilePath }).IsUnique();
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

        modelBuilder.Entity<TelegramChat>(e =>
        {
            e.ToTable("TelegramChats");
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Username).HasMaxLength(100);
            e.HasOne(x => x.ActiveProject).WithMany().HasForeignKey(x => x.ActiveProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ChatId).IsUnique();
        });

        modelBuilder.Entity<SkillSource>(e =>
        {
            e.ToTable("SkillSources");
            e.Property(x => x.Url).HasMaxLength(800).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Ref).HasMaxLength(200);
            e.Property(x => x.SubPath).HasMaxLength(500);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.Url).IsUnique();
        });

        modelBuilder.Entity<Skill>(e =>
        {
            e.ToTable("Skills");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Tags).HasMaxLength(500);
            e.Property(x => x.RelativeDir).HasMaxLength(500).IsRequired();
            e.Property(x => x.EntryFile).HasMaxLength(200).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.SkillSource).WithMany(s => s.Skills).HasForeignKey(x => x.SkillSourceId).OnDelete(DeleteBehavior.Cascade);
            // A folder may hold several skill files (e.g. agents/*.md), so the entry file is part of the key.
            e.HasIndex(x => new { x.SkillSourceId, x.RelativeDir, x.EntryFile }).IsUnique();
        });

        modelBuilder.Entity<NodeSkill>(e =>
        {
            e.ToTable("NodeSkills");
            e.HasKey(x => new { x.AiNodeId, x.SkillId });
            e.HasOne(x => x.AiNode).WithMany(n => n.Skills).HasForeignKey(x => x.AiNodeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Skill).WithMany(s => s.Nodes).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.SkillId);
        });
    }

    // Columns ShortMemoryStore_* / LongMemoryStore_* of SystemSettings.
    private static void ConfigureMemoryStore(OwnedNavigationBuilder<SystemSetting, MemoryStoreSetting> b)
    {
        b.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Host).HasMaxLength(255);
        b.Property(x => x.Database).HasMaxLength(500);
        b.Property(x => x.Username).HasMaxLength(200);
        b.Property(x => x.Options).HasMaxLength(1000);
    }
}

internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

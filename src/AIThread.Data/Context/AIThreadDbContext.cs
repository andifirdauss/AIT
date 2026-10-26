using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIThread.Data.Context;

public class AIThreadDbContext(DbContextOptions<AIThreadDbContext> options) : DbContext(options), ILongMemoryDb, IBusinessMemoryDb, IMemoryCatalogDb, IUnitTestDb, ITestingDb, IGitTrackingDb
{
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AiNode> Nodes => Set<AiNode>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ThreadRun> Threads => Set<ThreadRun>();
    public DbSet<SessionPack> SessionPacks => Set<SessionPack>();
    public DbSet<ThreadTask> ThreadTasks => Set<ThreadTask>();
    public DbSet<ThreadFileSnapshot> ThreadFileSnapshots => Set<ThreadFileSnapshot>();
    public DbSet<ThreadAttachment> ThreadAttachments => Set<ThreadAttachment>();
    public DbSet<LongMemory> LongMemories => Set<LongMemory>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<TelegramChat> TelegramChats => Set<TelegramChat>();
    public DbSet<SkillSource> SkillSources => Set<SkillSource>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<NodeSkill> NodeSkills => Set<NodeSkill>();
    public DbSet<PlannerToolDelegation> PlannerToolDelegations => Set<PlannerToolDelegation>();
    public DbSet<BusinessPage> BusinessPages => Set<BusinessPage>();
    public DbSet<BusinessPageRevision> BusinessPageRevisions => Set<BusinessPageRevision>();
    public DbSet<BusinessAsset> BusinessAssets => Set<BusinessAsset>();
    public DbSet<ProjectModule> ProjectModules => Set<ProjectModule>();
    public DbSet<CodeMapEntry> CodeMapEntries => Set<CodeMapEntry>();
    public DbSet<ModuleMergeLog> ModuleMergeLogs => Set<ModuleMergeLog>();
    public DbSet<CodeFunctionScan> CodeFunctionScans => Set<CodeFunctionScan>();
    public DbSet<CodeFunctionCall> CodeFunctionCalls => Set<CodeFunctionCall>();
    public DbSet<UnitTestCase> UnitTestCases => Set<UnitTestCase>();
    public DbSet<UnitTestRun> UnitTestRuns => Set<UnitTestRun>();
    public DbSet<UnitTestSettings> UnitTestSettings => Set<UnitTestSettings>();
    public DbSet<TestAccount> TestAccounts => Set<TestAccount>();
    public DbSet<TestingSettings> TestingSettings => Set<TestingSettings>();
    public DbSet<TestingScenario> TestingScenarios => Set<TestingScenario>();
    public DbSet<ThreadChange> ThreadChanges => Set<ThreadChange>();
    public DbSet<ProjectEnvironment> ProjectEnvironments => Set<ProjectEnvironment>();
    public DbSet<ChangePresence> ChangePresences => Set<ChangePresence>();
    public DbSet<BusinessEnvPage> BusinessEnvPages => Set<BusinessEnvPage>();

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
            e.Property(x => x.UiLanguage).HasMaxLength(10).IsRequired().HasDefaultValue("id");
            e.Property(x => x.ProxyApiKey).HasMaxLength(200);
            e.Property(x => x.PassthroughModel).HasMaxLength(200);
            e.Property(x => x.SkillOrchestratorModel).HasMaxLength(200);
            e.Property(x => x.BusinessMemoryModel).HasMaxLength(200);
            e.Property(x => x.DeveloperName).HasMaxLength(200);
            e.OwnsOne(x => x.ShortMemoryStore, ConfigureMemoryStore);
            e.OwnsOne(x => x.LongMemoryStore, ConfigureMemoryStore);
            e.OwnsOne(x => x.BusinessMemoryStore, ConfigureMemoryStore);
            e.Navigation(x => x.ShortMemoryStore).IsRequired();
            e.Navigation(x => x.LongMemoryStore).IsRequired();
            e.Navigation(x => x.BusinessMemoryStore).IsRequired();
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
            e.Property(x => x.ProjectKey).HasMaxLength(300).IsRequired();
            e.Property(x => x.ProjectKeySource).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ManualKey).HasMaxLength(300);
            e.HasIndex(x => x.RootPath).IsUnique();
            // Not unique: two clones of one repository on the same machine share their key.
            e.HasIndex(x => x.ProjectKey);
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
            e.Property(x => x.TesterModel).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany(p => p.Threads).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => new { x.Status, x.BusinessDigestedAt });
            e.Property(x => x.GitBranch).HasMaxLength(MemorySchema.GitBranchLength);
            e.Property(x => x.GitBaseCommit).HasMaxLength(MemorySchema.GitCommitLength);
            e.Property(x => x.GitAuthor).HasMaxLength(MemorySchema.GitAuthorLength);
            e.Property(x => x.GitState).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.GitCommits).HasMaxLength(1000);
        });

        modelBuilder.Entity<ThreadAttachment>(e =>
        {
            e.ToTable("ThreadAttachments");
            e.Property(x => x.MediaType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Data).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.ThreadRun).WithMany().HasForeignKey(x => x.ThreadRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ThreadRunId);
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
            e.Property(x => x.AfterBlob).HasMaxLength(MemorySchema.GitCommitLength);
            e.Property(x => x.GitState).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.GitCommit).HasMaxLength(MemorySchema.GitCommitLength);
            e.HasOne(x => x.ThreadRun).WithMany().HasForeignKey(x => x.ThreadRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ThreadRunId, x.FilePath }).IsUnique();
        });

        modelBuilder.Entity<LongMemory>(e =>
        {
            e.ToTable("LongMemories");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProjectKey).HasMaxLength(300);
            e.Property(x => x.ProjectName).HasMaxLength(200);
            e.Property(x => x.Label).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).IsRequired();
            MemorySchema.ConfigureLabel(e);
            MemorySchema.ConfigureGit(e);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.Type });
            e.HasIndex(x => new { x.ProjectKey, x.Type });
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

        modelBuilder.Entity<PlannerToolDelegation>(e =>
        {
            e.ToTable("PlannerToolDelegations");
            e.HasKey(x => new { x.PlannerNodeId, x.Tool });
            e.Property(x => x.Tool).HasMaxLength(40);
            e.HasOne(x => x.PlannerNode).WithMany(n => n.ToolDelegations).HasForeignKey(x => x.PlannerNodeId).OnDelete(DeleteBehavior.Cascade);
            // A second cascade path to Nodes is refused by SQL Server; NodeService removes these rows before deleting a tools node.
            e.HasOne(x => x.ToolsNode).WithMany().HasForeignKey(x => x.ToolsNodeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.ToolsNodeId);
        });

        modelBuilder.Entity<CodeFunctionScan>(e =>
        {
            e.ToTable("CodeFunctionScans");
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.Path).HasMaxLength(CodeFunctionScan.MaxPathLength).IsRequired();
            e.Property(x => x.Symbol).HasMaxLength(300).IsRequired();
            e.Property(x => x.Signature).HasMaxLength(400);
            e.Property(x => x.BodyHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.Model).HasMaxLength(200);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.Key }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.Path });
        });

        modelBuilder.Entity<CodeFunctionCall>(e =>
        {
            e.ToTable("CodeFunctionCalls");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Target).HasMaxLength(500).IsRequired();
            e.Property(x => x.ResolvedPath).HasMaxLength(CodeFunctionScan.MaxPathLength);
            e.Property(x => x.ResolvedSymbol).HasMaxLength(300);
            e.HasOne(x => x.CodeFunctionScan).WithMany(s => s.Calls).HasForeignKey(x => x.CodeFunctionScanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.CodeFunctionScanId);
        });

        // Same mapping as an external business memory store (BusinessMemoryDbContext).
        BusinessMemoryModel.Configure(modelBuilder);

        // Same mapping as the catalog next to an external long memory store (MemoryCatalogDbContext).
        MemoryCatalogModel.Configure(modelBuilder);

        // Same mapping as the unit test catalog next to an external long memory store (UnitTestDbContext).
        UnitTestModel.Configure(modelBuilder);

        // Same mapping as the testing data next to an external long memory store (TestingDbContext).
        TestingModel.Configure(modelBuilder);

        // Same mapping as the git tracking data next to an external long memory store (GitTrackingDbContext).
        GitTrackingModel.Configure(modelBuilder);
    }

    // Columns ShortMemoryStore_* / LongMemoryStore_* / BusinessMemoryStore_* of SystemSettings.
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

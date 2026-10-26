using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace AIThread.Data.Memory;

// External memory databases are not migrated: the store creates its own table on first use. EnsureCreated cannot be
// used because it does nothing once the database holds any table (another app's, or the other memory level's).
public static class MemorySchema
{
    public const int ProjectKeyLength = 300;
    public const int ProjectNameLength = 200;
    public const int ModuleLength = 100;
    public const int KeywordsLength = 1000;

    // Columns of the structured label, added to LongMemories/ShortMemories tables made by an older version.
    public static readonly string[] LabelColumns =
    [
        nameof(LongMemory.Module), nameof(LongMemory.Layers), nameof(LongMemory.Kind), nameof(LongMemory.Area),
        nameof(LongMemory.Keywords), nameof(LongMemory.LabelVersion), nameof(LongMemory.LegacyLabel),
        nameof(LongMemory.ReviewReason), nameof(LongMemory.RelabelAttempts), nameof(LongMemory.ExtraModules)
    ];

    // Git origin of the memory (the source thread's branch, commit and author), added to tables made before it.
    public static readonly string[] GitColumns = [nameof(LongMemory.GitAuthor), nameof(LongMemory.GitBranch), nameof(LongMemory.GitCommit)];

    public const int GitAuthorLength = 300;
    public const int GitBranchLength = 250;
    public const int GitCommitLength = 64;

    // Room for MemoryLabels.MaxExtraModules names of ModuleLength plus separators.
    public const int ExtraModulesLength = 310;

    // The structured label columns of long memory, the same in the main and in an external database.
    public static void ConfigureLabel(EntityTypeBuilder<LongMemory> e)
    {
        e.Property(x => x.Module).HasMaxLength(ModuleLength);
        e.Property(x => x.ExtraModules).HasMaxLength(ExtraModulesLength);
        e.Property(x => x.Layers).HasMaxLength(100);
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Area).HasMaxLength(ModuleLength);
        e.Property(x => x.Keywords).HasMaxLength(KeywordsLength);
        e.Property(x => x.LegacyLabel).HasMaxLength(300);
        e.Property(x => x.ReviewReason).HasMaxLength(500);
        e.HasIndex(x => new { x.ProjectKey, x.Module });
    }

    // The git origin columns of long memory, the same in the main and in an external database.
    public static void ConfigureGit(EntityTypeBuilder<LongMemory> e)
    {
        e.Property(x => x.GitAuthor).HasMaxLength(GitAuthorLength);
        e.Property(x => x.GitBranch).HasMaxLength(GitBranchLength);
        e.Property(x => x.GitCommit).HasMaxLength(GitCommitLength);
    }

    // Columns added to a memory table after a release created it: a table made by an older version gets the missing
    // ones (nullable) here, since external stores are not migrated. The SQL comes from the provider's own migrations
    // generator, so it is valid on every supported database.
    public static async Task EnsureColumnsAsync<TEntity>(DbContext db, CancellationToken cancellationToken, params string[] properties)
        where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of {db.GetType().Name}.");
        var table = entityType.GetTableName() ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no table.");
        var schema = entityType.GetSchema();
        var storeObject = StoreObjectIdentifier.Table(table, schema);
        var sql = db.GetService<ISqlGenerationHelper>();

        List<MigrationOperation>? missing = null;
        foreach (var name in properties)
        {
            var property = entityType.FindProperty(name)
                ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{name} does not exist.");
            var column = property.GetColumnName(storeObject) ?? name;

            // Identifiers come from the EF model (not from input) and are delimited by the provider.
            var probe = "SELECT " + sql.DelimitIdentifier(column) + " FROM " + sql.DelimitIdentifier(table, schema) + " WHERE 1 = 0";
            try
            {
                await db.Database.ExecuteSqlRawAsync(probe, cancellationToken);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Column missing (a connection problem fails again below, with the provider's own message).
            }

            (missing ??= []).Add(new AddColumnOperation
            {
                Table = table,
                Schema = schema,
                Name = column,
                ClrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType,
                ColumnType = property.GetRelationalTypeMapping().StoreType,
                MaxLength = property.GetMaxLength(),
                IsNullable = true
            });
        }

        if (missing is null) return;
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(missing))
            await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
    }

    // Tables added to a store after a release created it (the context's other tables exist, so CreateTables would fail):
    // only the missing ones are created, with their indexes, from the context's model by the provider's own migrations
    // SQL generator.
    public static async Task EnsureTablesAsync(DbContext db, CancellationToken cancellationToken, params string[] tables)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            try
            {
                // Names come from the EF model (not from input) and are delimited by the provider.
                await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM " + sql.DelimitIdentifier(table) + " WHERE 1 = 0", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                missing.Add(table);
            }
        }
        if (missing.Count == 0) return;

        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel())
            .Where(o => o is CreateTableOperation create && missing.Contains(create.Name)
                        || o is CreateIndexOperation index && missing.Contains(index.Table))
            .ToList();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
    }

    public static async Task EnsureAsync<TContext>(
        MemoryStoreConnection connection,
        TContext db,
        Func<TContext, CancellationToken, Task> probeTable,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        PrepareLocalFile(connection);
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken)) await creator.CreateAsync(cancellationToken);

        try
        {
            await probeTable(db, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table missing. A connection problem fails here again, with the provider's own message.
            await creator.CreateTablesAsync(cancellationToken);
        }
    }

    // True when the table can be read; false when the database or the table does not exist yet.
    // Connection errors (host, login) are thrown.
    public static async Task<bool> TableExistsAsync<TContext>(
        MemoryStoreConnection connection,
        TContext db,
        Func<TContext, CancellationToken, Task> probeTable,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        if (connection.Provider == MemoryStoreProvider.Sqlite && !File.Exists(SqlitePath(connection))) return false;

        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken)) return false;

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await probeTable(db, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void PrepareLocalFile(MemoryStoreConnection connection)
    {
        if (connection.Provider != MemoryStoreProvider.Sqlite) return;
        var directory = Path.GetDirectoryName(SqlitePath(connection));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    private static string SqlitePath(MemoryStoreConnection connection) => Path.GetFullPath(connection.Database ?? string.Empty);
}

using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // Inserts the memories the new store does not have yet; existing rows (same id) are left alone.
    private async Task<int> CopyLongAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            List<LongMemory> rows;
            await using (var source = await longStore.OpenAsync(from, cancellationToken))
                rows = await source.LongMemories.AsNoTracking().ToListAsync(cancellationToken);
            if (rows.Count == 0) return 0;

            // The main database enforces the project foreign key: a project not registered here is dropped from the
            // row, which stays project memory through its key (older rows without a key turn into knowledge).
            HashSet<Guid> projects;
            await using (var main = await dbFactory.CreateDbContextAsync(cancellationToken))
                projects = (await main.Projects.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();

            await using var target = await longStore.OpenAsync(to, cancellationToken);
            var existing = (await target.LongMemories.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();
            var copied = 0;
            foreach (var batch in rows.Where(r => !existing.Contains(r.Id)).Chunk(CopyBatchSize))
            {
                foreach (var row in batch)
                {
                    var projectId = row.ProjectId is { } pid && projects.Contains(pid) ? pid : (Guid?)null;
                    target.LongMemories.Add(new LongMemory
                    {
                        Id = row.Id,
                        ProjectId = projectId,
                        ProjectKey = row.ProjectKey,
                        ProjectName = row.ProjectName,
                        Type = projectId is null && row.ProjectKey is null ? MemoryType.Knowledge : row.Type,
                        Label = row.Label,
                        Description = row.Description,
                        Module = row.Module,
                        Layers = row.Layers,
                        Kind = row.Kind,
                        Area = row.Area,
                        Keywords = row.Keywords,
                        LabelVersion = row.LabelVersion,
                        LegacyLabel = row.LegacyLabel,
                        ReviewReason = row.ReviewReason,
                        RelabelAttempts = row.RelabelAttempts,
                        SourceThreadId = row.SourceThreadId,
                        GitAuthor = row.GitAuthor,
                        GitBranch = row.GitBranch,
                        GitCommit = row.GitCommit,
                        PromotedAt = row.PromotedAt,
                        HitCount = row.HitCount,
                        LastAccessedAt = row.LastAccessedAt,
                        CreatedAt = row.CreatedAt,
                        UpdatedAt = row.UpdatedAt
                    });
                }
                await target.SaveChangesAsync(cancellationToken);
                copied += batch.Length;
            }
            return copied;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Long memories could not be copied to the new store.");
            warnings.Add(Lang.T($"Long memory lama tidak dapat disalin (sebagian mungkin sudah tersalin): {MemoryStoreTester.ErrorMessage(ex)}", $"The old long memory cannot be copied (part of it may already be copied): {MemoryStoreTester.ErrorMessage(ex)}"));
            return 0;
        }
    }
}

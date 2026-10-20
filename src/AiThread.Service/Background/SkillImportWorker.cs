using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Background;

// Pulls queued skill sources one at a time. Sources still queued or half-pulled when AiThread stopped are pulled again.
public sealed class SkillImportWorker(
    ISkillImportQueue queue,
    ISkillImporter importer,
    ISkillPaths paths,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ILogger<SkillImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let startup (migrations) finish first.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await ResumePendingAsync(stoppingToken);

            await foreach (var sourceId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await importer.ImportAsync(sourceId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Skill import of {SourceId} crashed", sourceId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task ResumePendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            SkillPaths.DeleteDirectory(paths.StagingRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Leftover skill staging folder could not be removed");
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var pending = await db.SkillSources
                .Where(s => s.Status == SkillSourceStatus.Queued || s.Status == SkillSourceStatus.Fetching || s.Status == SkillSourceStatus.Analyzing)
                .OrderBy(s => s.CreatedAt)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in pending) queue.Enqueue(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pending skill sources could not be resumed");
        }
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Every part is computed defensively: the dashboard must render even when 9router, Redis or SQL is down.
public sealed class DashboardService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INineRouterService router,
    INodeService nodeService,
    IMemoryService memoryService,
    IShortMemoryStore shortMemoryStore,
    ThreadRegistry registry,
    ILogger<DashboardService> logger) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var routerStatus = router.LastStatus ?? await Safe<RouterStatusDto?>(async () => await router.CheckStatusAsync(cancellationToken), null);
        var pipeline = await Safe(() => nodeService.GetPipelineStatusAsync(cancellationToken),
            new PipelineStatusDto(false, 0, 0, 0, ["Database tidak dapat diakses."]));
        var memory = await Safe(() => memoryService.GetStatsAsync(cancellationToken),
            new MemoryStatsDto(0, 0, 0, 0, shortMemoryStore.IsConnected));

        var databaseConnected = false;
        int threadsToday = 0, threadsTotal = 0, projectCount = 0;
        double successRate = 0, averageSpeedup = 0;
        long tokensToday = 0;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            databaseConnected = await db.Database.CanConnectAsync(cancellationToken);
            if (databaseConnected)
            {
                var todayUtc = DateTime.Today.ToUniversalTime();
                threadsTotal = await db.Threads.CountAsync(cancellationToken);
                threadsToday = await db.Threads.CountAsync(t => t.CreatedAt >= todayUtc, cancellationToken);
                tokensToday = await db.Threads.Where(t => t.CreatedAt >= todayUtc)
                    .SumAsync(t => (long)t.PlannerTokens + t.ToolsNodeTokens + t.ExecutorTokens + t.ReviewerTokens + t.WrapperTokens, cancellationToken);
                projectCount = await db.Projects.CountAsync(cancellationToken);

                var recentOutcomes = await db.Threads.AsNoTracking()
                    .Where(t => t.Status == ThreadStatus.Completed || t.Status == ThreadStatus.Failed)
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(100)
                    .Select(t => new { t.Status, t.ExecutionWallMs, t.ExecutionSumMs })
                    .ToListAsync(cancellationToken);

                if (recentOutcomes.Count > 0)
                    successRate = recentOutcomes.Count(t => t.Status == ThreadStatus.Completed) * 100.0 / recentOutcomes.Count;

                // Speedup = what sequential execution would have cost / what parallel execution actually took.
                var speedups = recentOutcomes
                    .Where(t => t.ExecutionWallMs > 0 && t.ExecutionSumMs > 0)
                    .Select(t => (double)t.ExecutionSumMs!.Value / t.ExecutionWallMs!.Value)
                    .ToList();
                if (speedups.Count > 0) averageSpeedup = speedups.Average();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard database statistics unavailable.");
        }

        return new DashboardSummaryDto(
            routerStatus,
            shortMemoryStore.IsConnected,
            databaseConnected,
            pipeline,
            threadsToday,
            threadsTotal,
            registry.ActiveCount,
            Math.Round(successRate, 1),
            Math.Round(averageSpeedup, 2),
            tokensToday,
            projectCount,
            memory);
    }

    private async Task<T> Safe<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard component failed.");
            return fallback;
        }
    }
}

using System.Text;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

// A finished thread as the business wiki writer reads it.
public sealed record ThreadDigest(Guid ThreadId, string Text, IReadOnlyList<ImageAttachment> Images);

// The threads of this machine that the business wiki has not seen yet, oldest first, for one project key (all local
// clones of the project together).
public sealed class BusinessDigestCollector(IDbContextFactory<AiThreadDbContext> dbFactory)
{
    public const int MaxThreadsPerBatch = 15;
    public const int MaxCharsPerBatch = 60_000;
    public const int MaxImagesPerBatch = 6;
    private const int MaxPromptChars = 4_000;
    private const int MaxReplyChars = 4_000;
    private const int MaxTasks = 30;

    public async Task<int> CountPendingAsync(string projectKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await Pending(db, projectKey).CountAsync(cancellationToken);
    }

    // Project keys of this machine that have threads waiting.
    public async Task<IReadOnlyList<(string Key, string Name)>> PendingProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Threads.AsNoTracking()
            .Where(t => t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey != "")
            .Select(t => new { t.Project!.ProjectKey, t.Project.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProjectKey).Select(g => (g.Key, g.First().Name)).ToList();
    }

    public async Task<IReadOnlyList<ThreadDigest>> NextBatchAsync(string projectKey, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await Pending(db, projectKey)
            .OrderBy(t => t.CompletedAt ?? t.CreatedAt)
            .Take(MaxThreadsPerBatch)
            .Select(t => new
            {
                t.Id, t.Prompt, t.PlanSummary, t.PlanJson, t.FinalResponse, t.CompletedAt,
                Tasks = t.Tasks.OrderBy(k => k.Order).Select(k => new { k.Title, k.FilePath, k.Action, k.Status, k.Summary }).Take(MaxTasks).ToList()
            })
            .ToListAsync(cancellationToken);
        if (threads.Count == 0) return [];

        var ids = threads.Select(t => t.Id).ToList();
        var attachments = await db.ThreadAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.ThreadRunId))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var digests = new List<ThreadDigest>();
        var chars = 0;
        var images = 0;
        foreach (var thread in threads)
        {
            var text = new StringBuilder();
            text.Append("Request: ").Append(Clip(thread.Prompt, MaxPromptChars)).Append('\n');
            if (!string.IsNullOrWhiteSpace(thread.PlanSummary)) text.Append("Plan: ").Append(thread.PlanSummary).Append('\n');
            if (thread.PlanJson is { } json && ThreadPlan.TryParse(json, out var plan, out _) && plan!.Contract.Count > 0)
            {
                text.Append("Agreed names:\n");
                foreach (var item in plan.Contract.Take(40))
                    text.Append($"- {item.Name}: {item.Value}{(string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" ({item.Note})")}\n");
            }
            if (thread.Tasks.Count > 0)
            {
                text.Append("Changes:\n");
                foreach (var task in thread.Tasks)
                    text.Append($"- [{task.Status}] {task.Action} {task.FilePath}: {task.Summary ?? task.Title}\n");
            }
            if (!string.IsNullOrWhiteSpace(thread.FinalResponse)) text.Append("Final reply: ").Append(Clip(thread.FinalResponse, MaxReplyChars)).Append('\n');

            // A batch always holds at least one thread, however long.
            if (digests.Count > 0 && chars + text.Length > MaxCharsPerBatch) break;
            chars += text.Length;

            var threadImages = attachments
                .Where(a => a.ThreadRunId == thread.Id)
                .Take(Math.Max(0, MaxImagesPerBatch - images))
                .Select(a => new ImageAttachment(a.MediaType, Convert.ToBase64String(a.Data)))
                .ToList();
            images += threadImages.Count;
            digests.Add(new ThreadDigest(thread.Id, text.ToString(), threadImages));
        }
        return digests;
    }

    public async Task MarkDigestedAsync(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken)
    {
        if (threadIds.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await db.Threads.Where(t => threadIds.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.BusinessDigestedAt, now), cancellationToken);
        // Screenshots that mattered are on a page now; the rest is not needed any more.
        await db.ThreadAttachments.Where(a => threadIds.Contains(a.ThreadRunId)).ExecuteDeleteAsync(cancellationToken);
    }

    // Images of threads that never reach the wiki (failed threads, business memory turned off) are not kept forever.
    public async Task<int> PruneAttachmentsAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - maxAge;
        return await db.ThreadAttachments.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    private static IQueryable<Common.Entities.ThreadRun> Pending(AiThreadDbContext db, string projectKey) =>
        db.Threads.AsNoTracking().Where(t =>
            t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey == projectKey);

    private static string Clip(string value, int max) => value.Length > max ? value[..max] + " …" : value;
}

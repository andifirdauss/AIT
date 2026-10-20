using System.Text;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
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
}

using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// PackMessageIndex is the chat message holding the anchor of the thread whose pack was found.
public sealed record ResolvedSession(SessionContext Context, int? PackMessageIndex);

// Keeps a running summary ("context pack") of each Claude Code conversation, so every planner run starts from a small,
// current context instead of the whole chat. The pack written after a thread is stored on that thread, and threads are
// found in the chat through their ThreadAnchor line: a rewound or compacted chat simply resolves to an earlier pack.
public sealed class ContextKeeper(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INodeService nodeService,
    NodeCaller caller,
    NodePromptBuilder prompts,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ContextKeeper> logger)
{
    // Every RebuildEvery-th turn the pack is rebuilt from the thread records rather than from the previous pack, so a
    // summary of summaries cannot drift for long.
    private const int RebuildEvery = 8;
    private const int MaxRebuildTurns = 15;
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(60);

    public async Task<ResolvedSession?> ResolveAsync(Guid projectId, IReadOnlyList<ThreadAnchorRef> anchors, CancellationToken cancellationToken)
    {
        if (anchors.Count == 0) return null;
        var known = await FindThreadsAsync(projectId, anchors.Select(a => a.ShortId), cancellationToken);

        var chain = new List<(Guid Id, bool HasPack, int MessageIndex)>();
        foreach (var anchor in anchors)
        {
            if (known.TryGetValue(anchor.ShortId, out var thread) && chain.All(c => c.Id != thread.Id))
                chain.Add((thread.Id, thread.HasPack, anchor.MessageIndex));
        }
        if (chain.Count == 0) return null;

        var ids = chain.Select(c => c.Id).ToList();
        var packed = chain.FindLastIndex(c => c.HasPack);
        if (packed < 0) return new ResolvedSession(new SessionContext(null, null, ids), null);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var packThreadId = chain[packed].Id;
        var pack = await db.Threads.AsNoTracking()
            .Where(t => t.Id == packThreadId)
            .Select(t => t.ContextPack)
            .FirstOrDefaultAsync(cancellationToken);

        return new ResolvedSession(new SessionContext(packThreadId, pack, ids), chain[packed].MessageIndex);
    }

    // The planner's read_thread tool. Accepts the short id shown in chats and packs, or a full id.
    public async Task<string> ReadThreadAsync(Guid projectId, string id, CancellationToken cancellationToken)
    {
        id = id.Trim().Trim('`').ToLowerInvariant();
        Guid? threadId = Guid.TryParse(id, out var full) ? full : null;
        if (threadId is null && id.Length >= 8)
        {
            var known = await FindThreadsAsync(projectId, [id[..8]], cancellationToken);
            if (known.TryGetValue(id[..8], out var thread)) threadId = thread.Id;
        }
        if (threadId is null) return $"Error: no thread '{id}' in this project. Use an 8-character id from the session context.";

        var turn = (await LoadTurnsAsync(projectId, [threadId.Value], cancellationToken)).FirstOrDefault();
        return turn is null
            ? $"Error: no thread '{id}' in this project."
            : ContextPackBuilder.Describe(turn, 6_000, 8_000);
    }

    // Runs after a Claude Code thread finished (or failed). Never throws: a missing pack only means the next planner
    // run falls back to the recent conversation.
    public async Task UpdateAsync(ThreadRun thread, ThreadRequest request, CancellationToken cancellationToken)
    {
        if (thread.ProjectId is not { } projectId) return;
        try
        {
            var session = request.Session;
            var prior = session?.ThreadIds.Where(id => id != thread.Id).ToList() ?? [];
            var incremental = session?.Pack is not null
                              && prior.Count > 0 && session.PackThreadId == prior[^1]
                              && (prior.Count + 1) % RebuildEvery != 0;

            List<Guid> wanted = incremental ? [thread.Id] : [.. prior.TakeLast(MaxRebuildTurns), thread.Id];
            var turns = await LoadTurnsAsync(projectId, wanted, cancellationToken);
            var current = turns.FirstOrDefault(t => t.ThreadId == thread.Id);
            if (current is null) return;

            var earlier = turns.Where(t => t.ThreadId != thread.Id).ToList();
            var previousPack = incremental ? session!.Pack : null;

            var (pack, author) = await WritePackAsync(
                ContextPackBuilder.Input(previousPack, earlier, request.ConversationContext, current),
                () => ContextPackBuilder.Fallback(previousPack, earlier, current),
                thread.Id, projectId, cancellationToken);

            // ThreadRecorder saves the whole entity on every update, so its copy must not stay without the pack.
            thread.ContextPack = pack;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Threads.Where(t => t.Id == thread.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContextPack, pack), cancellationToken);
            eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated,
                new { thread.Id, thread.Status, thread.PlanSummary, thread.TotalMs, thread.Error }, thread.Id));

            activityLog.Debug("context",
                $"Konteks sesi {(incremental ? "diperbarui" : "disusun ulang dari " + (earlier.Count + 1) + " thread")} oleh {author} ({pack.Length} karakter).",
                thread.Id, projectId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Context keeper failed for thread {ThreadId}", thread.Id);
            activityLog.Warn("context", $"Context Keeper gagal memperbarui konteks sesi: {ex.Message}", thread.Id, projectId);
        }
    }

    // Author describes who wrote the pack, for the activity log. fallback builds a pack without a model.
    public async Task<(string Pack, string Author)> WritePackAsync(
        string input, Func<string> fallback, Guid? threadId, Guid? projectId, CancellationToken cancellationToken)
    {
        // A dedicated node when there is one, otherwise the wrapper (also a cheap 9router model).
        var node = (await nodeService.GetActiveAsync(NodeRole.ContextKeeper, cancellationToken)).FirstOrDefault()
                   ?? (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
        if (node is null) return (fallback(), "ringkasan otomatis (tidak ada node Context Keeper/Wrapper)");

        try
        {
            var reply = await caller.ChatAsync(node, async candidate => new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages =
                [
                    ChatMessages.System(await prompts.BuildAsync(AgentPrompts.ContextKeeper, candidate, cancellationToken)),
                    ChatMessages.User(input)
                ],
                Temperature = candidate.Temperature ?? 0.2,
                MaxTokens = candidate.MaxTokens
            }, cancellationToken, onSwitch: s =>
            {
                activityLog.Warn("context", $"Context Keeper `{s.From.Model}` tidak tersedia ({s.Reason}), beralih ke `{s.To.Model}`.", threadId, projectId);
                return Task.CompletedTask;
            });

            if (ContextPackBuilder.Clean(reply.Result.Content) is { } pack) return (pack, $"{reply.Node.Model}, {reply.Result.TotalTokens} token");
            activityLog.Warn("context", $"Context Keeper `{reply.Node.Model}` tidak mengembalikan ringkasan; memakai ringkasan otomatis.", threadId, projectId);
        }
        catch (NineRouterException ex)
        {
            activityLog.Warn("context", $"Context Keeper `{node.Model}` gagal ({ex.Message}); memakai ringkasan otomatis.", threadId, projectId);
        }
        return (fallback(), "ringkasan otomatis");
    }

    // Short id -> thread of this project. Short ids are only unique in practice, so the search is limited to one
    // project and a recent window.
    private async Task<Dictionary<string, (Guid Id, bool HasPack)>> FindThreadsAsync(Guid projectId, IEnumerable<string> shortIds, CancellationToken cancellationToken)
    {
        var wanted = shortIds.ToHashSet(StringComparer.Ordinal);
        var cutoff = DateTime.UtcNow - LookBack;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Threads.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.CreatedAt >= cutoff)
            .Select(t => new { t.Id, HasPack = t.ContextPack != null })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => wanted.Contains(ThreadAnchor.ShortId(r.Id)))
            .GroupBy(r => ThreadAnchor.ShortId(r.Id))
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().HasPack), StringComparer.Ordinal);
    }

    // In the order of ids; threads of other projects are left out.
    private async Task<List<TurnRecord>> LoadTurnsAsync(Guid projectId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.ProjectId == projectId)
            .Select(t => new { t.Id, t.CreatedAt, t.Status, t.Prompt, t.PlanSummary, t.PlanJson, t.FinalResponse, t.Error })
            .ToListAsync(cancellationToken);
        var tasks = (await db.ThreadTasks.AsNoTracking()
                .Where(t => ids.Contains(t.ThreadRunId))
                .OrderBy(t => t.Order)
                .Select(t => new { t.ThreadRunId, t.TaskKey, t.FilePath, t.TargetPath, t.Action, t.Title, t.Status, t.Summary, t.Error })
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.ThreadRunId);

        var records = new List<TurnRecord>();
        foreach (var id in ids)
        {
            var t = threads.FirstOrDefault(x => x.Id == id);
            if (t is null) continue;
            records.Add(new TurnRecord(
                t.Id,
                t.CreatedAt,
                t.Status.ToString(),
                t.Prompt,
                t.PlanSummary,
                t.PlanJson is not null && ThreadPlan.TryParse(t.PlanJson, out var plan, out _) && plan is not null ? plan.Contract : [],
                tasks[t.Id].Select(x => new TurnTask(x.TaskKey, x.TargetPath is null ? x.FilePath : $"{x.FilePath} → {x.TargetPath}", x.Action, x.Title, x.Status.ToString(), x.Summary ?? x.Error)).ToList(),
                t.FinalResponse,
                t.Error));
        }
        return records;
    }
}

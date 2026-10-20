using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Module and layer of the files the prompt names, for the planner's first memory search layer: stored entries and
    // sure rules now, the model in the background (the memory extraction waits for it). A follow-up that names no file
    // ("berlaku buat yang lainnya juga") continues the session instead: the files the session's last thread changed or
    // named, else the ones the recent conversation or the session summary names. Never fails the thread.
    private async Task<(IReadOnlyList<Memory.CodeMapHit> CodeMap, SessionLabelSource? FromSession, IReadOnlyList<string> Files)> PromptCodeMapAsync(
        ProjectContext project, ThreadRequest request, Guid currentThreadId, CancellationToken cancellationToken)
    {
        try
        {
            var files = await codeMapper.ResolveReferencesAsync(project, request.Prompt, cancellationToken);
            if (files.Count > 0) return (await codeMapper.MapQuicklyAsync(project, files, cancellationToken), null, files);

            var last = await LastSessionThreadAsync(request.Session, currentThreadId, cancellationToken);
            files = last?.Files ?? Array.Empty<string>();
            if (files.Count == 0 && last is not null) files = await codeMapper.ResolveReferencesAsync(project, last.Prompt, cancellationToken);
            if (files.Count == 0) files = await codeMapper.ResolveReferencesAsync(project, request.ConversationContext, cancellationToken);
            if (files.Count == 0) files = await codeMapper.ResolveReferencesAsync(project, request.Session?.Pack, cancellationToken);

            var hits = files.Count == 0 ? [] : await codeMapper.MapQuicklyAsync(project, files.Take(MaxSessionLabelFiles), cancellationToken);
            if (last is null && hits.Count == 0) return ([], null, []);
            return (hits, new SessionLabelSource(last?.Id, last?.Prompt), files);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map of the prompt's files failed.");
            return ([], null, []);
        }
    }
}

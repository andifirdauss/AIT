using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? projectHint, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(workingDirectory);
        var hint = FolderOf(projectHint);
        var key = $"{sessionId}|{directory}|{hint}";
        var now = DateTime.UtcNow;

        if (_sessions.TryGetValue(key, out var decision) && decision.ExpiresAt > now)
        {
            // Ignored sessions keep a fixed expiry so an active session is re-checked periodically.
            if (decision.ProjectId is null) return null;

            // The project may have been deleted from the Project menu since; fall through and resolve again.
            if (await GetContextAsync(decision.ProjectId.Value, cancellationToken) is { } cached)
            {
                _sessions[key] = decision with { ExpiresAt = now + SessionDecisionLifetime };
                return cached;
            }
        }

        PruneExpiredSessions(now);

        // An explicit hint (header) wins; one that leads nowhere falls back to the working directory.
        var lookup = await LookupAsync(hint, cancellationToken);
        if (lookup.Project is null && !string.Equals(directory, hint, StringComparison.OrdinalIgnoreCase))
            lookup = await LookupAsync(directory, cancellationToken);

        var context = lookup.Project is null ? null : ToContext(lookup.Project);
        _sessions[key] = new SessionDecision(context?.ProjectId, now + (context is null ? IgnoredSessionLifetime : SessionDecisionLifetime));

        var sessionLabel = string.IsNullOrEmpty(sessionId) ? "tanpa id" : sessionId[..Math.Min(8, sessionId.Length)];
        if (context is null)
        {
            var subject = directory ?? hint;
            activityLog.Info("proxy",
                $"Sesi {sessionLabel} ({subject ?? "tanpa working directory"}) bukan bagian project ({NoProjectReason(subject, lookup.Resolution)}) — prompt diabaikan orkestrasi dan diteruskan langsung.");
        }
        else
        {
            activityLog.Info("proxy", $"Sesi {sessionLabel} terhubung ke project {context.Name}.", projectId: context.ProjectId);
        }

        return context;
    }
}

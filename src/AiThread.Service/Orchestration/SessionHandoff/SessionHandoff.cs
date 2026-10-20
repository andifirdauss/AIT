using System.Collections.Concurrent;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// Carries a Claude Code conversation across /clear, for any session (also ones not routed through the proxy):
// the SessionEnd hook hands in the transcript of the cleared session, the context keeper summarizes it in the
// background, and the SessionStart hook of the new session in the same directory receives the summary as
// additionalContext. Each pack remembers the session it was handed to, so the next /clear builds on it.
public sealed partial class SessionHandoff(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IContextKeeper contextKeeper,
    IActivityLogService activityLog,
    IHostApplicationLifetime lifetime,
    ILogger<SessionHandoff> logger) : ISessionHandoff
{
    // SessionEnd and SessionStart arrive as separate requests; the first may land a moment after the second.
    private static readonly TimeSpan AppearWait = TimeSpan.FromSeconds(3);

    // Must stay below the SessionStart hook timeout written into settings (ClaudeHookConfig).
    private static readonly TimeSpan PackWait = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

    public const string ContextIntro =
        "AIT Context Keeper — summary of the Claude Code session that was cleared (/clear) in this directory. " +
        "Treat it as your memory of that conversation and verify details in the files before relying on them.";

    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    private sealed record Pending(Guid PackId, DateTime StartedAt, Task<string?> Pack);
}

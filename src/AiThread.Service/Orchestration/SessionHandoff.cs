using System.Collections.Concurrent;
using AiThread.Common.Entities;
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
public sealed class SessionHandoff(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ContextKeeper contextKeeper,
    IActivityLogService activityLog,
    IHostApplicationLifetime lifetime,
    ILogger<SessionHandoff> logger)
{
    // SessionEnd and SessionStart arrive as separate requests; the first may land a moment after the second.
    private static readonly TimeSpan AppearWait = TimeSpan.FromSeconds(3);

    // Must stay below the SessionStart hook timeout written into settings (ClaudeHookConfig).
    private static readonly TimeSpan PackWait = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(10);

    public const string ContextIntro =
        "AiThread Context Keeper — summary of the Claude Code session that was cleared (/clear) in this directory. " +
        "Treat it as your memory of that conversation and verify details in the files before relying on them.";

    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    // SessionEnd with reason "clear". Returns quickly: SessionEnd hooks get a budget of a few seconds.
    public async Task BeginAsync(string sessionId, string transcriptPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var transcript = ValidTranscriptPath(transcriptPath);
        if (transcript is null)
        {
            activityLog.Warn("context", $"Hook /clear diabaikan: transcript di luar folder Claude Code ({transcriptPath}).");
            return;
        }

        var directory = NormalizeDirectory(workingDirectory);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var previous = await db.SessionPacks.AsNoTracking()
            .Where(p => p.HandedToSessionId == sessionId && p.Pack != null)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Pack })
            .FirstOrDefaultAsync(cancellationToken);

        var row = new SessionPack { SessionId = Truncate(sessionId, 100), WorkingDirectory = directory, PreviousPackId = previous?.Id };
        db.SessionPacks.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        var pack = Task.Run(() => WriteAsync(row.Id, transcript, workingDirectory, previous?.Pack, lifetime.ApplicationStopping), CancellationToken.None);
        _pending[directory] = new Pending(row.Id, DateTime.UtcNow, pack);
    }

    // SessionStart with source "clear": the summary for the new session, or null when there is none (in time).
    public async Task<string?> TakeAsync(string newSessionId, string workingDirectory, CancellationToken cancellationToken)
    {
        var directory = NormalizeDirectory(workingDirectory);
        var deadline = DateTime.UtcNow + PackWait;

        Pending? pending;
        var appearUntil = DateTime.UtcNow + AppearWait;
        while (!_pending.TryGetValue(directory, out pending) && DateTime.UtcNow < appearUntil)
            await Task.Delay(100, cancellationToken);

        Guid? packId = null;
        string? pack = null;
        if (pending is not null && DateTime.UtcNow - pending.StartedAt < Freshness)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero && await Task.WhenAny(pending.Pack, Task.Delay(remaining, cancellationToken)) == pending.Pack)
            {
                _pending.TryRemove(new KeyValuePair<string, Pending>(directory, pending));
                pack = await pending.Pack;
                packId = pending.PackId;
            }
            else
            {
                activityLog.Warn("context", $"Ringkasan sesi untuk /clear di {workingDirectory} belum selesai dalam {PackWait.TotalSeconds:0} detik; sesi baru dimulai tanpa ringkasan.");
                return null;
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (pack is null)
        {
            // Nothing in flight (e.g. AiThread restarted): a recent pack for this directory nobody received yet.
            var cutoff = DateTime.UtcNow - Freshness;
            var stored = await db.SessionPacks.AsNoTracking()
                .Where(p => p.WorkingDirectory == directory && p.Pack != null && p.HandedToSessionId == null && p.CreatedAt >= cutoff)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new { p.Id, p.Pack })
                .FirstOrDefaultAsync(cancellationToken);
            if (stored is null) return null;
            (packId, pack) = (stored.Id, stored.Pack);
        }

        var handedTo = Truncate(newSessionId, 100);
        await db.SessionPacks.Where(p => p.Id == packId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.HandedToSessionId, handedTo).SetProperty(p => p.HandedAt, DateTime.UtcNow), cancellationToken);
        activityLog.Success("context", $"Ringkasan sesi sebelumnya diserahkan ke sesi Claude Code baru setelah /clear ({workingDirectory}, {pack!.Length} karakter).");
        return $"{ContextIntro}\n\n{pack}";
    }

    private async Task<string?> WriteAsync(Guid packId, string transcriptPath, string workingDirectory, string? previousPack, CancellationToken cancellationToken)
    {
        try
        {
            var transcript = TranscriptDigest.Build(ReadLines(transcriptPath), workingDirectory);
            if (transcript.Entries == 0 && previousPack is null) return null;

            var (pack, author) = await contextKeeper.WritePackAsync(
                ContextPackBuilder.SessionInput(previousPack, transcript, workingDirectory),
                () => ContextPackBuilder.SessionFallback(previousPack, transcript),
                null, null, cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.SessionPacks.Where(p => p.Id == packId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Pack, pack).SetProperty(p => p.UpdatedAt, DateTime.UtcNow), cancellationToken);
            activityLog.Info("context",
                $"Sesi Claude Code di {workingDirectory} di-/clear: {transcript.Entries} entri transcript diringkas oleh {author} ({pack.Length} karakter).");
            return pack;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not summarize cleared session transcript {Path}", transcriptPath);
            activityLog.Warn("context", $"Ringkasan sesi untuk /clear gagal: {ex.Message}");
            return null;
        }
    }

    // Only transcripts inside Claude Code's own folder: the hook endpoint must not become a way to read any file.
    internal static string? ValidTranscriptPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (!full.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;

        var configDirectories = new[]
        {
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
        };
        return configDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.GetFullPath(Path.Combine(d!, "projects")) + Path.DirectorySeparatorChar)
            .Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            ? full
            : null;
    }

    internal static string NormalizeDirectory(string workingDirectory)
    {
        var full = workingDirectory.Trim();
        try
        {
            full = Path.GetFullPath(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the raw value; it still matches the same session's next hook call.
        }
        full = full.TrimEnd('\\', '/');
        return Truncate(OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full, 800);
    }

    // Claude Code keeps appending to the transcript, so it is opened with full sharing.
    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record Pending(Guid PackId, DateTime StartedAt, Task<string?> Pack);
}

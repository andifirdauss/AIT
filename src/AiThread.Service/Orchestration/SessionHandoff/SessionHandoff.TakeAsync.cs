using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
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
}

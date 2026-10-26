using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
    // SessionEnd with reason "clear". Returns quickly: SessionEnd hooks get a budget of a few seconds.
    public async Task BeginAsync(string sessionId, string transcriptPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var transcript = ValidTranscriptPath(transcriptPath);
        if (transcript is null)
        {
            activityLog.Warn("context", Lang.T($"Hook /clear diabaikan: transcript di luar folder Claude Code ({transcriptPath}).", $"/clear hook ignored: the transcript is outside the Claude Code folder ({transcriptPath})."));
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
}

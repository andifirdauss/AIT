using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
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
                Lang.T($"Sesi Claude Code di {workingDirectory} di-/clear: {transcript.Entries} entri transcript diringkas oleh {author} ({pack.Length} karakter).", $"Claude Code session in {workingDirectory} was /cleared: {transcript.Entries} transcript entries summarized by {author} ({pack.Length} characters)."));
            return pack;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not summarize cleared session transcript {Path}", transcriptPath);
            activityLog.Warn("context", Lang.T($"Ringkasan sesi untuk /clear gagal: {ex.Message}", $"Session summary for /clear failed: {ex.Message}"));
            return null;
        }
    }
}

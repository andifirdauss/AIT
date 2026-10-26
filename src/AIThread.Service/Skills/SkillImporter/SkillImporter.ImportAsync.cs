using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Skills;

public sealed partial class SkillImporter
{
    public async Task ImportAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await SetStatusAsync(sourceId, SkillSourceStatus.Fetching, null, cancellationToken);
        if (source is null) return;

        var staging = Path.Combine(paths.StagingRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var location = SkillSourceLocator.Locate(source.Url);
            activityLog.Info("skill", Lang.T($"Menarik skill dari {source.Name}…", $"Fetching skills from {source.Name}…"));
            var page = await fetcher.FetchAsync(location, staging, cancellationToken);

            await SetStatusAsync(sourceId, SkillSourceStatus.Analyzing, null, cancellationToken);
            var (skills, links) = await DiscoverAsync(source, staging, page, cancellationToken);
            var children = await AddChildSourcesAsync(source, links, cancellationToken);
            if (skills.Count == 0 && children == 0)
                throw new InvalidOperationException(Lang.T("Tidak ada skill yang ditemukan di link ini.", "No skill was found at this link."));

            ReplaceFolder(staging, paths.SourceDir(sourceId));
            var (added, updated, removed) = await SaveSkillsAsync(sourceId, skills, cancellationToken);

            var childNote = children > 0 ? Lang.T($", {children} sumber turunan ditambahkan ke antrean", $", {children} child sources queued") : string.Empty;
            activityLog.Success("skill", Lang.T($"Skill dari {source.Name} siap: {skills.Count} skill ({added} baru, {updated} diperbarui, {removed} dihapus){childNote}.", $"Skills from {source.Name} ready: {skills.Count} skills ({added} new, {updated} updated, {removed} removed){childNote}."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the worker queues it again on the next start.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pulling skill source {SourceId} failed", sourceId);
            await SetStatusAsync(sourceId, SkillSourceStatus.Failed, ex.Message, CancellationToken.None);
            activityLog.Error("skill", Lang.T($"Gagal menarik skill dari {source.Name}: {ex.Message}", $"Fetching skills from {source.Name} failed: {ex.Message}"));
        }
        finally
        {
            progress.Clear(sourceId);
            TryDeleteDirectory(staging);
        }
    }
}

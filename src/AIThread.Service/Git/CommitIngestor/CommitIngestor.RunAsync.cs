using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // One pass over the project key's commits made outside AIT since CommitIngestSettings.Since, oldest first, at most
    // MaxPerRun summarized. Never throws for a commit: a failure is recorded on its row and not tried again.
    public async Task<CommitIngestResultDto> RunAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return new CommitIngestResultDto(0, 0, 0, Lang.T("Ingest commit sedang berjalan.", "Commit ingest is already running."));
        try
        {
            if (await ProjectAsync(projectId, cancellationToken) is not { } project)
                return new CommitIngestResultDto(0, 0, 0, Lang.T("Project tidak ditemukan atau belum punya key.", "Project not found or without a key."));
            var key = project.Key!;
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            var settings = await db.CommitIngestSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectKey == key, cancellationToken);
            if (settings is not { Enabled: true })
                return new CommitIngestResultDto(0, 0, 0, Lang.T("Ingest commit belum dinyalakan untuk project ini.", "Commit ingest is not turned on for this project."));
            if (await EffectiveRefAsync(db, key, settings, project.RootPath, cancellationToken) is not { } reference)
                return new CommitIngestResultDto(0, 0, 0, Lang.T("Ref untuk ingest commit tidak ditemukan di clone ini (fetch dulu, atau atur ref).",
                    "The ref for commit ingest is not in this clone (fetch first, or set the ref)."));

            // Claims this AIT left unfinished (it stopped mid-summary): one pass at a time runs here, so any is stale, and
            // its commit is taken on again below.
            var claimant = Claimant();
            await db.IngestedCommits.Where(c => c.ProjectKey == key && c.Status == IngestStatus.Pending && c.ClaimedBy == claimant)
                .ExecuteDeleteAsync(cancellationToken);

            var since = "--since=" + settings.Since.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var list = await GitProcess.RunAsync(project.RootPath, ["rev-list", "--no-merges", "--reverse", since, reference], cancellationToken);
            if (!list.Ok) return new CommitIngestResultDto(0, 0, 0, list.Error);
            var (known, threadFiles) = await KnownAsync(db, key, cancellationToken);
            var pushed = await IsRemoteAsync(project.RootPath, reference, cancellationToken);

            int ingested = 0, skipped = 0, failed = 0;
            foreach (var sha in GitProcess.Lines(list.Output).Take(MaxScannedCommits).Where(c => !known.Contains(c)))
            {
                if (ingested + failed + skipped >= settings.MaxPerRun) break;
                if (await ReadCommitAsync(project.RootPath, sha, cancellationToken) is not { } commit) continue;

                // An AIT thread's change committed by hand (or squashed) is not summarized twice.
                var own = commit.Files.Count > 0 && commit.Files.All(f => f.Removed || threadFiles.Contains($"{f.Path}|{f.Blob}"));
                var row = new IngestedCommit
                {
                    Id = Guid.NewGuid(), ProjectKey = key, Commit = sha, Status = own || commit.Files.Count == 0 ? IngestStatus.Skipped : IngestStatus.Pending,
                    Title = Clip(commit.Message.Split('\n')[0].Trim(), ThreadChange.MaxTitleLength - 1), Author = Clip(commit.Author, 299),
                    CommittedAt = commit.CommittedAt, ClaimedBy = claimant, Note = own ? "AIT" : commit.Files.Count == 0 ? "no files" : null
                };
                db.IngestedCommits.Add(row);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Another developer's AIT claimed it a moment ago.
                    db.IngestedCommits.Entry(row).State = EntityState.Detached;
                    continue;
                }
                if (row.Status == IngestStatus.Skipped) continue;

                try
                {
                    var (memoryId, digest) = await SummarizeAsync(project, commit, row.Id, cancellationToken);
                    if (memoryId is null)
                    {
                        row.Status = IngestStatus.Skipped;
                        row.Note = "nothing to keep";
                        skipped++;
                    }
                    else
                    {
                        row.Status = IngestStatus.Done;
                        row.MemoryId = memoryId;
                        row.Digest = digest;
                        db.ThreadChanges.Add(new ThreadChange
                        {
                            Id = row.Id, ProjectKey = key, ProjectName = project.Name, Title = row.Title, GitAuthor = row.Author, GitBranch = reference,
                            GitBaseCommit = sha, State = pushed ? GitChangeState.Pushed : GitChangeState.Committed, Commits = sha,
                            Files = ThreadChangeFiles.Write(commit.Files), Ingested = true, UpdatedBy = GitIdentity.UserName
                        });
                        ingested++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Commit {Commit} could not be summarized", sha);
                    row.Status = IngestStatus.Failed;
                    row.Note = Clip(ex.Message, 999);
                    failed++;
                }
                row.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (ingested + failed > 0)
            {
                var message = Lang.T($"Ingest commit {project.Name}: {ingested} commit di luar AIT dirangkum ke memory, {skipped} dilewati, {failed} gagal.",
                    $"Commit ingest {project.Name}: {ingested} commit(s) made outside AIT summarized into memory, {skipped} skipped, {failed} failed.");
                if (failed > 0) activityLog.Warn("git", message, projectId: projectId);
                else activityLog.Success("git", message, projectId: projectId);
                eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { Id = projectId, Action = "ingest" }));
            }
            return new CommitIngestResultDto(ingested, skipped, failed, null);
        }
        finally
        {
            _gate.Release();
        }
    }
}

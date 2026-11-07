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
    // One pass over the project key's commits made outside AIT since CommitIngestSettings.Since, oldest first: the ref's,
    // then the working branch's that are not in the ref yet; at most MaxPerRun summarized (copies and AIT's own commits
    // cost nothing and do not count). Then the working-branch commits this AIT took on are followed in this clone. Never
    // throws for a commit: a failure is recorded on its row and not tried again.
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
            var branch = settings.IgnoreWorkingBranch ? null : await WorkingBranchAsync(project.RootPath, reference, since, cancellationToken);
            var candidates = GitProcess.Lines(list.Output).Take(MaxScannedCommits).Select(c => new Candidate(c, null))
                .Concat(branch?.Commits.Take(MaxScannedCommits).Select(c => new Candidate(c, branch.Name)) ?? [])
                .DistinctBy(c => c.Sha, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var (known, threadFiles) = await KnownAsync(db, key, cancellationToken);
            var summarized = await SummarizedAsync(db, key, cancellationToken);
            var pushed = await IsRemoteAsync(project.RootPath, reference, cancellationToken);

            int ingested = 0, skipped = 0, failed = 0, copies = 0;
            foreach (var candidate in candidates.Where(c => !known.Contains(c.Sha)))
            {
                if (ingested + failed + skipped >= settings.MaxPerRun) break;
                var sha = candidate.Sha;
                if (await ReadCommitAsync(project.RootPath, sha, cancellationToken) is not { } commit) continue;

                // An AIT thread's change committed by hand (or squashed) is not summarized twice, nor is a commit that
                // brings back one summarized before (rebased, cherry-picked, squash merged).
                var own = commit.Files.Count > 0 && commit.Files.All(f => f.Removed || threadFiles.Contains($"{f.Path}|{f.Blob}"));
                string? patch = null;
                IngestCopy? copy = null;
                if (!own && commit.Files.Count > 0)
                {
                    patch = await PatchIdAsync(project.RootPath, sha, cancellationToken);
                    copy = CopyOf(commit, patch, candidate.Branch, summarized, threadFiles);
                }
                var row = new IngestedCommit
                {
                    Id = Guid.NewGuid(), ProjectKey = key, Commit = sha,
                    Status = own || copy is not null || commit.Files.Count == 0 ? IngestStatus.Skipped : IngestStatus.Pending,
                    Title = Clip(commit.Message.Split('\n')[0].Trim(), ThreadChange.MaxTitleLength - 1), Author = Clip(commit.Author, 299),
                    CommittedAt = commit.CommittedAt, ClaimedBy = claimant, Note = own ? "AIT" : copy?.Note ?? (commit.Files.Count == 0 ? "no files" : null),
                    PatchId = patch, Branch = candidate.Branch
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
                if (copy is not null)
                {
                    await JoinAsync(db, copy.ChangeIds, sha, cancellationToken);
                    copies++;
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
                        var state = candidate.Branch is null
                            ? pushed ? GitChangeState.Pushed : GitChangeState.Committed
                            : await StateOfAsync(project.RootPath, [sha], cancellationToken) ?? GitChangeState.Committed;
                        db.ThreadChanges.Add(new ThreadChange
                        {
                            Id = row.Id, ProjectKey = key, ProjectName = project.Name, Title = row.Title, GitAuthor = row.Author,
                            GitBranch = candidate.Branch ?? reference, GitBaseCommit = sha, State = state, Commits = sha,
                            Files = ThreadChangeFiles.Write(commit.Files), Ingested = true, UpdatedBy = GitIdentity.UserName
                        });
                        Remember(summarized, row, commit.Files);
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

            var moved = await RefreshBranchesAsync(db, key, project.RootPath, cancellationToken);
            if (ingested + failed > 0)
            {
                var message = Lang.T($"Ingest commit {project.Name}: {ingested} commit di luar AIT dirangkum ke memory, {skipped} dilewati, {failed} gagal.",
                    $"Commit ingest {project.Name}: {ingested} commit(s) made outside AIT summarized into memory, {skipped} skipped, {failed} failed.");
                if (failed > 0) activityLog.Warn("git", message, projectId: projectId);
                else activityLog.Success("git", message, projectId: projectId);
            }
            if (copies > 0)
                activityLog.Info("git", Lang.T($"Ingest commit {project.Name}: {copies} commit adalah salinan commit yang sudah dirangkum (rebase, cherry-pick, squash), tidak dirangkum ulang.",
                    $"Commit ingest {project.Name}: {copies} commit(s) copy commits summarized before (rebase, cherry-pick, squash), not summarized again."), projectId: projectId);
            if (ingested + failed + copies + moved > 0)
                eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { Id = projectId, Action = "ingest" }));
            return new CommitIngestResultDto(ingested, skipped, failed, null);
        }
        finally
        {
            _gate.Release();
        }
    }
}

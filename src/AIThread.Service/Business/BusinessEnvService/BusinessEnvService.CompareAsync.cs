using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Git;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // Two sides (environment names, or Working) page by page, and the changes only one side holds: for the working wiki
    // every shared change that was not thrown away, for an environment those seen in it. Null when the project is not
    // registered.
    public async Task<BusinessCompareDto?> CompareAsync(Guid projectId, string left, string right, CancellationToken cancellationToken = default)
    {
        if (await KeyAsync(projectId, cancellationToken) is not { Length: > 0 } key) return null;

        List<BusinessCompareItemDto> items;
        await using (var db = await store.CreateAsync(cancellationToken))
        {
            var a = await SideAsync(db, key, left, cancellationToken);
            var b = await SideAsync(db, key, right, cancellationToken);
            items = a.Keys.Union(b.Keys)
                .Select(id =>
                {
                    var l = a.GetValueOrDefault(id);
                    var r = b.GetValueOrDefault(id);
                    var shown = r ?? l!;
                    var status = l is null ? "right" : r is null ? "left" : l.Content == r.Content && l.Title == r.Title ? "same" : "changed";
                    var diff = status == "changed" ? TextDiff.Lines(l!.Content, r!.Content) : [];
                    return new BusinessCompareItemDto(id, shown.Module, shown.Title, status, l?.Version, r?.Version, diff);
                })
                .OrderBy(i => i.Status == "same" ? 1 : 0)
                .ThenBy(i => i.Module)
                .ThenBy(i => i.Title)
                .ToList();
        }

        var changes = new List<BusinessCompareChangeDto>();
        await using (var git = await longStore.CreateGitAsync(cancellationToken))
        {
            var all = await git.ThreadChanges.AsNoTracking()
                .Where(c => c.ProjectKey == key && c.State != GitChangeState.Discarded && c.State != GitChangeState.Lost)
                .OrderByDescending(c => c.CreatedAt)
                .Take(2_000)
                .Select(c => new { c.Id, c.Title, c.GitAuthor, c.GitBranch, c.Commits })
                .ToListAsync(cancellationToken);
            var presence = (await git.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == key)
                    .Select(p => new { p.ThreadId, p.Environment, p.Commit }).ToListAsync(cancellationToken))
                .ToLookup(p => p.Environment, StringComparer.OrdinalIgnoreCase);

            IReadOnlySet<Guid> Holding(string side) => string.Equals(side, Working, StringComparison.OrdinalIgnoreCase)
                ? all.Select(c => c.Id).ToHashSet()
                : presence[side].Select(p => p.ThreadId).ToHashSet();
            string? CommitIn(string side, Guid id) => presence[side].FirstOrDefault(p => p.ThreadId == id)?.Commit;

            var inLeft = Holding(left);
            var inRight = Holding(right);
            foreach (var change in all)
            {
                var side = inLeft.Contains(change.Id) && !inRight.Contains(change.Id) ? "left"
                    : inRight.Contains(change.Id) && !inLeft.Contains(change.Id) ? "right" : null;
                if (side is null) continue;
                var commit = CommitIn(side == "left" ? left : right, change.Id) ?? ThreadChangeFiles.Commits(change.Commits).FirstOrDefault();
                changes.Add(new BusinessCompareChangeDto(change.Id, change.Title, change.GitAuthor, change.GitBranch, side, commit));
                if (changes.Count >= MaxCompareChanges) break;
            }
        }
        return new BusinessCompareDto(left, right, items, changes);
    }
}

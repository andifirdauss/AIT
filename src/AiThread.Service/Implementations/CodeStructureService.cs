using System.Collections.Concurrent;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Keeps the latest snapshot of the few projects viewed recently in memory; one scan at a time per project.
public sealed class CodeStructureService(IProjectService projectService, IHostApplicationLifetime lifetime, ILogger<CodeStructureService> logger)
    : ICodeStructureService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(1);
    private const int KeptProjects = 4;
    private const int MaxFolderFiles = 1_000;
    private const int MaxSearchHits = 100;
    private const int MinSearchLength = 2;

    private readonly ConcurrentDictionary<Guid, Entry> _projects = new();

    public async Task<CodeStructureSummaryDto> GetSummaryAsync(Guid projectId, bool rescan, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, rescan, cancellationToken);
        var parsed = snapshot.Files.Values.Where(f => f.Outline is not null).ToList();
        var languages = parsed
            .GroupBy(f => f.Language!)
            .Select(g => new StructureLanguageDto(g.Key, g.Count(), g.Sum(f => f.Outline!.Types.Count)))
            .OrderByDescending(l => l.Files)
            .ToList();
        return new CodeStructureSummaryDto(
            snapshot.Folders,
            snapshot.Files.Count,
            parsed.Count,
            snapshot.Files.Values.Count(f => f.ThirdParty),
            parsed.Sum(f => f.Outline!.Types.Count),
            parsed.Sum(f => f.Outline!.MemberCount),
            languages,
            snapshot.ScanMs,
            snapshot.Reparsed,
            snapshot.ScannedAt,
            snapshot.Truncated);
    }

    public async Task<StructureFolderDto> GetFolderAsync(Guid projectId, string? path, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var folder = Normalize(path);
        var prefix = folder.Length == 0 ? string.Empty : folder + "/";
        var folders = new Dictionary<string, (int Files, int Types, int ThirdParty)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<StructureFile>();
        foreach (var file in snapshot.Files.Values)
        {
            if (!file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = file.Path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                files.Add(file);
                continue;
            }
            var name = rest[..slash];
            var (count, types, thirdParty) = folders.GetValueOrDefault(name);
            folders[name] = (count + 1, types + (file.Outline?.Types.Count ?? 0), thirdParty + (file.ThirdParty ? 1 : 0));
        }
        if (folder.Length > 0 && folders.Count == 0 && files.Count == 0) throw new AppValidationException($"Folder '{folder}' tidak ada di hasil scan.");

        var folderRows = folders
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => new StructureFolderEntryDto(f.Key, prefix + f.Key, f.Value.Files, f.Value.Types, f.Value.ThirdParty == f.Value.Files))
            .ToList();
        var fileRows = files
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFolderFiles)
            .Select(f => new StructureFileEntryDto(Path.GetFileName(f.Path), f.Path, f.Language, f.Outline?.Types.Count ?? 0, f.Outline?.MemberCount ?? 0,
                f.ThirdParty, f.Outline is not null))
            .ToList();
        return new StructureFolderDto(folder, folderRows, fileRows, Math.Max(0, files.Count - MaxFolderFiles));
    }

    public async Task<StructureFileDto> GetFileAsync(Guid projectId, string path, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var file = snapshot.Files.GetValueOrDefault(Normalize(path)) ?? throw new AppValidationException($"File '{path}' tidak ada di hasil scan.");
        var outline = file.Outline ?? FileOutline.Empty;
        return new StructureFileDto(file.Path, file.Language, file.ThirdParty, file.Outline is not null,
            outline.Types.Select(t => new StructureTypeDto(t.Name, t.Kind, t.Namespace, t.Container, t.Line, t.IsPartial, t.Members.Select(ToDto).ToList())).ToList(),
            outline.Functions.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<StructureGroupDto>> GetGroupsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        return snapshot.Classes.Groups.Select(g => new StructureGroupDto(g.Key, g.Name, g.IsFolder, g.Types)).ToList();
    }

    public async Task<IReadOnlyList<StructureTypeSummaryDto>> GetTypesAsync(Guid projectId, string group, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        return snapshot.Classes.TypesIn(group)
            .Select(t => new StructureTypeSummaryDto(t.Name, t.Container, t.Kind, t.Locations.Count, t.Members.Count, t.IsPartial))
            .ToList();
    }

    public async Task<StructureClassDto> GetClassAsync(Guid projectId, string group, string name, string? container, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var type = snapshot.Classes.Find(group, name, string.IsNullOrEmpty(container) ? null : container)
                   ?? throw new AppValidationException($"Class '{name}' tidak ada di hasil scan.");
        var severalFiles = type.Locations.Count > 1;
        return new StructureClassDto(type.Group, type.Name, type.Container, type.Kind, type.IsPartial,
            type.Locations.Select(l => new StructureLocationDto(l.Path, l.Line)).ToList(),
            type.Members.Select(m => ToDto(m.Member) with { Path = severalFiles ? m.Path : null }).ToList());
    }

    // Types first, then members, then file names; within each, exact name, then prefix, then substring.
    public async Task<IReadOnlyList<StructureSearchHitDto>> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default)
    {
        var text = query.Trim();
        if (text.Length < MinSearchLength) return [];
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var hits = new List<(int Rank, StructureSearchHitDto Hit)>();
        foreach (var file in snapshot.Files.Values)
        {
            if (Rank(Path.GetFileName(file.Path), text) is { } fileRank)
                hits.Add((20 + fileRank, new StructureSearchHitDto("file", Path.GetFileName(file.Path), file.Language, file.Path, 1)));
            if (file.Outline is not { } outline) continue;
            foreach (var type in outline.Types)
            {
                if (Rank(type.Name, text) is { } typeRank)
                    hits.Add((typeRank, new StructureSearchHitDto("type", type.Name, $"{type.Kind}{(type.Namespace is null ? string.Empty : " · " + type.Namespace)}", file.Path, type.Line)));
                foreach (var member in type.Members)
                {
                    if (Rank(member.Name, text) is { } memberRank)
                        hits.Add((10 + memberRank, new StructureSearchHitDto("member", member.Name, $"{type.Name} · {member.Kind}", file.Path, member.Line)));
                }
            }
            foreach (var function in outline.Functions)
            {
                if (Rank(function.Name, text) is { } functionRank)
                    hits.Add((10 + functionRank, new StructureSearchHitDto("member", function.Name, function.Kind, file.Path, function.Line)));
            }
        }
        return hits
            .OrderBy(h => h.Rank)
            .ThenBy(h => h.Hit.Name.Length)
            .ThenBy(h => h.Hit.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchHits)
            .Select(h => h.Hit)
            .ToList();
    }

    // A queue of one per project: a new request cancels the refresh before it (waiting, or already scanning) and
    // takes its place, so threads finishing in several CLIs at once end in one scan of the latest files. The short
    // delay lets a burst of requests collapse before any scanning starts.
    public void Refresh(Guid projectId)
    {
        var entry = _projects.GetOrAdd(projectId, _ => new Entry());
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        lock (entry)
        {
            entry.Refresh?.Cancel();
            entry.Refresh = refresh;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RefreshDelay, refresh.Token);
                await SnapshotAsync(projectId, rescan: true, refresh.Token);
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer request, or shutting down.
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Background refresh of the code structure of project {ProjectId} failed", projectId);
            }
            finally
            {
                // Cancel only ever runs under this lock, so the source is no longer reachable once it is removed here.
                lock (entry)
                {
                    if (entry.Refresh == refresh) entry.Refresh = null;
                }
                refresh.Dispose();
            }
        }, CancellationToken.None);
    }

    private static int? Rank(string name, string text) =>
        name.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
        : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 2
        : null;

    private static StructureMemberDto ToDto(OutlineMember member) => new(member.Name, member.Kind, member.Line, member.Signature);

    private static string Normalize(string? path) => (path ?? string.Empty).Replace('\\', '/').Trim().Trim('/');

    private async Task<StructureSnapshot> SnapshotAsync(Guid projectId, bool rescan, CancellationToken cancellationToken)
    {
        var entry = _projects.GetOrAdd(projectId, _ => new Entry());
        entry.LastUsed = DateTime.UtcNow;
        if (!rescan && entry.Snapshot is { } current && DateTime.UtcNow - current.ScannedAt < FreshFor) return current;

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have scanned while this one waited.
            if (!rescan && entry.Snapshot is { } scanned && DateTime.UtcNow - scanned.ScannedAt < FreshFor) return scanned;

            var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
            if (!Directory.Exists(project.RootPath)) throw new AppValidationException($"Folder project '{project.RootPath}' tidak ditemukan di mesin ini.");

            var ignored = ProjectLocator.IgnoredDirectoriesFor(project.Stacks);
            var previous = entry.Snapshot;
            var snapshot = await Task.Run(() => CodeStructureScanner.Scan(project.RootPath, ignored, previous, cancellationToken), cancellationToken);
            entry.Snapshot = snapshot;
            Evict(projectId);
            return snapshot;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    // Drops the snapshots of the projects not viewed for the longest time.
    private void Evict(Guid keep)
    {
        foreach (var stale in _projects.Where(p => p.Key != keep && p.Value.Snapshot is not null)
                     .OrderByDescending(p => p.Value.LastUsed)
                     .Skip(KeptProjects - 1)
                     .ToList())
            stale.Value.Snapshot = null;
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public volatile StructureSnapshot? Snapshot;
        public DateTime LastUsed { get; set; }

        // The background refresh in the queue (guarded by locking the entry).
        public CancellationTokenSource? Refresh { get; set; }
    }
}

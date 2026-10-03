using System.Text;
using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Skills;

// Pulls one source: download into a staging folder, find the skills (SKILL.md files, or the skill orchestrator's
// pick when there are none), swap the staging folder in and upsert the skills. Skills keep their id across re-pulls
// (keyed by folder and entry file), so the nodes they are attached to keep them.
public sealed partial class SkillImporter(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    SkillPaths paths,
    SkillFetcher fetcher,
    SkillCatalogAgent catalog,
    SkillImportQueue queue,
    SkillImportProgress progress,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<SkillImporter> logger)
{
    private const int MaxChildSources = 5;

    public async Task ImportAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await SetStatusAsync(sourceId, SkillSourceStatus.Fetching, null, cancellationToken);
        if (source is null) return;

        var staging = Path.Combine(paths.StagingRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var location = SkillSourceLocator.Locate(source.Url);
            activityLog.Info("skill", $"Menarik skill dari {source.Name}…");
            var page = await fetcher.FetchAsync(location, staging, cancellationToken);

            await SetStatusAsync(sourceId, SkillSourceStatus.Analyzing, null, cancellationToken);
            var (skills, links) = await DiscoverAsync(source, staging, page, cancellationToken);
            var children = await AddChildSourcesAsync(source, links, cancellationToken);
            if (skills.Count == 0 && children == 0)
                throw new InvalidOperationException("Tidak ada skill yang ditemukan di link ini.");

            ReplaceFolder(staging, paths.SourceDir(sourceId));
            var (added, updated, removed) = await SaveSkillsAsync(sourceId, skills, cancellationToken);

            var childNote = children > 0 ? $", {children} sumber turunan ditambahkan ke antrean" : string.Empty;
            activityLog.Success("skill", $"Skill dari {source.Name} siap: {skills.Count} skill ({added} baru, {updated} diperbarui, {removed} dihapus){childNote}.");
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
            activityLog.Error("skill", $"Gagal menarik skill dari {source.Name}: {ex.Message}");
        }
        finally
        {
            progress.Clear(sourceId);
            TryDeleteDirectory(staging);
        }
    }

    private void ReportProgress(Guid sourceId, string text)
    {
        progress.Set(sourceId, text);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = SkillSourceStatus.Analyzing.ToString(), progress = text }));
    }

    private async Task<(IReadOnlyList<DiscoveredSkill> Skills, IReadOnlyList<string> Links)> DiscoverAsync(
        SkillSource source, string staging, WebPage? page, CancellationToken cancellationToken)
    {
        if (page is not null)
        {
            ReportProgress(source.Id, "membaca halaman web…");
            var extraction = await catalog.ExtractFromWebAsync(source.Url, page, cancellationToken);
            var tags = WriteWebSkills(staging, extraction.Skills);
            var webSkills = SkillDiscovery.Scan(staging, source.Name)
                .Select(s => s with { Tags = tags.GetValueOrDefault(s.RelativeDir) })
                .ToList();
            return (webSkills, extraction.Sources);
        }

        var rootName = source.Name.Split('/')[^1];
        var found = SkillDiscovery.Scan(staging, rootName);
        if (found.Count > 0)
        {
            try
            {
                ReportProgress(source.Id, $"{found.Count} skill ditemukan, memberi tag…");
                var annotated = await catalog.AnnotateAsync(source.Name, found,
                    (done, total) =>
                    {
                        ReportProgress(source.Id, $"{found.Count} skill · batch {done}/{total}");
                        return Task.CompletedTask;
                    },
                    cancellationToken);
                return (annotated, []);
            }
            catch (Exception ex) when (ex is NineRouterException or AppValidationException)
            {
                // Tags only help matching; the skills are usable without them.
                activityLog.Warn("skill", $"Skill dari {source.Name} disimpan tanpa tag — Skill Orchestrator tidak dapat dipanggil: {ex.Message}");
                return (found, []);
            }
        }

        // No SKILL.md: the skill orchestrator tells which files are skills.
        ReportProgress(source.Id, "tidak ada SKILL.md, AI memilih file skill…");
        var picked = await catalog.IdentifyAsync(source.Name, staging, cancellationToken);
        var skills = picked
            .Select(p => SkillDiscovery.FromFile(staging, p.Path, p.Name, p.Description) is { } skill
                ? skill with { Tags = p.Tags is null ? null : SkillDiscovery.Limit(p.Tags, 500) }
                : null)
            .OfType<DiscoveredSkill>()
            .DistinctBy(s => $"{s.RelativeDir}/{s.EntryFile}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (skills, []);
    }

    // Each skill written on a web page becomes <slug>/SKILL.md; returns the tags by folder.
    private static Dictionary<string, string?> WriteWebSkills(string staging, IReadOnlyList<WebSkill> skills)
    {
        var tags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            var slug = Slug(skill.Name);
            for (var n = 2; tags.ContainsKey(slug); n++) slug = $"{Slug(skill.Name)}-{n}";
            var folder = Path.Combine(staging, slug);
            Directory.CreateDirectory(folder);

            var text = new StringBuilder();
            text.Append("---\n");
            text.Append($"name: {skill.Name.Replace('\n', ' ')}\n");
            text.Append($"description: \"{skill.Description.Replace("\"", "'").Replace('\n', ' ')}\"\n");
            text.Append("---\n\n").Append(skill.Content.Trim()).Append('\n');
            File.WriteAllText(Path.Combine(folder, SkillDiscovery.EntryFileName), text.ToString());
            tags[slug] = skill.Tags is null ? null : SkillDiscovery.Limit(skill.Tags, 500);
        }
        return tags;
    }

    // Repositories a web page points to become sources of their own (one level deep: never another web page).
    private async Task<int> AddChildSourcesAsync(SkillSource parent, IReadOnlyList<string> links, CancellationToken cancellationToken)
    {
        if (links.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var added = new List<SkillSource>();
        var accepted = 0;
        foreach (var link in links.Take(MaxChildSources))
        {
            SkillSourceLocation location;
            try
            {
                location = SkillSourceLocator.Locate(link);
            }
            catch (AppValidationException)
            {
                continue;
            }
            if (location.Kind == SkillSourceKind.Web) continue;

            accepted++;
            if (await db.SkillSources.AnyAsync(s => s.Url == location.Url, cancellationToken) || added.Any(s => s.Url == location.Url)) continue;
            var child = new SkillSource
            {
                Url = location.Url,
                Name = location.Name,
                Kind = location.Kind,
                Ref = location.Ref,
                SubPath = location.SubPath,
                ParentSourceId = parent.Id
            };
            db.SkillSources.Add(child);
            added.Add(child);
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var child in added)
        {
            queue.Enqueue(child.Id);
            eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = child.Id, status = child.Status.ToString(), action = "added" }));
        }
        return accepted;
    }

    private async Task<(int Added, int Updated, int Removed)> SaveSkillsAsync(Guid sourceId, IReadOnlyList<DiscoveredSkill> skills, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.Include(s => s.Skills).FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null) return (0, 0, 0);

        static string Key(string dir, string file) => $"{dir}|{file}".ToLowerInvariant();
        var existing = source.Skills.ToDictionary(s => Key(s.RelativeDir, s.EntryFile));
        var seen = new HashSet<string>();
        int added = 0, updated = 0;

        foreach (var found in skills)
        {
            var key = Key(found.RelativeDir, found.EntryFile);
            if (!seen.Add(key)) continue;
            var hash = SkillDiscovery.Hash(found.Body);
            // A pull without the orchestrator (model down) keeps the tags of the previous pull.
            var tags = found.Tags ?? (existing.TryGetValue(key, out var previous) ? previous.Tags : null);
            if (existing.TryGetValue(key, out var skill))
            {
                if (skill.ContentHash != hash || skill.Name != found.Name || skill.Description != found.Description || skill.Tags != tags)
                {
                    updated++;
                    skill.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                // Added explicitly: Skill gets its Guid key up front, and a keyed entity found only through a navigation
                // is taken for an existing row (UPDATE of 0 rows).
                skill = new Skill { SkillSourceId = sourceId, RelativeDir = found.RelativeDir, EntryFile = found.EntryFile };
                db.Skills.Add(skill);
                added++;
            }
            skill.Name = found.Name;
            skill.Description = found.Description;
            skill.Tags = tags;
            skill.Content = found.Body;
            skill.ContentHash = hash;
        }

        var removed = source.Skills.Where(s => existing.ContainsValue(s) && !seen.Contains(Key(s.RelativeDir, s.EntryFile))).ToList();
        foreach (var skill in removed)
        {
            // Cascades to NodeSkills: the skill disappears from every node it was attached to.
            source.Skills.Remove(skill);
            db.Skills.Remove(skill);
        }
        if (removed.Count > 0)
            activityLog.Warn("skill", $"{removed.Count} skill tidak ada lagi di {source.Name} dan dihapus dari gudang: {string.Join(", ", removed.Select(s => s.Name).Take(10))}.");

        source.Status = SkillSourceStatus.Ready;
        source.Error = null;
        source.LastPulledAt = DateTime.UtcNow;
        source.SkillCount = seen.Count;
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = source.Status.ToString() }));
        if (removed.Count > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { action = "skills" }));
        return (added, updated, removed.Count);
    }

    private async Task<SkillSource?> SetStatusAsync(Guid sourceId, SkillSourceStatus status, string? error, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null) return null;

        source.Status = status;
        source.Error = error is null ? null : SkillDiscovery.Limit(error, 2000);
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = status.ToString(), error }));
        return source;
    }

    // The staging folder lives next to the store (same volume), so the swap is two renames.
    private static void ReplaceFolder(string staging, string target)
    {
        Directory.CreateDirectory(staging);
        var old = target + ".old";
        TryDeleteDirectory(old);
        if (Directory.Exists(target)) Directory.Move(target, old);
        Directory.Move(staging, target);
        TryDeleteDirectory(old);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            SkillPaths.DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open by an editor or a virus scanner; the next pull cleans it up.
        }
    }

    public static string Slug(string name)
    {
        var slug = NonSlug().Replace(name.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "skill" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

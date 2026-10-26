using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Skills;

public sealed partial class SkillImporter
{
    private async Task<(IReadOnlyList<DiscoveredSkill> Skills, IReadOnlyList<string> Links)> DiscoverAsync(
        SkillSource source, string staging, WebPage? page, CancellationToken cancellationToken)
    {
        if (page is not null)
        {
            ReportProgress(source.Id, Lang.T("membaca halaman web…", "reading the web page…"));
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
                ReportProgress(source.Id, Lang.T($"{found.Count} skill ditemukan, memberi tag…", $"{found.Count} skills found, tagging…"));
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
                activityLog.Warn("skill", Lang.T($"Skill dari {source.Name} disimpan tanpa tag — Skill Orchestrator tidak dapat dipanggil: {ex.Message}", $"Skills from {source.Name} saved without tags — the Skill Orchestrator cannot be called: {ex.Message}"));
                return (found, []);
            }
        }

        // No SKILL.md: the skill orchestrator tells which files are skills.
        ReportProgress(source.Id, Lang.T("tidak ada SKILL.md, AI memilih file skill…", "no SKILL.md, the AI picks the skill files…"));
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
}

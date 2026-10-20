using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Skills;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Sources are only added, never removed; each is pulled in the background like one added from the Setting menu.
    private async Task PlanSkillsAsync(SetupProfileSkills s, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await skillService.GetSettingsAsync(cancellationToken);
        var model = Clean(s.Model);
        var diffs = new List<string>();
        Diff(diffs, "Model Skill Orchestrator", current.Model ?? "otomatis", model ?? "otomatis");
        Diff(diffs, "Maks karakter skill", current.MaxPromptChars, s.MaxPromptChars);
        if (diffs.Count > 0)
        {
            plan.Change(SetupProfileSection.Skills, string.Join(" · ", diffs));
            plan.Actions.Add((SetupProfileSection.Skills, () =>
                skillService.UpdateSettingsAsync(new UpdateSkillSettingsRequest(model, s.MaxPromptChars), cancellationToken)));
        }

        var known = (await skillService.GetSourcesAsync(cancellationToken)).Select(x => x.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var url in s.Sources ?? [])
        {
            string normalized;
            try
            {
                normalized = SkillSourceLocator.Locate(url).Url;
            }
            catch (AppValidationException ex)
            {
                plan.Warn($"Sumber skill {url} dilewati: {ex.Message}");
                continue;
            }
            if (!known.Add(normalized)) continue;

            plan.Change(SetupProfileSection.Skills, $"Sumber skill ditambah: {normalized} (ditarik di latar belakang).");
            plan.Actions.Add((SetupProfileSection.Skills, () => skillService.AddSourceAsync(new AddSkillSourceRequest(normalized), cancellationToken)));
        }
    }
}

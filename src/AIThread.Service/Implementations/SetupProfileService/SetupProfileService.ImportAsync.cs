using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    public async Task<SetupProfileImportResultDto> ImportAsync(SetupProfileImportRequest request, CancellationToken cancellationToken = default)
    {
        var profile = request.Profile;
        if (profile is null || profile.Format != SetupProfileFile.FormatName)
            throw new AppValidationException(Lang.T($"Bukan file profil setup AIT (format harus \"{SetupProfileFile.FormatName}\").", $"Not an AIT setup profile file (the format must be \"{SetupProfileFile.FormatName}\")."));
        if (profile.Version > SetupProfileFile.CurrentVersion)
            throw new AppValidationException(Lang.T($"Profil dibuat oleh AIT yang lebih baru (format v{profile.Version}). Perbarui AIT dulu.", $"The profile was made by a newer AIT (format v{profile.Version}). Update AIT first."));
        if (!Enum.IsDefined(request.NodeMode)) throw new AppValidationException(Lang.T("Mode node tidak valid.", "Invalid node mode."));

        var available = SectionsOf(profile);
        var selected = (request.Sections ?? available).Where(available.Contains).ToHashSet();
        var secrets = request.Secrets ?? new SetupProfileSecrets(null, null, null, null);
        var plan = new ImportPlan();

        if (selected.Contains(SetupProfileSection.Router)) await PlanRouterAsync(profile.Router!, secrets, plan, cancellationToken);
        if (selected.Contains(SetupProfileSection.Orchestration)) await PlanOrchestrationAsync(profile.Orchestration!, plan, cancellationToken);
        if (selected.Contains(SetupProfileSection.Memory)) await PlanMemoryAsync(profile.Memory!, secrets, request.CopyExistingMemory, plan, cancellationToken);
        if (selected.Contains(SetupProfileSection.Skills)) await PlanSkillsAsync(profile.Skills!, plan, cancellationToken);
        if (selected.Contains(SetupProfileSection.Nodes)) await PlanNodesAsync(profile.Nodes!, request.NodeMode, plan, cancellationToken);
        if (selected.Contains(SetupProfileSection.Business)) await PlanBusinessAsync(profile.Business!, plan, cancellationToken);

        var errors = new List<string>();
        if (!request.DryRun)
        {
            foreach (var (section, apply) in plan.Actions)
            {
                try
                {
                    await apply();
                }
                catch (AppValidationException ex)
                {
                    errors.Add($"{SectionLabels[section]}: {ex.Message}");
                }
            }

            var failed = errors.Count > 0 ? Lang.T($", {errors.Count} bagian gagal", $", {errors.Count} sections failed") : string.Empty;
            activityLog.Info("setting", Lang.T($"Profil setup diimpor: {plan.Changes.Count} perubahan{failed}.", $"Setup profile imported: {plan.Changes.Count} changes{failed}."));
            foreach (var error in errors) activityLog.Warn("setting", Lang.T($"Import profil setup — {error}", $"Setup profile import — {error}"));
        }

        return new SetupProfileImportResultDto(request.DryRun, available, plan.Changes, plan.Secrets, plan.Warnings, errors);
    }
}

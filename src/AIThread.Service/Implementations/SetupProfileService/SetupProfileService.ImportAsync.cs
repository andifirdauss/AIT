using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    public async Task<SetupProfileImportResultDto> ImportAsync(SetupProfileImportRequest request, CancellationToken cancellationToken = default)
    {
        var profile = request.Profile;
        if (profile is null || profile.Format != SetupProfileFile.FormatName)
            throw new AppValidationException($"Bukan file profil setup AIT (format harus \"{SetupProfileFile.FormatName}\").");
        if (profile.Version > SetupProfileFile.CurrentVersion)
            throw new AppValidationException($"Profil dibuat oleh AIT yang lebih baru (format v{profile.Version}). Perbarui AIT dulu.");
        if (!Enum.IsDefined(request.NodeMode)) throw new AppValidationException("Mode node tidak valid.");

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

            var failed = errors.Count > 0 ? $", {errors.Count} bagian gagal" : string.Empty;
            activityLog.Info("setting", $"Profil setup diimpor: {plan.Changes.Count} perubahan{failed}.");
            foreach (var error in errors) activityLog.Warn("setting", $"Import profil setup — {error}");
        }

        return new SetupProfileImportResultDto(request.DryRun, available, plan.Changes, plan.Secrets, plan.Warnings, errors);
    }
}

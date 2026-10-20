using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The developer name stays this machine's.
    private async Task PlanBusinessAsync(SetupProfileBusiness b, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await businessMemory.GetSettingsAsync(cancellationToken);
        var model = Clean(b.Model);
        var diffs = new List<string>();
        Diff(diffs, "Wiki bisnis", OnOff(current.Enabled), OnOff(b.Enabled));
        Diff(diffs, "Interval (jam)", current.IntervalHours, b.IntervalHours);
        Diff(diffs, "Model", current.Model ?? "otomatis", model ?? "otomatis");
        if (diffs.Count == 0) return;

        plan.Change(SetupProfileSection.Business, string.Join(" · ", diffs));
        plan.Actions.Add((SetupProfileSection.Business, () =>
            businessMemory.UpdateSettingsAsync(new UpdateBusinessSettingsRequest(b.Enabled, b.IntervalHours, model, current.DeveloperName), cancellationToken)));
    }
}

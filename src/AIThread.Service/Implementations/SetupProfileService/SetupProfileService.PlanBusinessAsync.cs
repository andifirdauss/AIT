using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The developer name stays this machine's.
    private async Task PlanBusinessAsync(SetupProfileBusiness b, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await businessMemory.GetSettingsAsync(cancellationToken);
        var model = Clean(b.Model);
        var diffs = new List<string>();
        Diff(diffs, Lang.T("Wiki bisnis", "Business wiki"), OnOff(current.Enabled), OnOff(b.Enabled));
        Diff(diffs, Lang.T("Interval (jam)", "Interval (hours)"), current.IntervalHours, b.IntervalHours);
        Diff(diffs, "Model", current.Model ?? Lang.T("otomatis", "automatic"), model ?? Lang.T("otomatis", "automatic"));
        if (diffs.Count == 0) return;

        plan.Change(SetupProfileSection.Business, string.Join(" · ", diffs));
        plan.Actions.Add((SetupProfileSection.Business, () =>
            businessMemory.UpdateSettingsAsync(new UpdateBusinessSettingsRequest(b.Enabled, b.IntervalHours, model, current.DeveloperName), cancellationToken)));
    }
}

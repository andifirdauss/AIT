using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto CliStep(IReadOnlyList<string> enabledClis) =>
        new("cli", Lang.T("Arahkan CLI coding ke AIT", "Point a coding CLI to AIT"), true, enabledClis.Count > 0, enabledClis.Count > 0
            ? Lang.T($"Aktif: {string.Join(", ", enabledClis)}.", $"On: {string.Join(", ", enabledClis)}.")
            : Lang.T("Belum ada CLI yang diarahkan ke AIT. Nyalakan minimal satu, global atau per project.", "No CLI points to AIT yet. Turn on at least one, globally or per project."));
}

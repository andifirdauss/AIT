using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto CliStep(IReadOnlyList<string> enabledClis) =>
        new("cli", "Arahkan CLI coding ke AIT", true, enabledClis.Count > 0, enabledClis.Count > 0
            ? $"Aktif: {string.Join(", ", enabledClis)}."
            : "Belum ada CLI yang diarahkan ke AIT. Nyalakan minimal satu, global atau per project.");
}

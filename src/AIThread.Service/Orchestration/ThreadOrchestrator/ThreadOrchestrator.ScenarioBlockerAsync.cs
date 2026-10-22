using AIThread.Common.Entities;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Why a scenario cannot run yet (Indonesian, for the developer), or null. agreed: the developer accepts that the run
    // creates real data and may send notifications; asked once per project, then remembered with their name.
    private async Task<string?> ScenarioBlockerAsync(TestingScenario scenario, bool agreed, string by, CancellationToken cancellationToken)
    {
        if (ScenarioText.Missing(scenario) is { Count: > 0 } missing) return "Skenario belum lengkap: " + string.Join(" ", missing);
        if (scenario.SourceThreadId is null) return "Skenario ini tidak terhubung ke thread mana pun, jadi tidak ada aplikasi dan rencana testing untuk dijalankan.";

        var settings = (await testAccounts.GetAsync(scenario.ProjectKey, cancellationToken)).Settings;
        if (settings.SideEffectsAcknowledgedBy is not null) return null;
        if (!agreed) return SideEffectsWarning;
        await testAccounts.AcknowledgeSideEffectsAsync(scenario.ProjectKey, by, cancellationToken);
        return null;
    }

    public const string SideEffectsWarning =
        "Skenario dijalankan di aplikasi yang sedang berjalan: submit dan approve sungguhan membuat data dan bisa mengirim email atau notifikasi. " +
        "Pastikan database dan email di lingkungan dev tidak mengarah ke orang asli. Konfirmasi ini diminta sekali per project.";
}

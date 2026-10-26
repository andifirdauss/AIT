using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Why a scenario cannot run yet (in the UI language, for the developer), or null. agreed: the developer accepts that the run
    // creates real data and may send notifications; asked once per project, then remembered with their name.
    private async Task<string?> ScenarioBlockerAsync(TestingScenario scenario, bool agreed, string by, CancellationToken cancellationToken)
    {
        if (ScenarioText.Missing(scenario) is { Count: > 0 } missing) return Lang.T("Skenario belum lengkap: ", "The scenario is not complete yet: ") + string.Join(" ", missing);
        if (scenario.SourceThreadId is null) return Lang.T("Skenario ini tidak terhubung ke thread mana pun, jadi tidak ada aplikasi dan rencana testing untuk dijalankan.", "This scenario is not linked to any thread, so there is no application and testing plan to run.");

        var settings = (await testAccounts.GetAsync(scenario.ProjectKey, cancellationToken)).Settings;
        if (settings.SideEffectsAcknowledgedBy is not null) return null;
        if (!agreed) return SideEffectsWarning;
        await testAccounts.AcknowledgeSideEffectsAsync(scenario.ProjectKey, by, cancellationToken);
        return null;
    }

    public static string SideEffectsWarning =>
        Lang.T("Skenario dijalankan di aplikasi yang sedang berjalan: submit dan approve sungguhan membuat data dan bisa mengirim email atau notifikasi. ", "Scenarios run in the running application: real submits and approvals create data and can send emails or notifications. ") +
        Lang.T("Pastikan database dan email di lingkungan dev tidak mengarah ke orang asli. Konfirmasi ini diminta sekali per project.", "Make sure the database and email of the dev environment do not reach real people. This confirmation is asked once per project.");
}

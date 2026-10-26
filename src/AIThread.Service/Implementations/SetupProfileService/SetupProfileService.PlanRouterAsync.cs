using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    private async Task PlanRouterAsync(SetupProfileRouter profileRouter, SetupProfileSecrets secrets, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await settingService.GetAsync(string.Empty, cancellationToken);
        var target = SettingService.NormalizeRouterUrl(profileRouter.BaseUrl);
        var key = Clean(secrets.NineRouterApiKey);
        plan.Secrets.Add(new SetupProfileSecretDto("nineRouterApiKey", "API key 9router", current.HasNineRouterApiKey));

        var urlChanged = !string.Equals(current.NineRouterBaseUrl, target, StringComparison.OrdinalIgnoreCase);
        if (urlChanged) plan.Change(SetupProfileSection.Router, $"Base URL 9router: {current.NineRouterBaseUrl} → {target}");
        if (key is not null) plan.Change(SetupProfileSection.Router, Lang.T("API key 9router diganti dengan yang diisi.", "The 9router API key is replaced with the one entered."));
        if (!urlChanged && key is null) return;

        plan.Actions.Add((SetupProfileSection.Router, async () =>
        {
            await settingService.UpdateRouterAsync(target, key, cancellationToken);
            var status = await router.CheckStatusAsync(cancellationToken);
            if (!status.IsOnline)
                plan.Warn(Lang.T($"9router belum bisa dihubungi di {status.BaseUrl}. Jalankan 9router, lalu cek langkah 9router di Penyiapan.", $"9router cannot be reached yet at {status.BaseUrl}. Start 9router, then check the 9router step in Setup."));
            else if (status.ApiKeyValid == false)
                plan.Warn(status.HasApiKey ? Lang.T("9router menolak API key yang tersimpan.", "9router rejects the stored API key.") : Lang.T("9router mewajibkan API key. Isi di langkah 9router.", "9router requires an API key. Fill it in at the 9router step."));
        }));
    }
}

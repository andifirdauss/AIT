using AIThread.Common.DTOs;

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
        if (key is not null) plan.Change(SetupProfileSection.Router, "API key 9router diganti dengan yang diisi.");
        if (!urlChanged && key is null) return;

        plan.Actions.Add((SetupProfileSection.Router, async () =>
        {
            await settingService.UpdateRouterAsync(target, key, cancellationToken);
            var status = await router.CheckStatusAsync(cancellationToken);
            if (!status.IsOnline)
                plan.Warn($"9router belum bisa dihubungi di {status.BaseUrl}. Jalankan 9router, lalu cek langkah 9router di Setup.");
            else if (status.ApiKeyValid == false)
                plan.Warn(status.HasApiKey ? "9router menolak API key yang tersimpan." : "9router mewajibkan API key. Isi di langkah 9router.");
        }));
    }
}

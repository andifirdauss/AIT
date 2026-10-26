using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Test and save in one click: values that do not work are never saved, so the step cannot end up half done.
    public async Task<SetupRouterResultDto> SaveRouterAsync(SetupRouterRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = SettingService.NormalizeRouterUrl(request.BaseUrl);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppValidationException(Lang.T("URL 9router tidak valid. Contoh: http://localhost:20128", "Invalid 9router URL. Example: http://localhost:20128"));

        var typedKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var tested = await router.TestConnectionAsync(baseUrl, typedKey, cancellationToken);
        if (!tested.IsOnline)
            return new SetupRouterResultDto(tested, false, Lang.T($"9router tidak dapat dihubungi di {tested.BaseUrl}: {tested.Error}. Belum disimpan.", $"9router cannot be reached at {tested.BaseUrl}: {tested.Error}. Not saved yet."));
        if (tested.ApiKeyValid == false)
        {
            return new SetupRouterResultDto(tested, false, tested.HasApiKey
                ? Lang.T("9router menolak API key ini. Belum disimpan.", "9router rejects this API key. Not saved yet.")
                : Lang.T("9router mewajibkan API key. Salin dari dashboard 9router (menu API Keys).", "9router requires an API key. Copy it from the 9router dashboard (API Keys menu)."));
        }

        await settingService.UpdateRouterAsync(baseUrl, typedKey, cancellationToken);
        var saved = await router.CheckStatusAsync(cancellationToken);
        activityLog.Info("setting", Lang.T($"Koneksi 9router disimpan dari menu Penyiapan ({saved.BaseUrl}).", $"9router connection saved from the Setup menu ({saved.BaseUrl})."));
        return new SetupRouterResultDto(saved, true, saved.ModelCount == 0
            ? Lang.T("Tersimpan. 9router belum punya model: hubungkan provider di dashboard 9router (menu Providers).", "Saved. 9router has no model yet: connect a provider in the 9router dashboard (Providers menu).")
            : Lang.T("Koneksi 9router tersimpan.", "9router connection saved."));
    }
}

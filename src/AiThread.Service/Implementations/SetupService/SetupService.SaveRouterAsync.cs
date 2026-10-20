using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    // Test and save in one click: values that do not work are never saved, so the step cannot end up half done.
    public async Task<SetupRouterResultDto> SaveRouterAsync(SetupRouterRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = SettingService.NormalizeRouterUrl(request.BaseUrl);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppValidationException("URL 9router tidak valid. Contoh: http://localhost:20128");

        var typedKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var tested = await router.TestConnectionAsync(baseUrl, typedKey, cancellationToken);
        if (!tested.IsOnline)
            return new SetupRouterResultDto(tested, false, $"9router tidak dapat dihubungi di {tested.BaseUrl}: {tested.Error}. Belum disimpan.");
        if (tested.ApiKeyValid == false)
        {
            return new SetupRouterResultDto(tested, false, tested.HasApiKey
                ? "9router menolak API key ini. Belum disimpan."
                : "9router mewajibkan API key. Salin dari dashboard 9router (menu API Keys).");
        }

        await settingService.UpdateRouterAsync(baseUrl, typedKey, cancellationToken);
        var saved = await router.CheckStatusAsync(cancellationToken);
        activityLog.Info("setting", $"Koneksi 9router disimpan dari menu Setup ({saved.BaseUrl}).");
        return new SetupRouterResultDto(saved, true, saved.ModelCount == 0
            ? "Tersimpan. 9router belum punya model: hubungkan provider di dashboard 9router (menu Providers)."
            : "Koneksi 9router tersimpan.");
    }
}

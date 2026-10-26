using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Online is not enough: chat calls also need an accepted key and at least one provider (a model) behind 9router.
    private SetupStepDto RouterStep(RouterStatusDto? status)
    {
        const string id = "router";
        var title = Lang.T("Hubungkan 9router", "Connect 9router");
        if (status is null || !status.IsOnline)
        {
            var where = status is null ? string.Empty : Lang.T($" di {status.BaseUrl}", $" at {status.BaseUrl}");
            var hint = FindNodeJs() is null
                ? Lang.T(" Node.js belum ditemukan: pasang Node.js 18+ agar AIT bisa memasang dan menjalankan 9router.", " Node.js was not found yet: install Node.js 18+ so AIT can install and run 9router.")
                : Lang.T(" Klik Jalankan 9router.", " Click Start 9router.");
            return new SetupStepDto(id, title, true, false, $"9router offline{where}.{hint}");
        }

        if (status.ApiKeyValid == false)
        {
            return new SetupStepDto(id, title, true, false, status.HasApiKey
                ? Lang.T("9router menolak API key yang tersimpan. Salin ulang dari dashboard 9router (menu API Keys).", "9router rejects the stored API key. Copy it again from the 9router dashboard (API Keys menu).")
                : Lang.T("9router mewajibkan API key. Salin dari dashboard 9router (menu API Keys) lalu simpan di sini.", "9router requires an API key. Copy it from the 9router dashboard (API Keys menu), then save it here."));
        }

        if (status.ModelCount == 0)
            return new SetupStepDto(id, title, true, false, Lang.T("9router online, tetapi belum ada model. Hubungkan minimal satu provider di dashboard 9router (menu Providers).", "9router is online, but has no model yet. Connect at least one provider in the 9router dashboard (Providers menu)."));

        var key = status.HasApiKey ? "API key OK" : Lang.T("tanpa API key", "no API key");
        return new SetupStepDto(id, title, true, true, $"Online · {status.ModelCount} model · {status.ComboCount} combo · {key}.");
    }
}

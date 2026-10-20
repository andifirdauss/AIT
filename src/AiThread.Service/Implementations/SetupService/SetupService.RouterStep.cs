using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    // Online is not enough: chat calls also need an accepted key and at least one provider (a model) behind 9router.
    private SetupStepDto RouterStep(RouterStatusDto? status)
    {
        const string id = "router", title = "Hubungkan 9router";
        if (status is null || !status.IsOnline)
        {
            var where = status is null ? string.Empty : $" di {status.BaseUrl}";
            var hint = FindNodeJs() is null
                ? " Node.js belum ditemukan: pasang Node.js 18+ agar AIT bisa memasang dan menjalankan 9router."
                : " Klik Jalankan 9router.";
            return new SetupStepDto(id, title, true, false, $"9router offline{where}.{hint}");
        }

        if (status.ApiKeyValid == false)
        {
            return new SetupStepDto(id, title, true, false, status.HasApiKey
                ? "9router menolak API key yang tersimpan. Salin ulang dari dashboard 9router (menu API Keys)."
                : "9router mewajibkan API key. Salin dari dashboard 9router (menu API Keys) lalu simpan di sini.");
        }

        if (status.ModelCount == 0)
            return new SetupStepDto(id, title, true, false, "9router online, tetapi belum ada model. Hubungkan minimal satu provider di dashboard 9router (menu Providers).");

        var key = status.HasApiKey ? "API key OK" : "tanpa API key";
        return new SetupStepDto(id, title, true, true, $"Online · {status.ModelCount} model · {status.ComboCount} combo · {key}.");
    }
}

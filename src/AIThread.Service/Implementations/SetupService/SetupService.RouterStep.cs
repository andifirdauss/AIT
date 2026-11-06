using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Online is not enough: chat calls also need an accepted key and at least one provider (a model) behind 9router.
    // When every active node runs on an official CLI (cliOnly), 9router is not needed; when it is offline and a CLI is
    // installed, the step says that the CLI is the other way.
    private SetupStepDto RouterStep(RouterStatusDto? status, bool cliOnly, CliBackendStatusDto? cliEngine)
    {
        const string id = "router";
        var title = Lang.T("Hubungkan 9router", "Connect 9router");
        if (cliOnly && status is not { IsOnline: true })
        {
            var engine = cliEngine is null ? "CLI" : $"{cliEngine.Name} {cliEngine.Version}".Trim();
            return new SetupStepDto(id, title, true, true,
                Lang.T($"Tidak diperlukan: semua node aktif memakai CLI resmi ({engine}) dengan login yang sudah ada di komputer ini.",
                    $"Not needed: every active node uses an official CLI ({engine}) with the login already on this computer."));
        }

        if (status is null || !status.IsOnline)
        {
            var where = status is null ? string.Empty : Lang.T($" di {status.BaseUrl}", $" at {status.BaseUrl}");
            var hint = FindNodeJs() is null
                ? Lang.T(" Node.js belum ditemukan: pasang Node.js 18+ agar AIT bisa memasang dan menjalankan 9router.", " Node.js was not found yet: install Node.js 18+ so AIT can install and run 9router.")
                : Lang.T(" Klik Jalankan 9router.", " Click Start 9router.");
            var cliHint = cliEngine is null
                ? string.Empty
                : Lang.T($" Tanpa akses 9router? {cliEngine.Name} terpasang di sini: pilih model \"{cliEngine.Name} (CLI)\" (mis. cli:claude:opus) di preset node, maka langkah ini tidak diperlukan.",
                    $" No 9router access? {cliEngine.Name} is installed here: choose a \"{cliEngine.Name} (CLI)\" model (e.g. cli:claude:opus) in the node preset, and this step is no longer needed.");
            return new SetupStepDto(id, title, true, false, $"9router offline{where}.{hint}{cliHint}");
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

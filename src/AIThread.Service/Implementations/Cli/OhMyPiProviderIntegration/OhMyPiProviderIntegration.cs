using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// AIThread as a provider of its own in oh-my-pi: a "providers.AIT" entry in ~/.omp/agent/models.yml, so omp lists
// "AIT/AIThread" next to its built-in providers and shows the model name "AIThread" in its status line. Checked against
// omp 18.11.3 (ids keep their case): api anthropic-messages posts to <baseUrl>/v1/messages, apiKey is
// sent as Authorization: Bearer (an environment variable of that name wins over the literal). models.yml exists only
// per user (or per --profile), so there is no project scope; the project comes from the cwd omp sends. The proxy
// ignores the model id, so renaming it only changes what omp shows.
public sealed partial class OhMyPiProviderIntegration(ISettingService settingService, string? modelsPath = null) : ICliIntegration
{
    public const string Id = "oh-my-pi-provider";
    public const string ProviderId = "AIT";
    public const string ModelId = "AIThread";
    private const string BackupSuffix = ".aithread-backup";

    private readonly string _modelsPath = modelsPath ?? Path.Combine(OmpAgentDirectory, "models.yml");

    private static string OmpAgentDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".omp", "agent");

    public CliInfoDto Info => new(
        Id,
        "oh-my-pi (omp) — provider AIT",
        Lang.T($"Menambah provider \"{ProviderId}\" di models.yml omp. Pilih model {ProviderId}/{ModelId} lewat /model (atau --model {ProviderId}/{ModelId}); provider Anthropic asli tetap bisa dipakai.", $"Adds the provider \"{ProviderId}\" to the omp models.yml. Choose the model {ProviderId}/{ModelId} with /model (or --model {ProviderId}/{ModelId}); the original Anthropic provider can still be used."),
        _modelsPath,
        null,
        Lang.T("Berlaku untuk sesi omp berikutnya. Project tetap terdeteksi dari folder tempat omp dijalankan.", "Takes effect for the next omp session. The project is still detected from the folder omp runs in."));
}

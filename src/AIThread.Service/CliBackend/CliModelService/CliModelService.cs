using AIThread.Service.Interfaces;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService(IEnumerable<ICliEngine> engines) : ICliModelService
{
    private readonly IReadOnlyList<ICliEngine> _engines = engines.ToList();

    // The owner label of an engine's models in the pickers: "Claude Code (CLI)", "Codex (CLI)", "Gemini (CLI)".
    public static string Owner(ICliEngine engine) => $"{engine.Name} (CLI)";
}

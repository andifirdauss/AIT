using AIThread.Common.DTOs;
using AIThread.Service.CliBackend;

namespace AIThread.Service.Interfaces;

// An official coding CLI run headless as a model ("cli:<Tool>:<model>"): one stateless call per request, built-in
// tools off, with the login already on this machine. AIT never reads or forwards that login. CliModelService picks the
// engine by Tool.
public interface ICliEngine
{
    // The tool part of the model id: "claude", "codex", "gemini".
    string Tool { get; }

    // Shown to the user: "Claude Code", "Codex", "Gemini" (messages add "CLI").
    string Name { get; }

    // The models offered in the Node and Setup pickers ("" = the CLI's own default model): a fixed list of aliases, or
    // what the CLI itself lists. Any other name the CLI knows can still be typed.
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    // model: an alias or a full model name; empty = the CLI's default model. Failures are NineRouterException, so
    // retries and fallback nodes work as for 9router.
    Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default);

    // Whether the CLI is installed and its version (cached for a few minutes unless refresh).
    Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default);
}

using AIThread.Common.DTOs;
using AIThread.Service.CliBackend;

namespace AIThread.Service.Interfaces;

// Runs the official Claude Code CLI headless (`claude -p`) as a model: one stateless call per request, built-in tools
// off, with the login already on this machine. AIT never reads or forwards that login.
public interface IClaudeCliAdapter
{
    // model: an alias (opus, sonnet, haiku, fable) or a full model name; empty = the CLI's default model.
    // Failures are NineRouterException, so retries and fallback nodes work as for 9router.
    Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default);

    // Whether claude is installed and its version (cached for a few minutes unless refresh).
    Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default);
}

using AIThread.Common.DTOs;
using AIThread.Service.CliBackend;
using AIThread.Service.Models;

namespace AIThread.Service.Interfaces;

// Node models served by an official coding CLI on this machine ("cli:<tool>:<model>") instead of 9router. Called by
// NineRouterService.ChatAsync after the messages are masked, so every agent can run on it unchanged; tools are
// emulated in text (CliTranscript / CliToolCallParser).
public interface ICliModelService
{
    Task<ChatCompletionResult> ChatAsync(CliModelId model, ChatCompletionRequest request, CliCallLimits limits,
        Func<string, Task>? onDelta = null, CancellationToken cancellationToken = default);

    // The models of the CLIs installed here, for the Node and Setup pickers (not for the proxy's /v1/models).
    Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CliBackendStatusDto>> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default);
}

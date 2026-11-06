using AIThread.Common.Localization;
using AIThread.Service.CliBackend;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<ChatCompletionResult> ChatAsync(
        ChatCompletionRequest request,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        // The last line of defence: every model call of every agent goes through here, so nothing reaches a provider
        // with a secret in it (in place, so the agent's own message list keeps the placeholders too).
        secrets.RedactMessages(request.Messages);
        var emitted = false;
        Func<string, Task>? trackedDelta = onDelta is null
            ? null
            : async delta =>
            {
                emitted = true;
                await onDelta(delta);
            };

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A "cli:<tool>:<model>" node runs on an official CLI of this machine instead (CliBackend), after the
                // masking above, with the same retries and fallback.
                if (CliModelId.TryParse(request.Model, out var cliModel))
                {
                    if (cliModels is null) throw new NineRouterException(Lang.T($"Model CLI '{request.Model}' tidak tersedia di proses ini.", $"CLI model '{request.Model}' is not available in this process."), 400);
                    return await cliModels.ChatAsync(cliModel, request,
                        new CliCallLimits(TimeSpan.FromSeconds(Math.Max(30, settings.RequestTimeoutSeconds)), _stallTimeout), trackedDelta, cancellationToken);
                }
                return await SendChatAsync(settings, request, trackedDelta, cancellationToken);
            }
            catch (NineRouterException ex) when (ex.IsTransient && attempt < maxAttempts && !emitted && !cancellationToken.IsCancellationRequested
                                                 && RetryDelay(ex, attempt) is { } delay)
            {
                logger.LogWarning("9router call to {Model} failed (attempt {Attempt}), retrying in {Delay}s: {Message}",
                    request.Model, attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}

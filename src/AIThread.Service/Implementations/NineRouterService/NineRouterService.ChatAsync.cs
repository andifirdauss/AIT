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

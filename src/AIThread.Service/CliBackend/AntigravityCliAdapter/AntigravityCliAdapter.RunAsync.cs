using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // A run that agy ended because the model emitted a native function call is tried again right away, up to
    // MalformedCallAttempts in all (nothing was streamed yet, so a retry cannot repeat text). Other failures go to
    // NodeCaller's retry and fallback as they are.
    private const int MalformedCallAttempts = 3;

    public async Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var streamed = false;
        Func<string, Task>? forward = onDelta is null
            ? null
            : delta =>
            {
                streamed = true;
                return onDelta(delta);
            };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RunOnceAsync(model, prompt, limits, forward, cancellationToken);
            }
            catch (NineRouterException ex) when (attempt < MalformedCallAttempts && !streamed && IsMalformedFunctionCall(ex.Message))
            {
                logger.LogInformation("Antigravity CLI call to {Model} ended on a malformed native function call (attempt {Attempt}); trying again.",
                    CliFailure.Shown(model), attempt);
            }
        }
    }
}

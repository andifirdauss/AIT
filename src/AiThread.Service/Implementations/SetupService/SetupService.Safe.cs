using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    private async Task<T> Safe<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Setup status component failed.");
            return fallback;
        }
    }
}

using AiThread.Data.Memory;

namespace AiThread.Service.Implementations;

public sealed partial class DashboardService
{
    private async Task<T> Safe<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.LogStoreWarning(ex, "Dashboard component failed");
            return fallback;
        }
    }
}

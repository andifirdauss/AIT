using AiThread.Common.Entities;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    public async Task ApplySavedAsync(CancellationToken cancellationToken = default)
    {
        SystemSetting entity;
        try
        {
            entity = await LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory storage settings could not be read; using the built-in stores.");
            return;
        }

        Apply("short", () => shortStore.Configure(ToConnection(entity.ShortMemoryStore)));
        Apply("long", () => longStore.Configure(ToConnection(entity.LongMemoryStore)));
        Apply("business", () => businessStore.Configure(ToConnection(entity.BusinessMemoryStore)));
        logger.LogInformation("Memory storage: short = {Short}, long = {Long}, business = {Business}.",
            shortStore.Description, longStore.Description, businessStore.Description);

        void Apply(string level, Action configure)
        {
            try
            {
                configure();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The saved {Level} memory store is invalid; using the built-in store.", level);
            }
        }
    }
}

namespace AiThread.Service.Memory;

public sealed partial class MemoryAi
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => await PickNodeAsync(cancellationToken) is not null;
}

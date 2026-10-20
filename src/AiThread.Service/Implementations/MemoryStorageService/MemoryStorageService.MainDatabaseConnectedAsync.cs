namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private async Task<bool> MainDatabaseConnectedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }
}

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    private Task<bool> DatabaseConnectedAsync(CancellationToken cancellationToken) =>
        Safe(async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken);
        }, false);
}

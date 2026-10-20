using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<(string MediaType, byte[] Data)?> GetAssetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var asset = await db.BusinessAssets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.MediaType, a.Data })
            .FirstOrDefaultAsync(cancellationToken);
        return asset is null ? null : (asset.MediaType, asset.Data);
    }
}

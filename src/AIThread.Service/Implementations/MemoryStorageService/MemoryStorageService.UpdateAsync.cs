using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    public async Task<MemoryStorageUpdateResultDto> UpdateAsync(UpdateMemoryStorageRequest request, CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
            if (entity is null)
            {
                entity = new SystemSetting { Id = SystemSetting.SingletonId };
                db.SystemSettings.Add(entity);
            }

            var oldShort = ToConnection(entity.ShortMemoryStore);
            var oldLong = ToConnection(entity.LongMemoryStore);
            var oldBusiness = ToConnection(entity.BusinessMemoryStore);
            var (shortSetting, newShort) = Normalize(request.ShortMemory, MemoryLevel.Short, entity.ShortMemoryStore);
            var (longSetting, newLong) = Normalize(request.LongMemory, MemoryLevel.Long, entity.LongMemoryStore);
            var (businessSetting, newBusiness) = request.BusinessMemory is { } business
                ? Normalize(business, MemoryLevel.Business, entity.BusinessMemoryStore)
                : (Copy(entity.BusinessMemoryStore), oldBusiness);

            // Open (and create the table in) every new store before anything is saved, so a typo never leaves
            // AIThread pointing at a store it cannot use.
            IShortMemoryStore? targetShort = null;
            try
            {
                if (newShort != oldShort) targetShort = await OpenShortAsync(newShort);
                if (newLong != oldLong) await EnsureLongAsync(newLong, cancellationToken);
                if (newBusiness != oldBusiness) await EnsureBusinessAsync(newBusiness, cancellationToken);

                var warnings = new List<string>();
                int copiedShort = 0, copiedLong = 0, copiedBusiness = 0;
                if (request.CopyExistingData)
                {
                    if (targetShort is not null) copiedShort = await CopyShortAsync(oldShort, targetShort, warnings);
                    if (newLong != oldLong)
                    {
                        copiedLong = await CopyLongAsync(oldLong, newLong, warnings, cancellationToken);
                        await CopyCatalogAsync(oldLong, newLong, warnings, cancellationToken);
                        await CopyUnitTestsAsync(oldLong, newLong, warnings, cancellationToken);
                        await CopyTestingAsync(oldLong, newLong, warnings, cancellationToken);
                    }
                    if (newBusiness != oldBusiness) copiedBusiness = await CopyBusinessAsync(oldBusiness, newBusiness, warnings, cancellationToken);
                }

                Assign(entity.ShortMemoryStore, shortSetting);
                Assign(entity.LongMemoryStore, longSetting);
                Assign(entity.BusinessMemoryStore, businessSetting);
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                shortStore.Configure(newShort);
                longStore.Configure(newLong);
                businessStore.Configure(newBusiness);
                logger.LogInformation("Memory storage: short = {Short}, long = {Long}, business = {Business}.",
                    shortStore.Description, longStore.Description, businessStore.Description);

                return new MemoryStorageUpdateResultDto(await GetAsync(cancellationToken), copiedShort, copiedLong, warnings, copiedBusiness);
            }
            finally
            {
                (targetShort as IDisposable)?.Dispose();
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }
}

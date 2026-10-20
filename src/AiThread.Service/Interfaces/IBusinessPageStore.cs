using AiThread.Common.Entities;
using AiThread.Service.Business;

namespace AiThread.Service.Interfaces;

public interface IBusinessPageStore
{
    Task<BusinessPage> SaveAsync(BusinessPageDraft draft, CancellationToken cancellationToken);

    Task<BusinessAsset> AddAssetAsync(string projectKey, Guid? pageId, byte[] data, string? caption, Guid? sourceThreadId, string author, CancellationToken cancellationToken);
}

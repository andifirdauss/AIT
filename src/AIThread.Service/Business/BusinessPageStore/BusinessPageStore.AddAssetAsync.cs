using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessPageStore
{
    // The same image is kept once per project: pasting it again returns the stored one.
    public async Task<BusinessAsset> AddAssetAsync(string projectKey, Guid? pageId, byte[] data, string? caption, Guid? sourceThreadId, string author, CancellationToken cancellationToken)
    {
        var mediaType = BusinessImages.Validate(data);
        var sha = BusinessImages.Sha256(data);

        await using var db = await store.CreateAsync(cancellationToken);
        var existing = await db.BusinessAssets.AsNoTracking()
            .Where(a => a.ProjectKey == projectKey && a.Sha256 == sha)
            .Select(a => new BusinessAsset { Id = a.Id, ProjectKey = a.ProjectKey, PageId = a.PageId, MediaType = a.MediaType, Sha256 = a.Sha256, Caption = a.Caption })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing;

        var asset = new BusinessAsset
        {
            Id = Guid.NewGuid(),
            ProjectKey = projectKey,
            PageId = pageId,
            MediaType = mediaType,
            Data = data,
            Sha256 = sha,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : Clip(caption.Trim(), 300),
            SourceThreadId = sourceThreadId,
            CreatedBy = Clip(author, 200),
            CreatedAt = DateTime.UtcNow
        };
        db.BusinessAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return asset;
    }
}

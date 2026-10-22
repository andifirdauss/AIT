using AIThread.Common.Entities;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessPageStore
{
    public async Task<BusinessPage> SaveAsync(BusinessPageDraft draft, CancellationToken cancellationToken)
    {
        draft = await NormalizeAsync(draft, cancellationToken);
        await using var db = await store.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        BusinessPage page;
        if (draft.Id is { } id)
        {
            page = await db.BusinessPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                   ?? throw new AppValidationException("Halaman bisnis tidak ditemukan (mungkin sudah dihapus).");
            if (draft.ExpectedVersion is { } expected && expected != page.Version)
                throw Conflict(page);
            page.Version++;
            page.UpdatedAt = now;
        }
        else
        {
            page = new BusinessPage
            {
                Id = Guid.NewGuid(),
                ProjectKey = draft.ProjectKey,
                Slug = await UniqueSlugAsync(db, draft.ProjectKey, BusinessMarkdown.Slug(draft.Module, draft.Title), cancellationToken),
                CreatedAt = now
            };
            db.BusinessPages.Add(page);
        }

        page.ProjectName = draft.ProjectName ?? page.ProjectName;
        page.Module = Clip(draft.Module.Trim(), 100);
        page.Title = Clip(draft.Title.Trim(), 200);
        page.Summary = string.IsNullOrWhiteSpace(draft.Summary) ? null : Clip(secrets.Redact(draft.Summary.Trim()), 1000);
        page.Labels = Clip(string.IsNullOrWhiteSpace(draft.Labels) ? DefaultLabels(draft.Module, draft.Title) : draft.Labels.Trim(), 500);
        page.Content = secrets.Redact(draft.Content);
        page.AutoUpdate = draft.AutoUpdate;
        page.UpdatedBy = Clip(draft.Author, 200);
        page.LastEditKind = draft.EditKind;

        db.BusinessPageRevisions.Add(new BusinessPageRevision
        {
            Id = Guid.NewGuid(),
            PageId = page.Id,
            Version = page.Version,
            Title = page.Title,
            Content = page.Content,
            ChangeNote = draft.ChangeNote is null ? null : Clip(draft.ChangeNote.Trim(), 1000),
            Author = page.UpdatedBy,
            EditKind = draft.EditKind,
            SourceThreadIds = draft.SourceThreadIds.Count == 0 ? null : Clip(string.Join(',', draft.SourceThreadIds), 2000),
            CreatedAt = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Saved by someone else between the read above and this write.
            throw Conflict(null);
        }

        // Images uploaded while writing the page belong to it from now on.
        var assetIds = BusinessMarkdown.AssetIds(page.Content);
        if (assetIds.Count > 0)
        {
            var key = page.ProjectKey;
            var pageId = page.Id;
            await db.BusinessAssets
                .Where(a => assetIds.Contains(a.Id) && a.PageId == null && a.ProjectKey == key)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PageId, pageId), cancellationToken);
        }
        return page;
    }
}

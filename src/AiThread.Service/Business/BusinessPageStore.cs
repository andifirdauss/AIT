using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Memory;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

// A page as it should be after a save. Id null creates the page; ExpectedVersion is the version the change was made
// from (null = no check, used only when creating).
public sealed record BusinessPageDraft(
    Guid? Id,
    string ProjectKey,
    string? ProjectName,
    string Module,
    string Title,
    string? Summary,
    string Labels,
    string Content,
    bool AutoUpdate,
    int? ExpectedVersion,
    BusinessEditKind EditKind,
    string Author,
    string? ChangeNote,
    IReadOnlyList<Guid> SourceThreadIds);

// Saving business pages and their images. Every save writes a revision; a page changed by someone else since it was
// read is never overwritten (BusinessPageConflictException), since several developers may share the store.
public sealed class BusinessPageStore(BusinessMemoryStore store)
{
    public const int MaxContentChars = 200_000;

    public async Task<BusinessPage> SaveAsync(BusinessPageDraft draft, CancellationToken cancellationToken)
    {
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
        page.Summary = string.IsNullOrWhiteSpace(draft.Summary) ? null : Clip(draft.Summary.Trim(), 1000);
        page.Labels = Clip(string.IsNullOrWhiteSpace(draft.Labels) ? DefaultLabels(draft.Module, draft.Title) : draft.Labels.Trim(), 500);
        page.Content = draft.Content;
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

    private static async Task<string> UniqueSlugAsync(IBusinessMemoryDb db, string projectKey, string slug, CancellationToken cancellationToken)
    {
        var taken = await db.BusinessPages.AsNoTracking()
            .Where(p => p.ProjectKey == projectKey && p.Slug.StartsWith(slug))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);
        if (!taken.Contains(slug)) return slug;
        for (var i = 2; ; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static BusinessPageConflictException Conflict(BusinessPage? current) => new(current is null
        ? "Halaman ini baru saja diubah orang lain. Muat ulang halaman, lalu terapkan perubahan Anda lagi."
        : $"Halaman ini sudah diubah {current.UpdatedBy ?? "orang lain"} (versi {current.Version}) sejak Anda membukanya. Muat ulang halaman, lalu terapkan perubahan Anda lagi.");

    private static string DefaultLabels(string module, string title) =>
        string.Join(", ", $"{module} {title}".Split([' ', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 2)
            .Distinct());

    private static string Clip(string value, int max) => value.Length > max ? value[..max] : value;
}

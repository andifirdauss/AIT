using System.Text.RegularExpressions;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    private async Task<(bool Created, int Images)> WritePageAsync(
        string model, string author, string projectKey, string projectName, BusinessPage? page, PageTarget target,
        IReadOnlyList<ThreadDigest> threads, CancellationToken cancellationToken)
    {
        // Another developer's AIThread may save the same page meanwhile: on a conflict, rewrite from the newer version.
        for (var attempt = 1; ; attempt++)
        {
            var images = threads.SelectMany(t => t.Images.Select(i => (Thread: t.ThreadId, Image: i))).Take(BusinessDigestCollector.MaxImagesPerBatch).ToList();
            var answer = await AskWriterAsync(model, projectName, page, target, threads, images.Select(i => i.Image).ToList(), cancellationToken);

            // Only the screenshots the page shows are stored.
            var assetIds = new Dictionary<int, Guid>();
            foreach (Match match in ImagePlaceholder().Matches(answer.Content))
            {
                var number = int.Parse(match.Groups["n"].Value);
                if (number < 1 || number > images.Count || assetIds.ContainsKey(number)) continue;
                var (threadId, image) = images[number - 1];
                var asset = await pages.AddAssetAsync(projectKey, page?.Id, Convert.FromBase64String(image.Data), null, threadId, author, cancellationToken);
                assetIds[number] = asset.Id;
            }
            var content = ImagePlaceholder().Replace(answer.Content, m =>
                assetIds.TryGetValue(int.Parse(m.Groups["n"].Value), out var assetId) ? BusinessMarkdown.AssetScheme + assetId : m.Value);
            // Images the model numbered beyond what it was given would render broken.
            content = UnknownImage().Replace(content, string.Empty);
            var used = assetIds.Count;

            try
            {
                var saved = await pages.SaveAsync(new BusinessPageDraft(
                    page?.Id, projectKey, projectName,
                    page?.Module ?? target.Module, page?.Title ?? target.Title,
                    answer.Summary ?? page?.Summary, answer.Labels ?? page?.Labels ?? string.Empty, content,
                    page?.AutoUpdate ?? true, page?.Version, BusinessEditKind.Ai, author,
                    answer.ChangeNote ?? "Diperbarui dari pekerjaan developer", threads.Select(t => t.ThreadId).ToList()), cancellationToken);
                return (page is null, used);
            }
            catch (BusinessPageConflictException) when (attempt == 1 && page is not null)
            {
                await using var db = await store.CreateAsync(cancellationToken);
                page = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == page.Id, cancellationToken);
                if (page is null || !page.AutoUpdate) return (false, 0);
            }
        }
    }
}

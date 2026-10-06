using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

public sealed record BusinessBatchResult(IReadOnlyList<Guid> DigestedThreads, int PagesCreated, int PagesUpdated, int Images, IReadOnlyList<string> Warnings);

// Turns a batch of finished threads into business wiki changes: one call decides which pages the threads touch, then
// one call per page rewrites it. Threads are only marked as digested when every page they feed was saved, so a
// failure is retried with the next run.
public sealed partial class BusinessMemoryWriter(
    INineRouterService router,
    ISettingService settingService,
    INodeService nodeService,
    BusinessMemoryStore store,
    BusinessPageStore pages,
    ILogger<BusinessMemoryWriter> logger)
{
    private const int RouterMaxTokens = 2_000;
    private const int WriterMaxTokens = 8_000;

    public async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.BusinessMemoryModel)) return settings.BusinessMemoryModel;
        return (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()?.Model
               ?? throw new AppValidationException("Pilih model business memory di Setting → Business memory (atau tambahkan node Planner aktif).");
    }

    public async Task<BusinessBatchResult> WriteAsync(string projectKey, string projectName, IReadOnlyList<ThreadDigest> threads, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(cancellationToken);
        var author = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        var warnings = new List<string>();
        var labels = threads.Select((t, i) => (Label: $"T{i + 1}", Thread: t)).ToDictionary(x => x.Label, x => x.Thread);

        List<BusinessPage> existing;
        await using (var db = await store.CreateAsync(cancellationToken))
            existing = await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == projectKey).ToListAsync(cancellationToken);

        var plan = await RouteAsync(model, projectName, existing, labels, cancellationToken);

        int created = 0, updated = 0, images = 0;
        var failedThreads = new HashSet<Guid>();
        foreach (var target in plan)
        {
            var feeding = target.Threads.Where(labels.ContainsKey).Select(l => labels[l]).ToList();
            if (feeding.Count == 0) continue;

            var page = target.PageId is { } id ? existing.FirstOrDefault(p => p.Id == id) : null;
            if (target.PageId is not null && page is null)
            {
                // The model named a page that does not exist; treat it as a new one of the same module and title.
                page = existing.FirstOrDefault(p => string.Equals(p.Module, target.Module, StringComparison.OrdinalIgnoreCase)
                                                    && string.Equals(p.Title, target.Title, StringComparison.OrdinalIgnoreCase));
            }
            if (page is { AutoUpdate: false }) continue;

            try
            {
                var saved = await WritePageAsync(model, author, projectKey, projectName, page, target, feeding, cancellationToken);
                if (saved.Created) created++;
                else updated++;
                images += saved.Images;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Business page {Module} — {Title} could not be written.", target.Module, target.Title);
                warnings.Add($"Halaman '{target.Module} — {target.Title}' gagal ditulis: {ex.Message}");
                foreach (var thread in feeding) failedThreads.Add(thread.ThreadId);
            }
        }

        var digested = threads.Select(t => t.ThreadId).Where(id => !failedThreads.Contains(id)).ToList();
        return new BusinessBatchResult(digested, created, updated, images, warnings);
    }

    private async Task<List<PageTarget>> RouteAsync(
        string model, string projectName, IReadOnlyList<BusinessPage> existing, IReadOnlyDictionary<string, ThreadDigest> threads, CancellationToken cancellationToken)
    {
        var input = new StringBuilder();
        input.Append("# Project\n").Append(projectName).Append("\n\n# Existing pages\n");
        if (existing.Count == 0) input.Append("(none)\n");
        foreach (var page in existing.OrderBy(p => p.Module).ThenBy(p => p.Title))
            input.Append($"- id {page.Id} · {page.Module} — {page.Title}{(page.AutoUpdate ? string.Empty : " · locked")}: {page.Summary}\n");
        input.Append("\n# Finished threads\n");
        foreach (var (label, thread) in threads) input.Append($"## {label}\n{thread.Text}\n");

        var reply = await router.ChatAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages = [ChatMessages.System(AgentPrompts.BusinessRouter), ChatMessages.User(input.ToString())],
            Temperature = 0.1,
            MaxTokens = RouterMaxTokens
        }, cancellationToken: cancellationToken);

        var json = SkillAi.ParseObject(reply.Content)
                   ?? throw new OrchestrationException("Model business memory tidak menjawab JSON yang bisa dibaca saat memilih halaman.");
        var targets = new List<PageTarget>();
        foreach (var item in json["pages"] as JsonArray ?? [])
        {
            var module = SkillAi.Text(item?["module"]);
            var title = SkillAi.Text(item?["title"]);
            var threadLabels = (item?["threads"] as JsonArray ?? []).Select(SkillAi.Text).OfType<string>().Distinct().ToList();
            if (module is null || title is null || threadLabels.Count == 0) continue;
            var id = Guid.TryParse(SkillAi.Text(item?["id"]), out var pageId) ? pageId : (Guid?)null;
            targets.Add(new PageTarget(id, module, title, threadLabels));
        }
        return targets;
    }

    private async Task<(bool Created, int Images)> WritePageAsync(
        string model, string author, string projectKey, string projectName, BusinessPage? page, PageTarget target,
        IReadOnlyList<ThreadDigest> threads, CancellationToken cancellationToken)
    {
        // Another developer's AiThread may save the same page meanwhile: on a conflict, rewrite from the newer version.
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

    private async Task<WriterAnswer> AskWriterAsync(
        string model, string projectName, BusinessPage? page, PageTarget target, IReadOnlyList<ThreadDigest> threads,
        IReadOnlyList<ImageAttachment> images, CancellationToken cancellationToken)
    {
        string Input(bool withImages)
        {
            var input = new StringBuilder();
            input.Append("# Project\n").Append(projectName).Append("\n\n");
            input.Append("# Page\n").Append($"{page?.Module ?? target.Module} — {page?.Title ?? target.Title}\n\n");
            input.Append("# Current page\n").Append(page is null ? "(new page)" : page.Content).Append("\n\n");
            input.Append("# New information from the developer threads\n");
            foreach (var thread in threads) input.Append(thread.Text).Append('\n');
            if (images.Count > 0)
            {
                input.Append(withImages
                    ? $"\n# Screenshots\n{images.Count} image(s) from these threads are attached, numbered Image 1 to Image {images.Count} in order.\n"
                    : "\n# Screenshots\nThe threads had screenshots, but they cannot be shown to you. Do not reference new images.\n");
            }
            return input.ToString();
        }

        ChatCompletionResult reply;
        try
        {
            reply = await AskAsync(Input(images.Count > 0), images);
        }
        catch (NineRouterException ex) when (images.Count > 0 && ex.StatusCode is 400 or 413 or 415 or 422)
        {
            // The model or route does not take images: write from the text alone.
            logger.LogInformation(ex, "Business writer model refused images; retrying without them.");
            reply = await AskAsync(Input(false), null);
        }

        var json = SkillAi.ParseObject(reply.Content);
        var content = SkillAi.Text(json?["content"]);
        if (content is null || content.Length < 20)
            throw new OrchestrationException(reply.FinishReason == "length"
                ? "jawaban model terpotong (halaman terlalu panjang untuk batas token)"
                : "model tidak menjawab JSON berisi halaman");
        if (content.Length > BusinessPageStore.MaxContentChars) throw new OrchestrationException("halaman hasil model terlalu panjang");

        return new WriterAnswer(content, SkillAi.Text(json?["summary"]), SkillAi.Text(json?["labels"]), SkillAi.Text(json?["changeNote"]));

        Task<ChatCompletionResult> AskAsync(string input, IReadOnlyList<ImageAttachment>? attached) => router.ChatAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages = [ChatMessages.System(AgentPrompts.BusinessWriter), ChatMessages.User(input, attached)],
            Temperature = 0.2,
            MaxTokens = WriterMaxTokens
        }, cancellationToken: cancellationToken);
    }

    [GeneratedRegex(@"asset:img(?<n>\d{1,2})\b")]
    private static partial Regex ImagePlaceholder();

    [GeneratedRegex(@"!\[[^\]]*\]\(asset:img\d+[^)]*\)")]
    private static partial Regex UnknownImage();

    private sealed record PageTarget(Guid? PageId, string Module, string Title, IReadOnlyList<string> Threads);

    private sealed record WriterAnswer(string Content, string? Summary, string? Labels, string? ChangeNote);
}

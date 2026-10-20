using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
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
}

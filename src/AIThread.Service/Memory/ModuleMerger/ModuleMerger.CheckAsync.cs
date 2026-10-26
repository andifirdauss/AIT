using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Common.Memory;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Skills;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    // Looks for duplicates of the given modules (all modules when null). Returns merges applied plus suggestions made.
    public async Task<int> CheckAsync(string key, IReadOnlyCollection<string>? focus, CancellationToken cancellationToken)
    {
        var count = 0;

        // 1. Same name written differently (case, separators, plural): merged right away.
        var modules = (await registry.ListAsync(key, cancellationToken: cancellationToken)).ToList();
        var usage = await UsageAsync(key, cancellationToken);
        foreach (var (a, b) in Pairs(modules, focus).Where(p => MemoryLabels.SameName(p.A.Name, p.B.Name)).ToList())
        {
            var (from, into) = Keep(a, b, usage);
            try
            {
                await MergeAsync(key, from.Name, into.Name, Lang.T("Nama sama dengan penulisan berbeda.", "The same name written differently."), 1.0, AutoDecider, cancellationToken);
                count++;
            }
            catch (AppValidationException)
            {
                // Already folded by an earlier pair.
            }
        }

        // 2. The model judges the rest (translations, longer names, typos).
        modules = (await registry.ListAsync(key, cancellationToken: cancellationToken)).ToList();
        if (modules.Count < 2 || !await ai.IsAvailableAsync(cancellationToken)) return count;
        var focusNames = focus?.Select(f => ModuleRegistry.Resolve(modules, f)).OfType<string>().Distinct().ToList();
        if (focusNames is { Count: 0 }) return count;

        JsonNode? json;
        try
        {
            (json, _) = await ai.AskJsonAsync(AgentPrompts.ModuleDuplicates, await InputAsync(key, modules, focusNames, cancellationToken), MaxTokens, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Duplicate module check of {Key} failed.", key);
            return count;
        }

        var decided = await DecidedPairsAsync(key, cancellationToken);
        foreach (var item in (json?["merges"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var current = await registry.ListAsync(key, includeMerged: true, cancellationToken);
            var from = ModuleRegistry.Resolve(current, SkillAi.Text(item["from"]));
            var into = ModuleRegistry.Resolve(current, SkillAi.Text(item["into"]));
            if (from is null || into is null || string.Equals(from, into, StringComparison.OrdinalIgnoreCase)) continue;
            if (decided.Contains(PairKey(from, into))) continue;

            var confidence = Math.Clamp(SkillAi.Number(item["confidence"]) ?? 0, 0, 1);
            var reason = SkillAi.Text(item["reason"]);
            if (confidence >= AutoMergeConfidence)
            {
                await MergeAsync(key, from, into, reason, confidence, AutoDecider, cancellationToken);
            }
            else
            {
                await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
                catalog.ModuleMergeLogs.Add(new ModuleMergeLog
                {
                    Id = Guid.NewGuid(), ProjectKey = key, FromModule = from, IntoModule = into, Status = ModuleMergeStatus.Suggested,
                    Reason = Clip(reason, 1000), Confidence = confidence, CreatedAt = DateTime.UtcNow
                });
                await catalog.SaveChangesAsync(cancellationToken);
                activityLog.Info("memory", Lang.T($"Saran merge modul {from} → {into} (keyakinan {confidence:P0}) menunggu review di menu Memori.", $"Module merge suggestion {from} → {into} (confidence {confidence:P0}) awaits review in the Memory menu."));
                Publish("suggested", key);
            }
            decided.Add(PairKey(from, into));
            count++;
        }
        return count;
    }
}

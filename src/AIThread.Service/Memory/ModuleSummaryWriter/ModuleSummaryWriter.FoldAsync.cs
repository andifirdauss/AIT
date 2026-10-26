using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Memory;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Folds what waits for one module into its summary. Null when nothing waited or the model gave no usable state.
    private async Task<Folded?> FoldAsync(ILongMemoryDb db, Pending pending, bool withFacts, CancellationToken cancellationToken)
    {
        var (key, module) = (pending.Key, pending.Module);
        var summaries = (await db.LongMemories
                .Where(m => m.Type == MemoryType.Project && m.ProjectKey == key && m.Kind == MemoryKind.Summary && m.Module != null)
                .ToListAsync(cancellationToken))
            .Where(m => string.Equals(m.Module, module, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Id == SummaryId(key, module) ? 0 : 1)
            .ThenBy(m => m.CreatedAt)
            .ToList();
        var current = summaries.FirstOrDefault();
        var others = summaries.Skip(1).ToList();
        var watermark = summaries.Count == 0 ? NoWatermark : summaries.Max(s => s.PromotedAt);

        var due = (await db.LongMemories.AsNoTracking()
                .Where(m => m.Type == MemoryType.Project && m.ProjectKey == key && m.Kind == MemoryKind.Change && m.PromotedAt > watermark
                    && m.LabelVersion != null && m.ReviewReason == null)
                .ToListAsync(cancellationToken))
            .Where(m => MemoryLabels.HasModule(m.Module, m.ExtraModules, module))
            .OrderBy(m => m.PromotedAt)
            .ThenBy(m => m.Id)
            .ToList();
        var changes = Batch(due);

        // Kind is stored as text, so the facts are named one by one rather than compared as a range.
        var facts = !withFacts ? [] : (await db.LongMemories
                .Where(m => m.Type == MemoryType.Project && m.ProjectKey == key && (m.Kind == MemoryKind.Pitfall || m.Kind == MemoryKind.Rule
                    || m.Kind == MemoryKind.Flow || m.Kind == MemoryKind.Convention || m.Kind == MemoryKind.Architecture || m.Kind == MemoryKind.Pattern)
                    && m.LabelVersion != null && m.ReviewReason == null && m.Module != null)
                .ToListAsync(cancellationToken))
            .Where(m => string.Equals(m.Module, module, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.CreatedAt)
            .Take(MaxFactsPerCall)
            .ToList();
        // A summary a model wrote in another language (Vietnamese, say) is written again even without new entries.
        var foreign = summaries.Any(s => TextLanguage.IsForeign(s.Description));
        if (changes.Count + facts.Count + others.Count == 0 && !foreign) return null;

        var language = await LanguageAsync(db, key, changes.Concat(facts).Select(m => m.Description), current, cancellationToken);
        var input = BuildInput(module, language, current, others, changes, facts);
        var (json, _) = await ai.AskJsonAsync(AgentPrompts.ModuleSummary, input, MaxTokens, cancellationToken);
        var state = ChangeLog.ParseState(json as JsonObject);
        if (ChangeLog.IsEmpty(state)) return null;
        var english = language == TextLanguage.English;
        var description = ChangeLog.Render(module, state, english);
        if (TextLanguage.IsForeign(description))
        {
            // Never stored: the facts stay, and the next pass asks again.
            logger.LogWarning("Summary of module {Module} ({Key}) came back in another language than {Language}; not saved.", module, key, language);
            return null;
        }

        var sources = changes.Concat(facts).Concat(summaries).ToList();
        var layers = sources.SelectMany(m => MemoryLabels.ParseLayers(m.Layers)).Distinct().Order().ToList();
        var keywords = MemoryLabels.Keywords(MemoryAi.Strings(json?["keywords"]), module);
        if (keywords.Count == 0) keywords = MemoryLabels.Keywords(sources.SelectMany(m => MemoryLabels.SplitCsv(m.Keywords)), module);
        var name = current?.Module ?? module;

        var summary = current ?? new LongMemory
        {
            Id = SummaryId(key, module),
            Type = MemoryType.Project,
            ProjectKey = key,
            Kind = MemoryKind.Summary,
            CreatedAt = DateTime.UtcNow
        };
        summary.ProjectId ??= sources.Select(m => m.ProjectId).FirstOrDefault(id => id is not null);
        summary.ProjectName ??= sources.Select(m => m.ProjectName).FirstOrDefault(n => !string.IsNullOrEmpty(n));
        summary.Module = name;
        summary.ExtraModules = null;
        summary.Area = null;
        summary.Layers = MemoryLabels.LayersCsv(layers);
        summary.Keywords = MemoryLabels.JoinCsv(keywords);
        summary.Label = MemoryLabels.Compose(MemoryLabels.Display(null, name, layers, MemoryKind.Summary), keywords);
        summary.Description = name == module ? description : ChangeLog.Render(name, state, english);
        summary.LabelVersion = MemoryLabels.CurrentVersion;
        summary.PromotedAt = changes.Count == 0 ? watermark : changes[^1].PromotedAt;
        if (current is null) db.LongMemories.Add(summary);
        foreach (var other in others) db.LongMemories.Remove(other);
        // The facts live on in the summary now (and in the backup). One save: a process stopped halfway never leaves
        // a summary that holds facts still waiting to be folded again.
        db.LongMemories.RemoveRange(facts);
        await db.SaveChangesAsync(cancellationToken);
        return new Folded(summary, changes.Count, facts.Count);
    }
}

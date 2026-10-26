using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Modules with change entries newer than their summary, older facts, or more than one summary; the ones with the
    // most waiting first.
    private static async Task<IReadOnlyList<Pending>> PendingAsync(ILongMemoryDb db, bool withFacts, CancellationToken cancellationToken)
    {
        var rows = await db.LongMemories.AsNoTracking()
            .Where(m => m.Type == MemoryType.Project && m.ProjectKey != null && m.Module != null && m.LabelVersion != null && m.ReviewReason == null && m.Kind != null)
            .Select(m => new { m.ProjectKey, m.Module, m.ExtraModules, m.Kind, m.PromotedAt })
            .ToListAsync(cancellationToken);

        var watermarks = rows.Where(r => r.Kind == MemoryKind.Summary)
            .GroupBy(r => (r.ProjectKey!, r.Module!.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Watermark: g.Max(r => r.PromotedAt)));

        var pending = new Dictionary<(string, string), (string Module, int Changes, int Facts)>();
        void Add(string key, string module, bool change)
        {
            var id = (key, module.ToLowerInvariant());
            var (_, changes, facts) = pending.GetValueOrDefault(id, (module, 0, 0));
            pending[id] = change ? (module, changes + 1, facts) : (module, changes, facts + 1);
        }

        foreach (var row in rows)
        {
            if (row.Kind == MemoryKind.Change)
            {
                foreach (var module in MemoryLabels.AllModules(row.Module, MemoryLabels.SplitCsv(row.ExtraModules)))
                {
                    var due = !watermarks.TryGetValue((row.ProjectKey!, module.ToLowerInvariant()), out var summary) || row.PromotedAt > summary.Watermark;
                    if (due) Add(row.ProjectKey!, module, change: true);
                }
            }
            else if (withFacts && MemoryLabels.IsFact(row.Kind))
            {
                Add(row.ProjectKey!, row.Module!, change: false);
            }
        }
        // Modules merged into one keep the summary of each: those are folded into one even without new entries.
        foreach (var group in rows.Where(r => r.Kind == MemoryKind.Summary).GroupBy(r => (r.ProjectKey!, r.Module!.ToLowerInvariant())).Where(g => g.Count() > 1))
            pending.TryAdd(group.Key, (group.First().Module!, 0, 0));

        return pending
            .Select(p => new Pending(p.Key.Item1, p.Value.Module, p.Value.Changes, p.Value.Facts,
                watermarks.TryGetValue(p.Key, out var s) ? s.Count - 1 : 0))
            .OrderByDescending(p => p.Changes + p.Facts + p.ExtraSummaries)
            .ToList();
    }
}

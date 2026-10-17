using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;

namespace AiThread.Service.Memory;

// The two search layers over the memory index. Layer 1 keeps the memories of the wanted modules (and layer/kind when
// given); layer 2 ranks them by keyword tokens weighted by rarity (IDF over the candidates), so a keyword used by half
// the project counts little and a precise identifier counts a lot. Matching is per token, never by substring.
public static class MemoryRanker
{
    // Module names that never come from free text: too generic ("EF Core" is not the Core module).
    private static readonly HashSet<string> NotDetected = new(StringComparer.OrdinalIgnoreCase) { ModuleRegistry.CoreModule };

    // Modules named in text: the whole name (or alias) as a token, or every hump of a multi-word name.
    public static IReadOnlyList<string> DetectModules(IReadOnlyList<ProjectModule> modules, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return [];
        var result = new List<string>();
        foreach (var module in modules.Where(m => m.MergedInto is null && !NotDetected.Contains(m.Name)))
        {
            foreach (var name in MemoryLabels.SplitCsv(module.Aliases).Prepend(module.Name))
            {
                if (!Named(name, tokens)) continue;
                result.Add(module.Name);
                break;
            }
        }
        return result;
    }

    public static List<MemoryIndexEntry> RankLabeled(
        IReadOnlyList<MemoryIndexEntry> entries,
        IReadOnlyCollection<string>? modules,
        MemoryLayer? layer,
        MemoryKind? kind,
        IReadOnlyList<string> tokens,
        int take)
    {
        if (take <= 0) return [];
        IEnumerable<MemoryIndexEntry> candidates = entries.Where(e => e.Labeled);
        var narrowed = modules is { Count: > 0 };
        if (narrowed) candidates = candidates.Where(e => modules!.Contains(e.Module!, StringComparer.OrdinalIgnoreCase));
        if (layer is { } l) candidates = candidates.Where(e => e.Layers?.Contains(l) == true);
        if (kind is { } k) candidates = candidates.Where(e => e.Kind == k);
        var list = candidates.ToList();
        if (list.Count == 0) return [];

        var tokenSets = list.ToDictionary(e => e.Id, e => EntryTokens(e));
        var idf = Idf(tokenSets.Values, list.Count);
        var scored = list.Select(e => (Entry: e, Score: Score(tokenSets[e.Id], idf, tokens) + LabelBonus(e, tokens))).ToList();

        // Narrowed by module (or layer/kind): the whole layer stays, best matches first. Otherwise only matches count.
        if (!narrowed && layer is null && kind is null)
        {
            if (tokens.Count == 0)
                return list.OrderBy(e => e.Level).ThenByDescending(e => e.CreatedAt).Take(take).ToList();
            scored = scored.Where(x => x.Score > 0).ToList();
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.HitCount)
            .ThenBy(x => x.Entry.Level)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .Take(take)
            .Select(x => x.Entry)
            .ToList();
    }

    // Memory written by an older version: its free label, matched per token.
    public static List<MemoryIndexEntry> RankUnlabeled(IReadOnlyList<MemoryIndexEntry> entries, IReadOnlyList<string> tokens, int take)
    {
        if (take <= 0 || tokens.Count == 0) return [];
        return entries
            .Where(e => !e.Labeled && e.Type != MemoryType.Business)
            .Select(e => (Entry: e, Score: Matches(FreeTokens(e.Label), tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Level)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .Take(take)
            .Select(x => x.Entry)
            .ToList();
    }

    // Business wiki pages: pages of the wanted modules first, then by token matches; the newest when nothing is asked.
    public static List<MemoryIndexEntry> RankPages(IReadOnlyList<MemoryIndexEntry> pages, IReadOnlyCollection<string>? modules, IReadOnlyList<string> tokens, int take)
    {
        if (take <= 0) return [];
        if (tokens.Count == 0 && modules is not { Count: > 0 })
            return pages.OrderByDescending(p => p.CreatedAt).Take(take).ToList();

        return pages
            .Select(p => (Entry: p, Score: Matches(FreeTokens(p.Label), tokens)
                                           + (modules?.Contains(p.Module ?? string.Empty, StringComparer.OrdinalIgnoreCase) == true ? 2 : 0)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .Take(take)
            .Select(x => x.Entry)
            .ToList();
    }

    // Jaccard similarity of two memories' keyword tokens (duplicate candidates in Memory → Perlu review).
    public static double Similarity(MemoryIndexEntry a, MemoryIndexEntry b)
    {
        var x = EntryTokens(a);
        var y = EntryTokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    // Pairs of labeled memories that likely say the same thing: same module and kind, keyword overlap at or above the
    // threshold. Most similar first.
    public static List<(MemoryIndexEntry First, MemoryIndexEntry Second, double Similarity)> FindDuplicates(
        IReadOnlyList<MemoryIndexEntry> entries, double threshold = 0.6, int max = 50)
    {
        var result = new List<(MemoryIndexEntry, MemoryIndexEntry, double)>();
        foreach (var group in entries.Where(e => e.Labeled).GroupBy(e => (Module: e.Module!.ToLowerInvariant(), e.Kind, e.Type)))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                var similarity = Similarity(list[i], list[j]);
                if (similarity >= threshold) result.Add((list[i], list[j], similarity));
            }
        }
        return result.OrderByDescending(r => r.Item3).Take(max).ToList();
    }

    private static bool Named(string name, IReadOnlyList<string> tokens)
    {
        var whole = MemoryLabels.Compact(name);
        if (whole.Length < 3) return false;
        if (tokens.Any(t => MemoryLabels.TokenMatches(t, whole))) return true;
        var parts = MemoryLabels.KeywordTokens(name).Where(p => p != whole).ToList();
        return parts.Count >= 2 && parts.All(p => tokens.Any(t => MemoryLabels.TokenMatches(t, p)));
    }

    private static HashSet<string> EntryTokens(MemoryIndexEntry entry)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in entry.Keywords ?? []) set.UnionWith(MemoryLabels.KeywordTokens(keyword));
        return set;
    }

    private static HashSet<string> FreeTokens(string label) => MemoryLabels.KeywordTokens(label).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, double> Idf(IEnumerable<HashSet<string>> sets, int count)
    {
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var set in sets)
            foreach (var token in set)
                df[token] = df.GetValueOrDefault(token) + 1;
        return df.ToDictionary(p => p.Key, p => Math.Log(1 + (double)count / p.Value));
    }

    private static double Score(HashSet<string> entryTokens, Dictionary<string, double> idf, IReadOnlyList<string> tokens)
    {
        var score = 0d;
        foreach (var token in tokens)
        {
            var best = 0d;
            foreach (var candidate in entryTokens)
                if (MemoryLabels.TokenMatches(token, candidate)) best = Math.Max(best, idf.GetValueOrDefault(candidate, 1));
            score += best;
        }
        return score;
    }

    // A query that names a kind or a layer ("flow", "pitfall", "test") nudges those memories up.
    private static double LabelBonus(MemoryIndexEntry entry, IReadOnlyList<string> tokens)
    {
        var bonus = 0d;
        if (entry.Kind is { } kind && tokens.Contains(kind.ToString().ToLowerInvariant())) bonus += 0.5;
        foreach (var layer in entry.Layers ?? [])
            if (tokens.Contains(layer.ToString().ToLowerInvariant())) bonus += 0.25;
        return bonus;
    }

    private static int Matches(HashSet<string> candidates, IReadOnlyList<string> tokens) =>
        tokens.Count(t => candidates.Any(c => MemoryLabels.TokenMatches(t, c)));
}

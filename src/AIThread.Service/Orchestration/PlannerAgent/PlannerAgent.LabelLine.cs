namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // First entry of the planner phase: what the label search put in the brief, before the model is called.
    // "🔖 Label · 2 file → Thread·Logic, Memory·Data · 14 memory relevan"; a follow-up adds "dari sesi (thread …)".
    public static string LabelLine(IReadOnlyList<Memory.CodeMapHit>? codeMap, int relevant, SessionLabelSource? fromSession = null)
    {
        var parts = new List<string> { "🔖 Label" };
        if (fromSession is not null)
            parts.Add(fromSession.ThreadId is { } threadId ? $"dari sesi (thread {ThreadAnchor.ShortId(threadId)})" : "dari sesi");
        if (codeMap is { Count: > 0 })
        {
            var labels = codeMap.Select(h => $"{h.Module}·{h.Layer}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var shown = string.Join(", ", labels.Take(MaxLabelsShown));
            if (labels.Count > MaxLabelsShown) shown += $", +{labels.Count - MaxLabelsShown} lainnya";
            parts.Add($"{codeMap.Count} file → {shown}");
        }
        parts.Add(relevant > 0 ? $"{relevant} memory relevan" : "belum ada memory relevan");
        return string.Join(" · ", parts);
    }
}

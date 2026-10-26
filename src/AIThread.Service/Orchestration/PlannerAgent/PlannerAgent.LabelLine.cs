using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // First entry of the planner phase: what the label search put in the brief, before the model is called.
    // "🔖 Label · 2 file → Thread·Logic, Memory·Data · 14 memory relevan"; a follow-up adds "dari sesi (thread …)".
    public static string LabelLine(IReadOnlyList<Memory.CodeMapHit>? codeMap, int relevant, SessionLabelSource? fromSession = null)
    {
        var parts = new List<string> { "🔖 Label" };
        if (fromSession is not null)
            parts.Add(fromSession.ThreadId is { } threadId ? Lang.T($"dari sesi (thread {ThreadAnchor.ShortId(threadId)})", $"from the session (thread {ThreadAnchor.ShortId(threadId)})") : Lang.T("dari sesi", "from the session"));
        if (codeMap is { Count: > 0 })
        {
            var labels = codeMap.Select(h => $"{h.Module}·{h.Layer}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var shown = string.Join(", ", labels.Take(MaxLabelsShown));
            if (labels.Count > MaxLabelsShown) shown += Lang.T($", +{labels.Count - MaxLabelsShown} lainnya", $", +{labels.Count - MaxLabelsShown} more");
            parts.Add($"{codeMap.Count} file → {shown}");
        }
        parts.Add(relevant > 0 ? Lang.T($"{relevant} memory relevan", $"{relevant} relevant memories") : Lang.T("belum ada memory relevan", "no relevant memory yet"));
        return string.Join(" · ", parts);
    }
}

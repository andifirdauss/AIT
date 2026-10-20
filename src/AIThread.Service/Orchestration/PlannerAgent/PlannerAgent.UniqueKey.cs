namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // A short identifier from the node name for investigate's agent enum: "Scout Murah" → "scout-murah".
    private static string UniqueKey(string name, HashSet<string> taken)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length == 0) slug = "sub";
        var key = slug;
        for (var i = 2; !taken.Add(key); i++) key = $"{slug}-{i}";
        return key;
    }
}

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Keeps the head and the tail: the developer's instruction usually sits at the end of a long message.
    public static string Limit(string text, int max)
    {
        if (text.Length <= max) return text;
        var head = max / 3;
        var tail = max - head;
        return text[..head] + $"\n... ({text.Length - max} chars truncated) ...\n" + text[^tail..];
    }
}

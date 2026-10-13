using System.Globalization;
using System.Text;

namespace AiThread.Service.Orchestration;

public sealed record TurnTask(string Key, string File, string Action, string Title, string Status, string? Note);

// One finished thread as the context keeper and read_thread see it.
public sealed record TurnRecord(
    Guid ThreadId,
    DateTime CreatedAt,
    string Status,
    string Prompt,
    string? PlanSummary,
    IReadOnlyList<ContractItem> Contract,
    IReadOnlyList<TurnTask> Tasks,
    string? Reply,
    string? Error);

// Text the context keeper reads and writes. Pure functions, so the format is covered by unit tests.
public static class ContextPackBuilder
{
    // Roughly 3K tokens: what every planner run of the chat pays on top of its own brief.
    public const int MaxPackChars = 12_000;

    // Turns appended without a model (no context keeper node, or its call failed); the next model run folds them in.
    public const string UnsummarizedHeading = "## Not yet summarized";

    public static string Describe(TurnRecord turn, int maxRequestChars, int maxReplyChars)
    {
        var builder = new StringBuilder();
        builder.Append($"### thread {ThreadAnchor.ShortId(turn.ThreadId)} · {turn.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC · {turn.Status}\n");
        builder.Append("Request: ").Append(Shorten(turn.Prompt, maxRequestChars)).Append('\n');
        if (!string.IsNullOrWhiteSpace(turn.PlanSummary)) builder.Append("Plan: ").Append(turn.PlanSummary.Trim()).Append('\n');
        if (turn.Contract.Count > 0)
            builder.Append("Contract: ").Append(string.Join("; ", turn.Contract.Select(c => $"{c.Name} = {c.Value}"))).Append('\n');
        if (turn.Tasks.Count > 0)
        {
            builder.Append("Tasks:\n");
            foreach (var task in turn.Tasks)
            {
                builder.Append($"- {task.Key} {task.Action} `{task.File}` — {task.Title} [{task.Status}]");
                if (!string.IsNullOrWhiteSpace(task.Note)) builder.Append(": ").Append(Shorten(task.Note, 400));
                builder.Append('\n');
            }
        }
        if (!string.IsNullOrWhiteSpace(turn.Reply)) builder.Append("Reply: ").Append(Shorten(turn.Reply, maxReplyChars)).Append('\n');
        if (!string.IsNullOrWhiteSpace(turn.Error)) builder.Append("Error: ").Append(Shorten(turn.Error, 600)).Append('\n');
        return builder.ToString();
    }

    // previousPack for an incremental update, earlierTurns for a rebuild; conversation is chat text that no thread
    // record covers (turns answered outside AiThread, or the chat before its first thread).
    public static string Input(string? previousPack, IReadOnlyList<TurnRecord> earlierTurns, string? conversation, TurnRecord current)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(previousPack))
        {
            builder.Append("# Current session context\n").Append(previousPack.Trim()).Append("\n\n");
        }
        else if (earlierTurns.Count > 0)
        {
            builder.Append("# Earlier turns (oldest first)\n");
            foreach (var turn in earlierTurns) builder.Append(Describe(turn, 1_000, 800)).Append('\n');
        }
        else
        {
            builder.Append("# Current session context\n(none — this is the first AIT turn of the conversation)\n\n");
        }

        if (!string.IsNullOrWhiteSpace(conversation))
            builder.Append("# Other conversation in the chat (oldest first, truncated)\n").Append(conversation.Trim()).Append("\n\n");

        builder.Append("# Turn that just finished\n").Append(Describe(current, 3_000, 3_000));
        return builder.ToString();
    }

    // Used when no model is available: keeps the previous pack and lists the new turn under UnsummarizedHeading.
    public static string Fallback(string? previousPack, IReadOnlyList<TurnRecord> earlierTurns, TurnRecord current)
    {
        string pack;
        if (!string.IsNullOrWhiteSpace(previousPack))
        {
            pack = previousPack.TrimEnd();
            if (!pack.Contains(UnsummarizedHeading, StringComparison.Ordinal)) pack += $"\n\n{UnsummarizedHeading}";
            pack += "\n" + Bullet(current);
        }
        else
        {
            pack = "## Work log\n" + string.Join("\n", earlierTurns.Append(current).Select(Bullet));
        }
        return Limit(pack);
    }

    // A direct Claude Code session the developer cleared with /clear; previousPack is the summary it started from.
    public static string SessionInput(string? previousPack, TranscriptSummary transcript, string workingDirectory)
    {
        var builder = new StringBuilder();
        builder.Append("# Current session context\n")
            .Append(string.IsNullOrWhiteSpace(previousPack) ? "(none — the cleared session did not start from a summary)" : previousPack.Trim())
            .Append("\n\n");
        builder.Append("# Claude Code session that was just cleared (digest, oldest first)\n");
        builder.Append("Working directory: ").Append(workingDirectory).Append('\n');
        if (transcript.FilesChanged.Count > 0) builder.Append("Files changed: ").Append(string.Join(", ", transcript.FilesChanged)).Append('\n');
        builder.Append('\n').Append(transcript.Digest);
        return builder.ToString();
    }

    public static string SessionFallback(string? previousPack, TranscriptSummary transcript)
    {
        var line = "- Cleared session — requests: " + (transcript.Requests.Count == 0
            ? "(none)"
            : string.Join(" | ", transcript.Requests.TakeLast(8).Select(r => Shorten(r, 150))));
        if (transcript.FilesChanged.Count > 0) line += "; files changed: " + string.Join(", ", transcript.FilesChanged);

        if (string.IsNullOrWhiteSpace(previousPack)) return Limit("## Work log\n" + line);
        var pack = previousPack.TrimEnd();
        if (!pack.Contains(UnsummarizedHeading, StringComparison.Ordinal)) pack += $"\n\n{UnsummarizedHeading}";
        return Limit(pack + "\n" + line);
    }

    // Null when the model returned nothing usable.
    public static string? Clean(string? modelOutput)
    {
        var text = modelOutput?.Trim() ?? string.Empty;
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            text = firstLineEnd < 0 ? string.Empty : text[(firstLineEnd + 1)..];
            if (text.TrimEnd().EndsWith("```", StringComparison.Ordinal)) text = text.TrimEnd()[..^3];
            text = text.Trim();
        }
        return text.Length == 0 ? null : Limit(text);
    }

    // Keeps the head (goal, decisions) and the tail (latest turns) of an oversized pack.
    public static string Limit(string pack)
    {
        if (pack.Length <= MaxPackChars) return pack;
        var half = (MaxPackChars - 20) / 2;
        return pack[..half] + "\n\n… (shortened) …\n\n" + pack[^half..];
    }

    private static string Bullet(TurnRecord turn)
    {
        var line = $"- `thread {ThreadAnchor.ShortId(turn.ThreadId)}` ({turn.Status}) — {OneLine(turn.Prompt, 200)}";
        if (!string.IsNullOrWhiteSpace(turn.PlanSummary)) line += $" → {OneLine(turn.PlanSummary, 200)}";
        if (turn.Tasks.Count > 0) line += "; files: " + string.Join(", ", turn.Tasks.Select(t => $"{t.File} [{t.Status}]"));
        if (!string.IsNullOrWhiteSpace(turn.Error)) line += $"; error: {OneLine(turn.Error, 200)}";
        return line;
    }

    private static string OneLine(string text, int max) => Shorten(text.ReplaceLineEndings(" "), max);

    private static string Shorten(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + " …";
    }
}

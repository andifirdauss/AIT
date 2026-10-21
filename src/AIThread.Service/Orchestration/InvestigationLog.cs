using System.Text;
using System.Text.Json;

namespace AIThread.Service.Orchestration;

// What the planner, and the tools nodes it delegated to, looked at while planning a thread: the project files read
// (merged line ranges), the lookups with the start of their result, and the sub agents' findings. Stored on the
// thread as JSON (ThreadRun.InvestigationJson), so a follow-up in the same chat starts from it instead of searching
// again: the next brief shows it and outlines the files, the context keeper turns it into key facts, read_thread
// shows it. Tool calls run in parallel, so every change takes the lock.
public sealed class InvestigationLog
{
    public const int MaxFiles = 40;
    public const int MaxLookups = 30;
    public const int MaxFindings = 8;
    private const int MaxResultChars = 300;
    private const int MaxQuestionChars = 400;
    private const int MaxFindingChars = 2_500;

    private readonly Lock _lock = new();

    public List<InvestigatedFile> Files { get; set; } = [];
    public List<InvestigationLookup> Lookups { get; set; } = [];
    public List<InvestigationFinding> Findings { get; set; } = [];

    public bool IsEmpty => Files.Count == 0 && Lookups.Count == 0 && Findings.Count == 0;

    // to null: as far as read_file returned (the end of the file, or its line limit).
    public void Read(string path, int from, int? to)
    {
        lock (_lock)
        {
            var file = Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (file is null)
            {
                if (Files.Count >= MaxFiles) return;
                file = new InvestigatedFile { Path = path };
                Files.Add(file);
            }
            file.Ranges.Add(new ReadRange(Math.Max(1, from), to));
            file.Ranges = Merge(file.Ranges);
        }
    }

    // query: the tool's arguments as PlannerToolRunner.Describe writes them; result: its output, or null to leave it out.
    public void Lookup(string tool, string query, string? result)
    {
        var line = result is null ? null : Shorten(string.Join("; ", result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4)), MaxResultChars);
        lock (_lock)
        {
            if (Lookups.Count >= MaxLookups || Lookups.Any(l => l.Tool == tool && l.Query == query)) return;
            Lookups.Add(new InvestigationLookup { Tool = tool, Query = query, Result = line });
        }
    }

    public void Finding(string question, string answer)
    {
        lock (_lock)
        {
            if (Findings.Count >= MaxFindings) return;
            Findings.Add(new InvestigationFinding { Question = Shorten(question, MaxQuestionChars), Answer = Shorten(answer, MaxFindingChars) });
        }
    }

    public string? ToJson()
    {
        lock (_lock) return IsEmpty ? null : JsonSerializer.Serialize(this, ThreadPlan.JsonOptions);
    }

    public static InvestigationLog? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<InvestigationLog>(json, ThreadPlan.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Text for models (the brief, the context keeper, read_thread); files come first, so clipping cuts the findings.
    public string Brief(int maxChars)
    {
        var builder = new StringBuilder();
        lock (_lock)
        {
            if (Files.Count > 0)
            {
                builder.Append("Files read (line ranges):\n");
                foreach (var file in Files)
                    builder.Append($"- `{file.Path}` ({string.Join(", ", file.Ranges.Select(r => r.To is { } to ? $"{r.From}-{to}" : $"{r.From}-…"))})\n");
            }
            if (Lookups.Count > 0)
            {
                builder.Append("Lookups:\n");
                foreach (var lookup in Lookups)
                    builder.Append($"- {lookup.Tool} {lookup.Query}{(string.IsNullOrEmpty(lookup.Result) ? string.Empty : " → " + lookup.Result)}\n");
            }
            if (Findings.Count > 0)
            {
                builder.Append("Sub agent findings:\n");
                foreach (var finding in Findings)
                    builder.Append($"- Q: {finding.Question}\n  A: {finding.Answer.ReplaceLineEndings("\n  ")}\n");
            }
        }
        return Shorten(builder.ToString().TrimEnd(), maxChars);
    }

    // The project files read, in the order they were first read.
    public IReadOnlyList<string> FilePaths()
    {
        lock (_lock) return Files.Select(f => f.Path).ToList();
    }

    private static List<ReadRange> Merge(List<ReadRange> ranges)
    {
        var merged = new List<ReadRange>();
        foreach (var range in ranges.OrderBy(r => r.From))
        {
            var last = merged.Count == 0 ? null : merged[^1];
            if (last is not null && (last.To is null || range.From <= last.To + 1))
                merged[^1] = last with { To = last.To is null || range.To is null ? null : Math.Max(last.To.Value, range.To.Value) };
            else
                merged.Add(range);
        }
        return merged;
    }

    private static string Shorten(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + " …";
    }
}

public sealed class InvestigatedFile
{
    public string Path { get; set; } = string.Empty;
    public List<ReadRange> Ranges { get; set; } = [];
}

public sealed record ReadRange(int From, int? To);

public sealed class InvestigationLookup
{
    public string Tool { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string? Result { get; set; }
}

public sealed class InvestigationFinding
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

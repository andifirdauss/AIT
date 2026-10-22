using System.Text.Json.Nodes;
using AIThread.Service.Memory;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // compact: the client progress version, with long paths and queries shortened.
    public static string Describe(string tool, JsonObject args, bool compact = false)
    {
        string? Path(string name) => Str(args, name) is { } path && compact ? ClientProgress.CompactPath(path, ProgressPathChars) : Str(args, name);
        string? Query(string name) => Str(args, name) is { } query && compact ? Clip(query, ProgressQueryChars) : Str(args, name);
        return tool switch
        {
            PlannerTools.ReadFile => $"`{Path("path")}`" + (Int(args, "startLine") is { } s ? $" ({s}-{Int(args, "endLine")?.ToString() ?? "…"})" : string.Empty),
            PlannerTools.ListDirectory => $"`{Path("path") ?? "."}`",
            PlannerTools.FindFiles => $"`{Path("pattern")}`" + (Path("path") is { } findIn ? $" in `{findIn}`" : string.Empty),
            PlannerTools.SearchText => $"\"{Query("query")}\"" + (Path("glob") is { } g ? $" in `{g}`" : string.Empty) + (Path("path") is { } searchIn ? $" under `{searchIn}`" : string.Empty),
            PlannerTools.FindSymbol => $"`{Str(args, "name")}`" + (Str(args, "kind") is { } kind ? $" ({kind})" : string.Empty) + (Path("path") is { } symbolIn ? $" under `{symbolIn}`" : string.Empty),
            PlannerTools.FindReferences => $"`{Str(args, "name")}`" + (Path("path") is { } refsIn ? $" under `{refsIn}`" : string.Empty),
            PlannerTools.CodeOutline => string.Join(" ", new[] { Path("path") is { } outlined ? $"`{outlined}`" : null, Str(args, "name") is { } type ? $"`{type}`" : null }.Where(s => s is not null)),
            PlannerTools.FunctionCalls => $"`{Path("path")}` {Str(args, "name")}",
            PlannerTools.SearchMemory => string.Join(" ", new[]
            {
                Str(args, "module"), Str(args, "layer"), Str(args, "kind"),
                MemoryAi.Strings(args["keywords"]) is { Count: > 0 } words ? "\"" + Clip(string.Join(", ", words), ProgressQueryChars) + "\"" : null,
                Str(args, "query") is not null ? $"\"{Query("query")}\"" : null
            }.Where(s => s is not null)),
            PlannerTools.SetModule => $"`{Path("path")}` → {Str(args, "module")}" + (Str(args, "layer") is { } setLayer ? $" · {setLayer}" : string.Empty),
            PlannerTools.ReadMemory => $"{(args["ids"] as JsonArray)?.Count ?? 0} item",
            PlannerTools.ReadThread => $"`{Str(args, "id")}`",
            PlannerTools.GitStatus => Path("path") is { } statusOf ? $"`{statusOf}`" : string.Empty,
            PlannerTools.GitDiff => string.Join(" ", new[]
            {
                Str(args, "base") is { } diffBase ? $"`{diffBase}`" : null, Bool(args, "staged") ? "staged" : null,
                Path("path") is { } diffOf ? $"`{diffOf}`" : null, Bool(args, "stat") ? "stat" : null
            }.Where(s => s is not null)),
            PlannerTools.GitLog => string.Join(" ", new[]
            {
                Str(args, "revision") is { } revision ? $"`{revision}`" : null, Path("path") is { } logOf ? $"`{logOf}`" : null,
                Str(args, "query") is not null ? $"\"{Query("query")}\"" : null, Str(args, "change") is not null ? $"-S \"{Query("change")}\"" : null,
                Int(args, "limit") is { } limit ? $"max {limit}" : null
            }.Where(s => s is not null)),
            PlannerTools.GitShow => $"`{Str(args, "commit")}`" + (Path("path") is { } shownIn ? $" `{shownIn}`" : string.Empty) + (Bool(args, "stat") ? " stat" : string.Empty),
            PlannerTools.GitBlame => $"`{Path("path")}`" + (Int(args, "startLine") is { } from ? $" ({from}-{Int(args, "endLine")?.ToString() ?? "…"})" : string.Empty),
            PlannerTools.RunBuild => Path("target") is { } target ? $"`{target}`" : string.Empty,
            PlannerTools.RunTests => Bool(args, "all") ? "semua"
                : Path("file") is { } testFile ? $"`{testFile}`"
                : string.Join(", ", MemoryAi.Strings(args["tests"])),
            PlannerTools.Investigate => Quote(Str(args, "question")),
            PlannerTools.AskUser => Quote(Str(args, "question")),
            _ => string.Empty
        };
    }
}

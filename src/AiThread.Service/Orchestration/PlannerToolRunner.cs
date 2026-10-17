using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Memory;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// Runs the read-only investigation tools and run_build, for the planner and for the tools nodes it delegates tools to.
// prefix marks the progress lines of a tools node ("#1 › "); empty for the planner itself.
public sealed class PlannerToolRunner(IMemoryService memoryService, ContextKeeper contextKeeper, BuildRunner buildRunner, CodeMapper codeMapper)
{
    // Paths and queries are shortened in the client progress so a tool line rarely wraps; the activity log keeps them whole.
    private const int ProgressPathChars = 48;
    private const int ProgressQueryChars = 60;

    // Parses the arguments of a tool call; null with an error outcome when they are not a JSON object.
    public static JsonObject? ParseArguments(string arguments, out PlannerToolOutcome? error)
    {
        error = null;
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            error = new PlannerToolOutcome($"Error: arguments are not valid JSON ({ex.Message}). Call the tool again with a JSON object.", null, true);
            return null;
        }
    }

    // The progress line of a tool call; run_build writes its own lines (the command it resolved, then the result).
    public static async Task AnnounceAsync(string tool, JsonObject args, string actor, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var detail = Describe(tool, args);
        recorder.PlannerActivity(tool, prefix + detail);
        recorder.Log(ActivityLevel.Debug, $"{actor} → {tool} {detail}");
        if (tool != PlannerTools.RunBuild)
            await progress.StepAsync(prefix + ClientProgress.Pad(tool, PlannerTools.NameColumn) + Describe(tool, args, compact: true), cancellationToken, hang: Hang(prefix));
    }

    public async Task<PlannerToolOutcome> RunAsync(
        string tool,
        JsonObject args,
        PlannerInput input,
        WorkspaceTools tools,
        HashSet<Guid> memoriesRead,
        string prefix,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (tool)
            {
                case PlannerTools.ListDirectory:
                    return new PlannerToolOutcome(tools.ListDirectory(Str(args, "path") ?? ".", Int(args, "depth") ?? 2), null);

                case PlannerTools.FindFiles:
                    return new PlannerToolOutcome(tools.FindFiles(Str(args, "pattern") ?? string.Empty, Str(args, "path")), null);

                case PlannerTools.SearchText:
                    return new PlannerToolOutcome(tools.SearchText(Str(args, "query") ?? string.Empty, Str(args, "glob"), Bool(args, "regex"), path: Str(args, "path")), null);

                case PlannerTools.FindSymbol:
                    return new PlannerToolOutcome(new SymbolSearch(tools.Scope).FindSymbol(Str(args, "name") ?? string.Empty, Str(args, "kind"), Str(args, "path")), null);

                case PlannerTools.FindReferences:
                    return new PlannerToolOutcome(new SymbolSearch(tools.Scope).FindReferences(Str(args, "name") ?? string.Empty, Str(args, "path")), null);

                case PlannerTools.ReadFile:
                    return new PlannerToolOutcome(await tools.ReadFileAsync(Str(args, "path") ?? string.Empty, Int(args, "startLine"), Int(args, "endLine"), cancellationToken), null);

                case PlannerTools.SearchMemory:
                    var entries = await memoryService.SearchAsync(new MemorySearch(
                        input.Project.ProjectId,
                        Str(args, "module"),
                        MemoryLabels.TryParseLayer(Str(args, "layer"), out var layer) ? layer : null,
                        MemoryLabels.TryParseKind(Str(args, "kind"), out var kind) ? kind : null,
                        MemoryAi.Strings(args["keywords"]),
                        Str(args, "query"),
                        25), cancellationToken);
                    return new PlannerToolOutcome(entries.Count == 0 ? "No matching memories." : FormatIndex(entries), null);

                case PlannerTools.SetModule:
                    return await SetModuleAsync(input, Str(args, "path"), Str(args, "module"), Str(args, "layer"), recorder, cancellationToken);

                case PlannerTools.ReadMemory:
                    var ids = (args["ids"] as JsonArray ?? [])
                        .Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : (Guid?)null)
                        .Where(g => g is not null).Select(g => g!.Value).ToList();
                    var memories = await memoryService.GetManyAsync(ids, cancellationToken);
                    lock (memoriesRead) memoriesRead.UnionWith(memories.Select(m => m.Id));
                    return new PlannerToolOutcome(memories.Count == 0
                        ? "No memories found for those ids."
                        : string.Join("\n\n", memories.Select(m => $"### {m.Label} ({m.Type}, {m.Level})\n{m.Description}")), null);

                case PlannerTools.ReadThread:
                    return new PlannerToolOutcome(await contextKeeper.ReadThreadAsync(input.Project.ProjectId, Str(args, "id") ?? string.Empty, cancellationToken), null);

                case PlannerTools.RunBuild:
                    return await RunBuildAsync(input, Str(args, "target"), prefix, recorder, progress, cancellationToken);

                default:
                    return new PlannerToolOutcome($"Error: unknown tool '{tool}'.", null, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }

    // A class or file name is resolved to the project's file first.
    private async Task<PlannerToolOutcome> SetModuleAsync(PlannerInput input, string? path, string? module, string? layerText, ThreadRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (path is null || module is null) return new PlannerToolOutcome("Error: path and module are required.", null, true);
        var target = path;
        if (!path.Contains('/') && !path.Contains('\\') && !Path.HasExtension(path))
        {
            var found = await codeMapper.ResolveReferencesAsync(input.Project, path, cancellationToken);
            if (found.Count == 0) return new PlannerToolOutcome($"Error: no file named '{path}' in the project. Give its path.", null, true);
            if (found.Count > 1) return new PlannerToolOutcome($"Error: '{path}' matches several files ({string.Join(", ", found.Take(5))}). Give the path.", null, true);
            target = found[0];
        }

        try
        {
            var hit = await codeMapper.SetManualAsync(input.Project, target, module,
                MemoryLabels.TryParseLayer(layerText, out var layer) ? layer : null, cancellationToken);
            recorder.Log(ActivityLevel.Info, $"Planner → set_module `{hit.Path}` → {hit.Module} · {hit.Layer}");
            return new PlannerToolOutcome($"Code map updated: {hit.Path} → {hit.Module} · {hit.Layer}.", null);
        }
        catch (AppValidationException ex)
        {
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }

    // Two progress lines: the command as soon as it is known, the result under it when the build is done.
    private async Task<PlannerToolOutcome> RunBuildAsync(PlannerInput input, string? target, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var head = prefix + ClientProgress.Pad(PlannerTools.RunBuild, PlannerTools.NameColumn);
        var shownTarget = target is null ? string.Empty : ClientProgress.CompactPath(target, ProgressPathChars);
        var hang = Hang(prefix);
        if (!input.Settings.BuildEnabled)
        {
            await progress.StepAsync(head + shownTarget, cancellationToken, hang: hang);
            await progress.DetailAsync("🟡 build dimatikan di setting", cancellationToken, hang);
            return new PlannerToolOutcome("Error: building is disabled in the AIT settings.", null, true);
        }

        var command = BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, target, out var error);
        if (command is null)
        {
            await progress.StepAsync(head + shownTarget, cancellationToken, hang: hang);
            await progress.DetailAsync($"❌ {error}", cancellationToken, hang);
            return new PlannerToolOutcome($"Error: {error}", null, true);
        }

        await progress.StepAsync(head + command.Display, cancellationToken, hang: hang);
        var result = await buildRunner.RunAsync(command, TimeSpan.FromSeconds(input.Settings.BuildTimeoutSeconds), cancellationToken);
        recorder.Log(result.Passed ? ActivityLevel.Info : ActivityLevel.Warning, $"Planner → run_build `{command.Display}`: {BuildReport.Headline(result)}");
        await progress.DetailAsync($"{(result.Passed ? "✅" : "❌")} {BuildReport.Headline(result)}", cancellationToken, hang);
        return new PlannerToolOutcome(BuildReport.Format(result, input.Scope), null);
    }

    // Wrapped lines of a tool call line up with its arguments.
    public static int Hang(string prefix) => ClientProgress.DisplayWidth(prefix) + PlannerTools.NameColumn;

    // Labeled: "Module · Layers · Kind — keywords"; business wiki pages and unlabeled (older) memory keep their label.
    public static string FormatIndex(IEnumerable<MemoryIndexEntry> entries) =>
        string.Concat(entries.Select(e =>
        {
            var level = e.Level.ToString().ToLowerInvariant();
            var type = e.Type.ToString().ToLowerInvariant();
            if (e.Labeled)
            {
                var keywords = e.Keywords is { Count: > 0 } list ? " — " + string.Join(", ", list) : string.Empty;
                return $"- {e.Id} [{level}/{type}, {e.CreatedAt:yyyy-MM-dd}] {MemoryLabels.Display(e.Area, e.Module!, e.Layers, e.Kind!.Value, e.ExtraModules)}{keywords}\n";
            }
            var tag = e.Type == MemoryType.Business ? type : $"{level}/{type}/unlabeled";
            return $"- {e.Id} [{tag}, {e.CreatedAt:yyyy-MM-dd}] {e.Label}\n";
        }));

    // The planner's memory map: labeled memories per module, with their layers and kinds.
    public static string FormatModuleMap(IReadOnlyList<MemoryModuleSummary> modules, int maxProject = 40, int maxKnowledge = 20)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var module in modules.Where(m => !m.Knowledge).Take(maxProject))
        {
            var layers = string.Join(" · ", module.Layers.OrderByDescending(l => l.Value).Select(l => $"{l.Key} {l.Value}"));
            var kinds = string.Join(" · ", module.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"));
            builder.Append($"- {module.Module} ({module.Count}): {(layers.Length > 0 ? layers + " | " : string.Empty)}{kinds}\n");
        }
        var knowledge = modules.Where(m => m.Knowledge).Take(maxKnowledge).Select(m => $"{m.Module} ({m.Count})").ToList();
        if (knowledge.Count > 0) builder.Append("- Knowledge: ").Append(string.Join(" · ", knowledge)).Append('\n');
        return builder.ToString();
    }

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
            PlannerTools.SearchMemory => string.Join(" ", new[]
            {
                Str(args, "module"), Str(args, "layer"), Str(args, "kind"),
                MemoryAi.Strings(args["keywords"]) is { Count: > 0 } words ? "\"" + Clip(string.Join(", ", words), ProgressQueryChars) + "\"" : null,
                Str(args, "query") is not null ? $"\"{Query("query")}\"" : null
            }.Where(s => s is not null)),
            PlannerTools.SetModule => $"`{Path("path")}` → {Str(args, "module")}" + (Str(args, "layer") is { } setLayer ? $" · {setLayer}" : string.Empty),
            PlannerTools.ReadMemory => $"{(args["ids"] as JsonArray)?.Count ?? 0} item",
            PlannerTools.ReadThread => $"`{Str(args, "id")}`",
            PlannerTools.RunBuild => Path("target") is { } target ? $"`{target}`" : string.Empty,
            PlannerTools.Investigate => Quote(Str(args, "question")),
            PlannerTools.AskUser => Quote(Str(args, "question")),
            _ => string.Empty
        };
    }

    public static string? Str(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    public static int? Int(JsonObject args, string name)
    {
        if (args[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }

    public static bool Bool(JsonObject args, string name) =>
        args[name] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) && parsed);

    // Keeps the head and the tail: the developer's instruction usually sits at the end of a long message.
    public static string Limit(string text, int max)
    {
        if (text.Length <= max) return text;
        var head = max / 3;
        var tail = max - head;
        return text[..head] + $"\n... ({text.Length - max} chars truncated) ...\n" + text[^tail..];
    }

    private static string Quote(string? text) => text is null ? string.Empty : $"\"{Clip(text, 120)}\"";

    private static string Clip(string text, int max) => text.Length > max ? text[..max] + "…" : text;
}

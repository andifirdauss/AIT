using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Memory;
using AIThread.Service.Memory;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
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
                {
                    var path = Str(args, "path") ?? string.Empty;
                    var (from, to) = (Int(args, "startLine"), Int(args, "endLine"));
                    var content = await tools.ReadFileAsync(path, from, to, cancellationToken);
                    // The functions in the lines just read get their calls read in the background (project files only).
                    if (!content.StartsWith("Error:", StringComparison.Ordinal) && tools.Scope.TryResolve(path, out var full, out _))
                    {
                        var start = Math.Max(1, from ?? 1);
                        functionCalls.Read(input.Project.ProjectId, input.Project.RootPath, tools.Scope.ToRelative(full), start, to ?? start + WorkspaceTools.MaxReadLines - 1);
                    }
                    return new PlannerToolOutcome(content, null);
                }

                case PlannerTools.CodeOutline:
                    return new PlannerToolOutcome(await skeleton.OutlineAsync(input.Project, tools.Scope, Str(args, "path"), Str(args, "name"), cancellationToken), null);

                case PlannerTools.FunctionCalls:
                    return new PlannerToolOutcome(await skeleton.CallsAsync(input.Project, tools.Scope, Str(args, "path"), Str(args, "name"), cancellationToken), null);

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

                case PlannerTools.GitStatus:
                    return new PlannerToolOutcome(await new GitTools(tools.Scope).StatusAsync(Str(args, "path"), cancellationToken), null);

                case PlannerTools.GitDiff:
                    return new PlannerToolOutcome(await new GitTools(tools.Scope).DiffAsync(Str(args, "path"), Str(args, "base"), Bool(args, "staged"), Bool(args, "stat"), cancellationToken), null);

                case PlannerTools.GitLog:
                    return new PlannerToolOutcome(await new GitTools(tools.Scope).LogAsync(Str(args, "path"), Str(args, "revision"), Int(args, "limit"), Str(args, "query"), Str(args, "change"), Bool(args, "files"), cancellationToken), null);

                case PlannerTools.GitShow:
                    return new PlannerToolOutcome(await new GitTools(tools.Scope).ShowAsync(Str(args, "commit") ?? string.Empty, Str(args, "path"), Bool(args, "stat"), cancellationToken), null);

                case PlannerTools.GitBlame:
                    return new PlannerToolOutcome(await new GitTools(tools.Scope).BlameAsync(Str(args, "path") ?? string.Empty, Int(args, "startLine"), Int(args, "endLine"), cancellationToken), null);

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
}

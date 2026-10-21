using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Brief: the planner's first user message. Relevant: memories the label search found for the request.
    private async Task<(string Brief, int Relevant)> BuildBriefAsync(PlannerInput input, WorkspaceTools tools, BuildCommand? build, CancellationToken cancellationToken)
    {
        var request = input.Request;
        var project = input.Project;
        var builder = new StringBuilder();

        // Ordered from the most stable part to the most specific one: providers that cache prompt prefixes reuse the
        // project section and the structure across the threads of a project, and the request itself comes last, after
        // the material it refers to. Each part keeps its head and tail when clipped.
        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        if (project.Stacks.Count > 0) builder.Append($"Stack: {string.Join(", ", project.Stacks)}\n");
        if (project.Modules.Count > 0)
        {
            builder.Append("Manifests found (solution, build and package files):\n");
            foreach (var module in project.Modules.Take(60)) builder.Append("- ").Append(module).Append('\n');
        }
        builder.Append(build is null
            ? "Build: AIT cannot build this project; mention build or test steps in the summary.\n"
            : $"Build: `{build.Display}` (run_build). AIT builds it again after the executors finish{(input.Settings.MaxFixRounds > 0 ? " and has the errors fixed" : string.Empty)}.\n");
        builder.Append("Tool paths are relative to the root. The read tools also accept absolute paths outside the root, for read-only checks; file changes are only possible inside the root.\n\n");

        builder.Append("## Structure (depth 2)\n");
        builder.Append(tools.ListDirectory(".", 2, 200)).Append("\n\n");

        // Differs per session, so it comes after the structure: before it, it would end the shared prefix there.
        if (FocusPath(input.Scope, request.WorkingDirectory) is { } focus)
            builder.Append($"Session opened in: {focus} (relative to the root; the request most likely concerns this part)\n\n");

        // Claude Code sends injected context (CLAUDE.md, reminders, attached files) in the same turn as the request.
        var attached = AttachedContext(request.PromptContext, request.Prompt);
        if (attached is not null)
        {
            builder.Append("# Context sent with the request (reference only; act on the developer request at the end)\n");
            builder.Append(PlannerToolRunner.Limit(attached, MaxAttachedContextChars)).Append("\n\n");
        }

        // With a context pack the planner sees the summary plus only the turns the summary does not cover yet.
        var pack = request.Session?.Pack;
        if (!string.IsNullOrWhiteSpace(pack))
        {
            builder.Append("# Session so far (summary by the context keeper; read_thread gives the full record of a thread)\n");
            builder.Append(pack.Trim()).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationContext))
        {
            builder.Append(string.IsNullOrWhiteSpace(pack) ? "# Earlier conversation (oldest first, truncated)\n" : "# Conversation since that summary (oldest first, truncated)\n");
            builder.Append(request.ConversationContext).Append("\n\n");
        }

        if (input.PreviousTurn is { } previous)
        {
            builder.Append($"# What the planner of the previous turn (thread {ThreadAnchor.ShortId(previous.ThreadId)}) already looked at (files may have changed since)\n");
            builder.Append(previous.Investigation).Append("\n\n");
        }

        if (input.CodeMap is { Count: > 0 } codeMap)
        {
            builder.Append(input.LabelsFromSession is null
                ? "# Code map of the files the request names (module · layer)\n"
                : "# Code map of the files this session worked on (module · layer; the request names none, so it most likely continues that work)\n");
            foreach (var hit in codeMap.Take(30))
                builder.Append($"- {hit.Path}: {(hit.Area is null ? string.Empty : hit.Area + " · ")}{hit.Module} · {hit.Layer}\n");
            builder.Append('\n');
        }

        var outlined = (input.Files ?? []).Concat(input.CodeMap?.Select(h => h.Path) ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
        if (await skeleton.BriefAsync(project, input.Scope, outlined, MaxOutlinedFiles, MaxOutlineChars, cancellationToken) is { } outlines)
        {
            builder.Append(input.LabelsFromSession is null
                ? "# Outline of the files the request names (declarations with startLine-endLine; read only the ranges you need)\n"
                : "# Outline of the files this session worked on (declarations with startLine-endLine; read only the ranges you need)\n");
            builder.Append(outlines).Append('\n');
        }

        var summary = await memoryService.GetModuleSummaryAsync(project.ProjectId, cancellationToken);
        if (summary.Count > 0)
        {
            builder.Append("# Memory map (labeled memories per module: layers | kinds)\n");
            builder.Append(PlannerToolRunner.FormatModuleMap(summary)).Append('\n');
        }

        builder.Append("# Memory index (labels only — call read_memory with ids to read the details)\n");
        var focusModules = input.CodeMap?.Select(h => h.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // A follow-up's own words rarely match a keyword ("berlaku buat yang lainnya"): the prompt it follows joins them.
        var searchText = input.LabelsFromSession?.Prompt is { Length: > 0 } earlier
            ? request.Prompt + "\n" + PlannerToolRunner.Limit(earlier, MaxSessionPromptChars)
            : request.Prompt;
        var relevant = await memoryService.SearchAsync(new MemorySearch(project.ProjectId, Text: searchText, Take: 20, Modules: focusModules), cancellationToken);
        var recent = await memoryService.GetIndexAsync(project.ProjectId, null, 10, cancellationToken);
        var index = relevant.Concat(recent.Where(r => r.Level is MemoryLevel.Short or MemoryLevel.Business)).DistinctBy(e => e.Id).Take(25).ToList();
        builder.Append(index.Count == 0 ? "(empty)\n" : PlannerToolRunner.FormatIndex(index)).Append('\n');

        builder.Append("# Developer request\n");
        builder.Append(PlannerToolRunner.Limit(request.Prompt, MaxRequestChars)).Append('\n');

        return (builder.ToString(), relevant.Count);
    }
}

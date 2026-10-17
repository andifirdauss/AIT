using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Service.Implementations;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.CodeCalls;

// Reads what functions call, at the smallest scale: only the functions whose lines the planner read or a thread
// changed. The tools node lists the calls of a batch of functions; every call it names must be written on (or next
// to) the line it gives, or it is dropped. Where each call is declared is resolved by AIT against the code structure
// (CallResolver), never by the model. A function whose body did not change since its last scan is skipped.
public sealed partial class FunctionCallScanner(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    CodeStructureService structure,
    FunctionCallStore store,
    IProjectService projectService,
    INodeService nodeService,
    NodeCaller caller,
    ILogger<FunctionCallScanner> logger)
{
    private const int MaxBatchChars = 16_000;
    private const int MaxBatchFunctions = 20;
    private const int MaxBodyChars = 8_000;
    private const int MaxFunctionsPerFile = 60;
    private const int MaxTokens = 4_000;
    private const int MaxTargetLength = 500;
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);

    public async Task ScanAsync(FunctionScanRequest request, CancellationToken cancellationToken)
    {
        if (request.ThreadId is { } threadId)
        {
            if (await projectService.GetContextAsync(request.ProjectId, cancellationToken) is not { } project) return;
            foreach (var (path, from, to) in await ThreadChangesAsync(project.RootPath, threadId, cancellationToken))
                await ScanFileAsync(request.ProjectId, project.RootPath, path, [(from, to)], cancellationToken);
            return;
        }
        if (request.Path is not null) await ScanFileAsync(request.ProjectId, request.Root, request.Path, [(request.From, request.To)], cancellationToken);
    }

    // The files a thread changed (still present) and the block of lines that differs from before the thread.
    private async Task<List<(string Path, int From, int To)>> ThreadChangesAsync(string root, Guid threadId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => s.ThreadRunId == threadId && !s.Removed)
            .Select(s => new { s.FilePath, s.Existed, s.OriginalContent })
            .ToListAsync(cancellationToken);

        var changes = new List<(string, int, int)>();
        foreach (var snapshot in snapshots)
        {
            var full = Path.Combine(root, snapshot.FilePath);
            if (OutlineLanguages.Of(full) is null || !File.Exists(full)) continue;
            var after = WorkspaceTools.SplitLines(await File.ReadAllTextAsync(full, cancellationToken));
            var before = snapshot.Existed && snapshot.OriginalContent is { } original ? WorkspaceTools.SplitLines(Encoding.UTF8.GetString(original)) : [];
            if (FunctionSpans.ChangedRange(before, after) is { } range) changes.Add((snapshot.FilePath.Replace('\\', '/'), range.From, range.To));
        }
        return changes;
    }

    public async Task ScanFileAsync(Guid projectId, string root, string path, IReadOnlyList<(int From, int To)> ranges, CancellationToken cancellationToken)
    {
        var full = Path.Combine(root, path);
        var language = OutlineLanguages.Of(path);
        if (language is null || !File.Exists(full) || new FileInfo(full).Length > CodeStructureScanner.MaxParseBytes) return;

        var snapshot = await structure.GetSnapshotAsync(projectId, cancellationToken);
        if (snapshot.Files.GetValueOrDefault(path) is { ThirdParty: true }) return;

        var content = await File.ReadAllTextAsync(full, cancellationToken);
        var lines = WorkspaceTools.SplitLines(content).Select(l => l.TrimEnd('\r')).ToArray();
        var outline = OutlineLanguages.Outline(language, content);
        var spans = FunctionSpans.Of(outline)
            .Where(s => ranges.Any(r => s.Overlaps(r.From, r.To)))
            .Take(MaxFunctionsPerFile)
            .Select(s => new Pending(s, FunctionSpans.Key(path, s.Symbol, s.Signature), Body(lines, s)))
            .ToList();
        if (spans.Count == 0) return;

        var states = await store.StatesAsync(projectId, spans.Select(s => s.Key).ToList(), cancellationToken);
        var todo = spans.Where(s => !states.TryGetValue(s.Key, out var state) || state.BodyHash != s.Hash
                                    || (state.Status == FunctionScanStatus.Failed && DateTime.UtcNow - state.UpdatedAt > RetryFailedAfter))
            .ToList();
        if (todo.Count == 0) return;

        var node = (await nodeService.GetActiveAsync(NodeRole.ToolsNode, cancellationToken)).FirstOrDefault();
        if (node is null)
        {
            logger.LogDebug("No active tools node; the calls of {Count} function(s) in {Path} are not read", todo.Count, path);
            return;
        }

        foreach (var batch in Batches(todo))
        {
            var scans = await AskAsync(node, path, language, lines, outline, snapshot.Symbols, batch, cancellationToken);
            await store.SaveAsync(projectId, scans, cancellationToken);
        }
    }

    private async Task<List<CodeFunctionScan>> AskAsync(AiNode node, string path, string language, string[] lines, FileOutline outline,
        StructureSymbolIndex symbols, List<Pending> batch, CancellationToken cancellationToken)
    {
        string? model = node.Model;
        var tokens = 0;
        JsonNode? json;
        try
        {
            var reply = await caller.ChatAsync(node, candidate => Task.FromResult(new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages = [ChatMessages.System(AgentPrompts.FunctionCalls), ChatMessages.User(Input(path, language, lines, batch))],
                Temperature = 0.1,
                MaxTokens = Math.Min(candidate.MaxTokens, MaxTokens)
            }), cancellationToken);
            model = reply.Node.Model;
            tokens = reply.Result.TotalTokens;
            json = SkillAi.ParseObject(reply.Result.Content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tools node could not read the calls of {Count} function(s) in {Path}", batch.Count, path);
            return batch.Select(p => Scan(path, p, FunctionScanStatus.Failed, model, 0, Truncate(ex.Message, 1000), [])).ToList();
        }

        var answers = (json?["functions"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(f => SkillAi.Text(f["id"]) is not null)
            .GroupBy(f => SkillAi.Text(f["id"])!)
            .ToDictionary(g => g.Key, g => g.First());
        var share = batch.Count == 0 ? 0 : tokens / batch.Count;
        var scans = new List<CodeFunctionScan>();
        for (var i = 0; i < batch.Count; i++)
        {
            var pending = batch[i];
            if (!answers.TryGetValue($"F{i + 1}", out var answer))
            {
                scans.Add(Scan(path, pending, FunctionScanStatus.Failed, model, share, "The tools node did not answer for this function.", []));
                continue;
            }
            var calls = Calls(answer, pending.Span, path, lines, outline, symbols);
            scans.Add(Scan(path, pending, FunctionScanStatus.Done, model, share, null, calls));
        }
        return scans;
    }

    // The calls the model gave for one function that are really written there, resolved to their declarations.
    public static List<CodeFunctionCall> Calls(JsonObject answer, FunctionSpan span, string path, string[] lines, FileOutline outline, StructureSymbolIndex symbols)
    {
        var calls = new List<CodeFunctionCall>();
        var seen = new HashSet<(CodeCallKind, string)>();
        var body = string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));
        foreach (var item in (answer["calls"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var target = SkillAi.Text(item["target"])?.Trim();
            if (string.IsNullOrEmpty(target) || target.Length > MaxTargetLength) continue;
            var kind = KindOf(SkillAi.Text(item["kind"]));
            var line = LineOf(item["line"]);
            if (line < span.Line || line > span.EndLine || !Written(kind, target, line, lines, body)) continue;
            if (!seen.Add((kind, target))) continue;

            var resolved = CallResolver.Resolve(kind, target, path, outline, symbols);
            calls.Add(new CodeFunctionCall
            {
                Kind = kind,
                Target = target,
                Line = line,
                ResolvedPath = resolved?.Path,
                ResolvedSymbol = resolved?.Symbol,
                ResolvedLine = resolved?.Line
            });
        }
        return calls;
    }

    // Evidence: a URL's literal path parts appear in the body; any other call's own name is on its line or a line next
    // to it (the model may count a wrapped call from its first line).
    private static bool Written(CodeCallKind kind, string target, int line, string[] lines, string body)
    {
        if (kind == CodeCallKind.Http)
        {
            var literal = HttpPrefix().Replace(target, string.Empty);
            var parts = literal.Split(['/', '?', '{', '}'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 1).ToList();
            return parts.Count > 0 && parts.All(p => body.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
        var name = LastName().Match(target.Split('(')[0]) is { Success: true } m ? m.Value : target;
        for (var i = Math.Max(1, line - 1); i <= Math.Min(lines.Length, line + 1); i++)
            if (lines[i - 1].Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // A number, or a number written as text ("26").
    private static int LineOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number
        : int.TryParse(SkillAi.Text(node), out var parsed) ? parsed
        : 0;

    private static CodeCallKind KindOf(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "function" => CodeCallKind.Function,
        "method" => CodeCallKind.Method,
        "http" => CodeCallKind.Http,
        "view" => CodeCallKind.View,
        "sql" => CodeCallKind.Sql,
        _ => CodeCallKind.Other
    };

    private static string Input(string path, string language, string[] lines, List<Pending> batch)
    {
        var builder = new StringBuilder($"# File\n{path} ({language})\n");
        for (var i = 0; i < batch.Count; i++)
        {
            var span = batch[i].Span;
            builder.Append($"\n## F{i + 1} {span.Name} (lines {span.Line}-{span.EndLine})\n");
            var written = 0;
            for (var n = span.Line; n <= span.EndLine && n <= lines.Length; n++)
            {
                var text = $"{n}| {lines[n - 1]}\n";
                if (written + text.Length > MaxBodyChars)
                {
                    builder.Append($"… (lines {n}-{span.EndLine} left out: function too long)\n");
                    break;
                }
                builder.Append(text);
                written += text.Length;
            }
        }
        return builder.ToString();
    }

    private static IEnumerable<List<Pending>> Batches(List<Pending> todo)
    {
        var batch = new List<Pending>();
        var size = 0;
        foreach (var pending in todo)
        {
            var length = Math.Min(pending.Body.Length, MaxBodyChars);
            if (batch.Count > 0 && (size + length > MaxBatchChars || batch.Count == MaxBatchFunctions))
            {
                yield return batch;
                batch = [];
                size = 0;
            }
            batch.Add(pending);
            size += length;
        }
        if (batch.Count > 0) yield return batch;
    }

    private static CodeFunctionScan Scan(string path, Pending pending, FunctionScanStatus status, string? model, int tokens, string? error, List<CodeFunctionCall> calls) => new()
    {
        Key = pending.Key,
        Path = path,
        Symbol = Truncate(pending.Span.Symbol, 300),
        Signature = pending.Span.Signature is { } signature ? Truncate(signature, 400) : null,
        StartLine = pending.Span.Line,
        EndLine = pending.Span.EndLine,
        BodyHash = pending.Hash,
        Status = status,
        Error = error,
        Model = model is null ? null : Truncate(model, 200),
        Tokens = tokens,
        Calls = calls
    };

    private static string Body(string[] lines, FunctionSpan span) =>
        string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private sealed record Pending(FunctionSpan Span, string Key, string Body)
    {
        public string Hash { get; } = FunctionSpans.Hash(Body);
    }

    [GeneratedRegex(@"^\s*(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex HttpPrefix();

    [GeneratedRegex(@"[A-Za-z_$][\w$]*(?=\W*$)")]
    private static partial Regex LastName();
}

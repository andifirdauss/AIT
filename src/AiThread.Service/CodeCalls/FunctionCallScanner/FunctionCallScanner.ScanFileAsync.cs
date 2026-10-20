using AiThread.Common.Enums;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
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
}

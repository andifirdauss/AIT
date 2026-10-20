using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // Whether the function reads differently now: gone, or another body. Unknown (the file cannot be read) counts as not.
    private async Task<bool> ChangedAsync(Guid projectId, string path, string key, string hash, CancellationToken cancellationToken)
    {
        try
        {
            var project = await projectService.GetContextAsync(projectId, cancellationToken);
            var full = project is null ? null : Path.Combine(project.RootPath, path);
            if (full is null || OutlineLanguages.Of(path) is not { } language || !File.Exists(full)) return full is not null;
            var content = await File.ReadAllTextAsync(full, cancellationToken);
            var lines = WorkspaceTools.SplitLines(content).Select(l => l.TrimEnd('\r')).ToArray();
            var span = FunctionSpans.Of(OutlineLanguages.Outline(language, content)).FirstOrDefault(s => FunctionSpans.Key(path, s.Symbol, s.Signature) == key);
            return span is null || FunctionSpans.Hash(string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1))) != hash;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

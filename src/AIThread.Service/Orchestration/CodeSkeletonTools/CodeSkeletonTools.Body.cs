using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static string Body(string[] lines, FunctionSpan span) =>
        string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));
}

using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static string Body(string[] lines, FunctionSpan span) =>
        string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));
}

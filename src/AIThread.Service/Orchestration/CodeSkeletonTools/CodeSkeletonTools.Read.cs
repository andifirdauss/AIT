using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static OutlinedFile? Read(string full)
    {
        var language = OutlineLanguages.Of(full);
        if (language is null || new FileInfo(full).Length > CodeStructureScanner.MaxParseBytes) return null;
        // The same reading as the call scanner, so body hashes compare.
        var content = File.ReadAllText(full);
        return new OutlinedFile(language, WorkspaceTools.SplitLines(content).Select(l => l.TrimEnd('\r')).ToArray(), OutlineLanguages.Outline(language, content));
    }
}

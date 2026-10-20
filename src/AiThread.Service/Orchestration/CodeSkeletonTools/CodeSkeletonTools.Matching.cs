using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // "Save", "UserService.Save" or "Outer.Inner.Save"; exact case first.
    private static List<FunctionSpan> Matching(IReadOnlyList<FunctionSpan> spans, string name)
    {
        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            var found = spans.Where(s => s.Symbol.Equals(name, comparison) || s.Name.Equals(name, comparison) || s.Symbol.EndsWith("." + name, comparison)).ToList();
            if (found.Count > 0) return found;
        }
        return [];
    }
}

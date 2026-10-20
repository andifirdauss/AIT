using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static bool TypeMatches(OutlineType type, string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0) return type.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        var qualifier = name[..dot];
        return type.Name.Equals(name[(dot + 1)..], StringComparison.OrdinalIgnoreCase)
               && (string.Equals(type.Container, qualifier, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(type.Namespace, qualifier, StringComparison.OrdinalIgnoreCase));
    }
}

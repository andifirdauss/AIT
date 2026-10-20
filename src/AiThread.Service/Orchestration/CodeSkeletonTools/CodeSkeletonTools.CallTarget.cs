namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // Calls are resolved to "Type.Member" (without the outer type of a nested one) or a bare function name.
    private static string CallTarget(string symbol)
    {
        var parts = symbol.Split('.');
        return parts.Length <= 2 ? symbol : $"{parts[^2]}.{parts[^1]}";
    }
}

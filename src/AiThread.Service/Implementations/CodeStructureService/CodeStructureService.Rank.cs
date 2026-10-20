namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private static int? Rank(string name, string text) =>
        name.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
        : name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 2
        : null;
}

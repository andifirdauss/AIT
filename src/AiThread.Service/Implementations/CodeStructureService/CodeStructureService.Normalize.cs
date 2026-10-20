namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private static string Normalize(string? path) => (path ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
}

namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    private static bool IsIndex(string path) => string.Equals(Path.GetFileName(path), IndexFileName, StringComparison.OrdinalIgnoreCase);
}

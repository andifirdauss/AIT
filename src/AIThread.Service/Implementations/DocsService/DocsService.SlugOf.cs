namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    private static string SlugOf(string fileName) =>
        string.Equals(fileName, IndexFileName, StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.GetFileNameWithoutExtension(fileName);
}

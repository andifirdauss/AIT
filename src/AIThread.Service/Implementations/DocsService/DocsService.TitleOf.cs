namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    // The first "# " heading, falling back to the file name.
    private static string TitleOf(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}

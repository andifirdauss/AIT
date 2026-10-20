namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    // A binary that cannot be opened for writing: a running program (the app itself, IIS without shadow copies, the
    // compiler server of bin\roslyn) has it loaded. Opening without writing changes nothing.
    public static string? FirstInUse(IEnumerable<string> directories)
    {
        var checkedFiles = 0;
        foreach (var directory in directories.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = InUseDepth });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files.Where(f => Path.GetExtension(f) is { } e && (e.Equals(".dll", StringComparison.OrdinalIgnoreCase) || e.Equals(".exe", StringComparison.OrdinalIgnoreCase))))
            {
                if (++checkedFiles > MaxInUseChecks) return null;
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (UnauthorizedAccessException)
                {
                    // Read-only, not in use.
                }
                catch (IOException)
                {
                    return file;
                }
            }
        }
        return null;
    }
}

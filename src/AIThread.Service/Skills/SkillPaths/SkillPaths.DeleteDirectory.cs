namespace AIThread.Service.Skills;

public sealed partial class SkillPaths
{
    // Git checkouts mark their object files read-only, which Directory.Delete refuses.
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(path, true);
    }
}

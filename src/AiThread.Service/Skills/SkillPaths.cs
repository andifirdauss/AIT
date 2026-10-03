using AiThread.Common.Options;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Skills;

// Where pulled skills live offline: <root>\<source id>\..., plus a .staging folder for pulls in progress.
public sealed class SkillPaths(IOptions<AiThreadOptions> options)
{
    public string Root { get; } = string.IsNullOrWhiteSpace(options.Value.SkillsDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread", "skills")
        : Path.GetFullPath(options.Value.SkillsDirectory);

    public string StagingRoot => Path.Combine(Root, ".staging");

    public string SourceDir(Guid sourceId) => SourceDir(Root, sourceId);

    public string SkillDir(Guid sourceId, string relativeDir) => SkillDir(Root, sourceId, relativeDir);

    public static string SourceDir(string root, Guid sourceId) => Path.Combine(root, sourceId.ToString("N"));

    // relativeDir uses forward slashes; "." is the source folder itself.
    public static string SkillDir(string root, Guid sourceId, string relativeDir)
    {
        var source = SourceDir(root, sourceId);
        return relativeDir is "." or "" ? source : Path.GetFullPath(Path.Combine(source, relativeDir.Replace('/', Path.DirectorySeparatorChar)));
    }

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

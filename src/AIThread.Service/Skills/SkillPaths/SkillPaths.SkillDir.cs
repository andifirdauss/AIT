namespace AIThread.Service.Skills;

public sealed partial class SkillPaths
{
    public string SkillDir(Guid sourceId, string relativeDir) => SkillDir(Root, sourceId, relativeDir);

    // relativeDir uses forward slashes; "." is the source folder itself.
    public static string SkillDir(string root, Guid sourceId, string relativeDir)
    {
        var source = SourceDir(root, sourceId);
        return relativeDir is "." or "" ? source : Path.GetFullPath(Path.Combine(source, relativeDir.Replace('/', Path.DirectorySeparatorChar)));
    }
}

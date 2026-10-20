namespace AIThread.Service.Skills;

public sealed partial class SkillPaths
{
    public string SourceDir(Guid sourceId) => SourceDir(Root, sourceId);

    public static string SourceDir(string root, Guid sourceId) => Path.Combine(root, sourceId.ToString("N"));
}

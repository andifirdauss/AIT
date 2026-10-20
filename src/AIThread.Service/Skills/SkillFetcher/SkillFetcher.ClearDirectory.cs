namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private static void ClearDirectory(string path)
    {
        SkillPaths.DeleteDirectory(path);
        Directory.CreateDirectory(path);
    }
}

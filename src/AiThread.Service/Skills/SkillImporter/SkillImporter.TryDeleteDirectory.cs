namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            SkillPaths.DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open by an editor or a virus scanner; the next pull cleans it up.
        }
    }
}

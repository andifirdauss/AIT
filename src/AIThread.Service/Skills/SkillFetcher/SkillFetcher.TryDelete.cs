namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    // The staging folder lives next to the store (same volume), so the swap is two renames.
    private static void ReplaceFolder(string staging, string target)
    {
        Directory.CreateDirectory(staging);
        var old = target + ".old";
        TryDeleteDirectory(old);
        if (Directory.Exists(target)) Directory.Move(target, old);
        Directory.Move(staging, target);
        TryDeleteDirectory(old);
    }
}

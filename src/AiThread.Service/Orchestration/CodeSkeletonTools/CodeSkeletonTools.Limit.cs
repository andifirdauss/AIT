namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static string Limit(string text) =>
        text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "\n... (truncated: outline a smaller part, or give name)\n";
}

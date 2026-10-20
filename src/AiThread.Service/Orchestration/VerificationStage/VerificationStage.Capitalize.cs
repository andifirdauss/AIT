namespace AiThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

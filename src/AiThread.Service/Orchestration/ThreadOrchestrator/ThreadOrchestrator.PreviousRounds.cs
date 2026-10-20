using System.Text.Json;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Verification rounds already stored on the thread; a retry numbers its rounds after them.
    private static int PreviousRounds(string? verificationJson)
    {
        if (string.IsNullOrWhiteSpace(verificationJson)) return 0;
        try
        {
            return JsonSerializer.Deserialize<VerificationReport>(verificationJson, ThreadPlan.JsonOptions)?.Rounds.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}

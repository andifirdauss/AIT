namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // The plan summary says what was done, not why. A false match only costs one wrapper call.
    private static bool AsksForExplanation(string prompt) => ExplanationPattern().IsMatch(prompt);
}

namespace AIThread.Service.Implementations;

public sealed partial class NodeBenchmarkService
{
    // The id at the start keeps the provider's prompt cache out of the measurement: every sample is read in full.
    private static string Probe(int contextTokens) =>
        $"Request {Guid.NewGuid():N}. In at most 30 words, say what this code does.\n\n"
        + string.Concat(Enumerable.Repeat(SampleCode, contextTokens * CharsPerToken / SampleCode.Length + 1));
}

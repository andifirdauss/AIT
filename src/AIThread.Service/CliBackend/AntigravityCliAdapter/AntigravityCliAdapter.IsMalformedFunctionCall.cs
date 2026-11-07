namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // agy's error when the model emitted a native function call it could not parse (the agent has no tools): the same
    // prompt usually goes through on the next try.
    public static bool IsMalformedFunctionCall(string? detail) =>
        detail is not null
        && (detail.Contains("improperly formatted function call", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("malformed function call", StringComparison.OrdinalIgnoreCase));
}

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // What a turn's record says about testing: the tester's check of the app and the unit tests, when they ran.
    private static string? Testing(string? app, string? unitTests)
    {
        var parts = new[] { app, unitTests is null ? null : $"unit tests: {unitTests}" }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}

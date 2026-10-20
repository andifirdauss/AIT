namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // "Langkah planner 24 → 30" when the value changes.
    private static void Diff<T>(List<string> diffs, string label, T current, T target)
    {
        if (!EqualityComparer<T>.Default.Equals(current, target)) diffs.Add($"{label} {current} → {target}");
    }
}

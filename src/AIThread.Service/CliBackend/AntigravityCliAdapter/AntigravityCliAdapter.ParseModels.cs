namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // `agy models` prints one "<id>\t<label>" line per model; a line without a tab (a progress note) is skipped.
    public static IReadOnlyList<string> ParseModels(IEnumerable<string> lines) =>
        lines
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Length > 0 && !parts[0].Trim().Contains(' '))
            .Select(parts => parts[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

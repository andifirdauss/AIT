namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // BusinessPageRevision.SourceThreadIds (comma-separated); ids that do not parse are left out.
    private static IReadOnlyList<Guid> ThreadIds(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .OfType<Guid>()
            .ToList();
}

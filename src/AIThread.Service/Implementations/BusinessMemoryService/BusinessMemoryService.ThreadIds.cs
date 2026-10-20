namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static IReadOnlyList<Guid> ThreadIds(string? csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',').Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
}

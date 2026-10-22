using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<TestingScenario?> FindAsync(string projectKey, string slugOrId, CancellationToken cancellationToken = default)
    {
        var text = slugOrId.Trim().Trim('`', '"', '\'');
        if (text.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) text = Path.GetFileNameWithoutExtension(text);
        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        var scenarios = await db.TestingScenarios.AsNoTracking().Where(s => s.ProjectKey == projectKey).ToListAsync(cancellationToken);
        return scenarios.FirstOrDefault(s => string.Equals(s.Slug, text, StringComparison.OrdinalIgnoreCase))
               ?? (Guid.TryParse(text, out var id) ? scenarios.FirstOrDefault(s => s.Id == id) : null);
    }
}

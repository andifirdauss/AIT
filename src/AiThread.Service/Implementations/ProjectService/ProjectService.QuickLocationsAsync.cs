using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    // Drives, the user's home and source\repos, and the folders around recently used projects.
    private async Task<IReadOnlyList<FolderEntryDto>> QuickLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<FolderEntryDto>(Drives());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home)) locations.Add(new FolderEntryDto("Home", home, false, []));
        var repos = Path.Combine(home, "source", "repos");
        if (Directory.Exists(repos)) locations.Add(new FolderEntryDto("source\\repos", repos, false, []));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var recentRoots = await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.LastSeenAt)
            .Select(p => p.RootPath)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var parent in recentRoots
                     .Select(Path.GetDirectoryName)
                     .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(6))
        {
            if (locations.Any(l => string.Equals(l.Path, parent, StringComparison.OrdinalIgnoreCase))) continue;
            locations.Add(new FolderEntryDto(Path.GetFileName(parent!) is { Length: > 0 } name ? name : parent!, parent!, false, []));
        }

        return locations;
    }
}

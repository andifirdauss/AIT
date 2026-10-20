using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths is not { Count: > 0 }) throw new AppValidationException("Pilih minimal satu project.");

        var registered = await RegisteredRootsAsync(cancellationToken);
        int added = 0, existing = 0;
        var errors = new List<string>();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ProjectRoot root;
            try
            {
                root = RootForRegistration(path);
            }
            catch (AppValidationException ex)
            {
                errors.Add($"{path}: {ex.Message}");
                continue;
            }

            if (registered.ContainsKey(root.Path))
            {
                existing++;
                continue;
            }

            var project = await UpsertAsync(root.Path, root.Name, cancellationToken);
            registered[root.Path] = project.Id;
            added++;
        }

        return new RegisterProjectsResultDto(added, existing, errors);
    }
}

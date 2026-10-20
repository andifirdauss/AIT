using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new AppValidationException("Path wajib diisi.");

        var root = RootForRegistration(path);
        var project = await UpsertAsync(root.Path, root.Name, cancellationToken);
        return (await GetAllAsync(cancellationToken)).First(p => p.Id == project.Id);
    }
}

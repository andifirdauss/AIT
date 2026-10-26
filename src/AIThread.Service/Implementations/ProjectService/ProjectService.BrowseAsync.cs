using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default)
    {
        var quickLocations = await QuickLocationsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(path)) return new FolderBrowseDto(null, null, Drives(), [], quickLocations);

        // A file path (e.g. a solution) opens its folder.
        var directory = FolderOf(path) ?? throw new AppValidationException(Lang.T($"Folder '{path}' tidak ditemukan.", $"Folder '{path}' not found."));

        var info = new DirectoryInfo(directory);
        var children = ProjectLocator.BrowsableDirectories(info).Take(MaxBrowseFolders).ToList();
        var markProjects = children.Count <= MaxFoldersForProjectHint;
        var folders = children
            .Select(child => markProjects && ProjectLocator.Inspect(child.FullName) is { } root
                ? new FolderEntryDto(child.Name, child.FullName, true, root.Stacks)
                : new FolderEntryDto(child.Name, child.FullName, false, []))
            .ToList();

        var registered = await RegisteredRootsAsync(cancellationToken);
        var projects = new List<ProjectCandidateDto>();
        if (!ProjectLocator.IsBlockedRoot(info.FullName) && ProjectLocator.Inspect(info.FullName) is { } own)
            projects.Add(ToCandidateDto(own, registered));
        return new FolderBrowseDto(info.FullName, info.Parent?.FullName, folders, projects, quickLocations);
    }
}

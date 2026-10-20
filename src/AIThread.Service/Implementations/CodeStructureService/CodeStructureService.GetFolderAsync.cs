using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<StructureFolderDto> GetFolderAsync(Guid projectId, string? path, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var folder = Normalize(path);
        var prefix = folder.Length == 0 ? string.Empty : folder + "/";
        var folders = new Dictionary<string, (int Files, int Types, int ThirdParty)>(StringComparer.OrdinalIgnoreCase);
        var files = new List<StructureFile>();
        foreach (var file in snapshot.Files.Values)
        {
            if (!file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = file.Path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                files.Add(file);
                continue;
            }
            var name = rest[..slash];
            var (count, types, thirdParty) = folders.GetValueOrDefault(name);
            folders[name] = (count + 1, types + (file.Outline?.Types.Count ?? 0), thirdParty + (file.ThirdParty ? 1 : 0));
        }
        if (folder.Length > 0 && folders.Count == 0 && files.Count == 0) throw new AppValidationException($"Folder '{folder}' tidak ada di hasil scan.");

        var folderRows = folders
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => new StructureFolderEntryDto(f.Key, prefix + f.Key, f.Value.Files, f.Value.Types, f.Value.ThirdParty == f.Value.Files))
            .ToList();
        var fileRows = files
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFolderFiles)
            .Select(f => new StructureFileEntryDto(Path.GetFileName(f.Path), f.Path, f.Language, f.Outline?.Types.Count ?? 0, f.Outline?.MemberCount ?? 0,
                f.ThirdParty, f.Outline is not null))
            .ToList();
        return new StructureFolderDto(folder, folderRows, fileRows, Math.Max(0, files.Count - MaxFolderFiles));
    }
}

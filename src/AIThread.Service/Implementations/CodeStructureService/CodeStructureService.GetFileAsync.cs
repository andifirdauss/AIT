using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<StructureFileDto> GetFileAsync(Guid projectId, string path, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var file = snapshot.Files.GetValueOrDefault(Normalize(path)) ?? throw new AppValidationException($"File '{path}' tidak ada di hasil scan.");
        var outline = file.Outline ?? FileOutline.Empty;
        var types = outline.Types.Select(t => (Type: t, Members: t.Members.Select(m => WithKey(ToDto(m), m, file.Path, t.Name, t.Container)).ToList())).ToList();
        var functions = outline.Functions.Select(f => WithKey(ToDto(f), f, file.Path, null, null)).ToList();
        var counted = await WithCallsAsync(projectId, types.SelectMany(t => t.Members).Concat(functions).ToList(), cancellationToken);
        return new StructureFileDto(file.Path, file.Language, file.ThirdParty, file.Outline is not null,
            types.Select(t => new StructureTypeDto(t.Type.Name, t.Type.Kind, t.Type.Namespace, t.Type.Container, t.Type.Line, t.Type.IsPartial,
                t.Members.Select(m => counted[m]).ToList())).ToList(),
            functions.Select(f => counted[f]).ToList());
    }
}

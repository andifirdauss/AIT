using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<StructureClassDto> GetClassAsync(Guid projectId, string group, string name, string? container, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(projectId, false, cancellationToken);
        var type = snapshot.Classes.Find(group, name, string.IsNullOrEmpty(container) ? null : container)
                   ?? throw new AppValidationException($"Class '{name}' tidak ada di hasil scan.");
        var severalFiles = type.Locations.Count > 1;
        var members = type.Members.Select(m => WithKey(ToDto(m.Member) with { Path = severalFiles ? m.Path : null }, m.Member, m.Path, type.Name, type.Container)).ToList();
        var counted = await WithCallsAsync(projectId, members, cancellationToken);
        return new StructureClassDto(type.Group, type.Name, type.Container, type.Kind, type.IsPartial,
            type.Locations.Select(l => new StructureLocationDto(l.Path, l.Line)).ToList(),
            members.Select(m => counted[m]).ToList());
    }
}

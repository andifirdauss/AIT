using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    public async Task<StructureFunctionCallsDto?> GetCallsAsync(Guid projectId, string key, CancellationToken cancellationToken = default)
    {
        var scan = await calls.GetAsync(projectId, key, cancellationToken);
        if (scan is null) return null;
        return new StructureFunctionCallsDto(scan.Key, scan.Path, scan.Symbol, scan.StartLine, scan.EndLine, scan.Status.ToString(), scan.Error, scan.Model,
            scan.UpdatedAt ?? scan.CreatedAt, await ChangedAsync(projectId, scan.Path, scan.Key, scan.BodyHash, cancellationToken),
            scan.Calls.Select(c => new StructureCallDto(c.Kind.ToString(), c.Target, c.Line, c.ResolvedPath, c.ResolvedSymbol, c.ResolvedLine)).ToList());
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    // The same members with Calls filled in for the functions the tools node has read.
    private async Task<Dictionary<StructureMemberDto, StructureMemberDto>> WithCallsAsync(Guid projectId, List<StructureMemberDto> members, CancellationToken cancellationToken)
    {
        var states = await calls.StatesAsync(projectId, members.Where(m => m.Key is not null).Select(m => m.Key!).ToList(), cancellationToken);
        var result = new Dictionary<StructureMemberDto, StructureMemberDto>(ReferenceEqualityComparer.Instance);
        foreach (var member in members)
            result[member] = member.Key is { } key && states.TryGetValue(key, out var state) && state.Status == FunctionScanStatus.Done
                ? member with { Calls = state.Calls }
                : member;
        return result;
    }
}

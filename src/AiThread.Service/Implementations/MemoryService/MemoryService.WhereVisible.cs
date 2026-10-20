using AiThread.Common.Entities;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Memory of the scope's project key (written by any developer) plus, for rows written before keys existed, the
    // memory of the project id itself.
    private static IQueryable<LongMemory> WhereVisible(IQueryable<LongMemory> query, ProjectScope? scope, bool includeKnowledge)
    {
        if (scope is null) return includeKnowledge ? query.Where(m => m.Type == MemoryType.Knowledge) : query.Where(_ => false);

        var key = scope.Key;
        var id = scope.Id;
        return includeKnowledge
            ? query.Where(m => m.Type == MemoryType.Knowledge || (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id))
            : query.Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id));
    }
}

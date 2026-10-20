using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private static CodeMapEntryDto ToDto(CodeMapEntry e) => new(e.Id, e.Path, e.Module, e.Layer, e.Area, e.Source, e.UpdatedAt ?? e.CreatedAt);
}

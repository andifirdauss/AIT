using AiThread.Common.Entities;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    private static CodeMapHit ToHit(CodeMapEntry e) => new(e.Path, e.Module, e.Layer, e.Area, e.Source);
}

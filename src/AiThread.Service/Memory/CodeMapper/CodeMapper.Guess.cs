using AiThread.Common.Entities;
using AiThread.Common.Enums;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Sure when the layer is known and the name stem is an existing module (or the file is plumbing that belongs to Core).
    private static CodeMapHit? Guess(string path, IReadOnlyList<ProjectModule> modules, IReadOnlyList<string> areas)
    {
        var layer = CodeMapHeuristics.Layer(path);
        if (layer is null) return null;
        var stem = CodeMapHeuristics.ModuleStem(path);
        var module = stem is null ? null : ModuleRegistry.Resolve(modules, stem);
        if (module is null && layer is MemoryLayer.Infra) module = ModuleRegistry.Resolve(modules, ModuleRegistry.CoreModule) ?? ModuleRegistry.CoreModule;
        return module is null ? null : new CodeMapHit(path, module, layer.Value, CodeMapHeuristics.AreaOf(path, areas), CodeMapSource.Heuristic);
    }
}

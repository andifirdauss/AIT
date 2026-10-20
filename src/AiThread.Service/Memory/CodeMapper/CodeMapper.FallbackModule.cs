namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    private static string FallbackModule(FileGroup group) =>
        group.Stem.StartsWith('(') ? ModuleRegistry.CoreModule : group.Stem;
}

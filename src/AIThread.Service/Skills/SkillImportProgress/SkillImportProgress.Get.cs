namespace AIThread.Service.Skills;

public sealed partial class SkillImportProgress
{
    public string? Get(Guid sourceId) => _progress.TryGetValue(sourceId, out var text) ? text : null;
}

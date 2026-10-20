namespace AIThread.Service.Skills;

public sealed partial class SkillImportProgress
{
    public void Clear(Guid sourceId) => _progress.TryRemove(sourceId, out _);
}

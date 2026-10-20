namespace AiThread.Service.Skills;

public sealed partial class SkillImportProgress
{
    public void Set(Guid sourceId, string text) => _progress[sourceId] = text;
}

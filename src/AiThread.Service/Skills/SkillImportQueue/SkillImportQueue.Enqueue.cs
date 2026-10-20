namespace AiThread.Service.Skills;

public sealed partial class SkillImportQueue
{
    public void Enqueue(Guid sourceId) => _channel.Writer.TryWrite(sourceId);
}

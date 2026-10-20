namespace AiThread.Service.Interfaces;

public interface ISkillImporter
{
    Task ImportAsync(Guid sourceId, CancellationToken cancellationToken);
}

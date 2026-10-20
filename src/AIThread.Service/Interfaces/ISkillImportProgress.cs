namespace AIThread.Service.Interfaces;

public interface ISkillImportProgress
{
    void Set(Guid sourceId, string text);

    void Clear(Guid sourceId);

    string? Get(Guid sourceId);
}

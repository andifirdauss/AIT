namespace AiThread.Service.Interfaces;

public interface ISkillPaths
{
    string Root { get; }

    string StagingRoot { get; }

    string SourceDir(Guid sourceId);

    string SkillDir(Guid sourceId, string relativeDir);
}

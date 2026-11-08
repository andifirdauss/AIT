using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// The check behind the Claude Code plugin's hooks. It runs before every prompt and file edit, so it only reads the
// project list and the node pipeline and never calls a model.
public interface ISkillGateService
{
    Task<SkillGateDto> GetAsync(string folder, CancellationToken cancellationToken = default);
}

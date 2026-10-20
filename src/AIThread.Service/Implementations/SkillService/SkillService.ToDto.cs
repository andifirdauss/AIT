using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    private SkillSourceDto ToDto(SkillSource s) =>
        new(s.Id, s.Url, s.Name, s.Kind, s.Ref, s.SubPath, s.Status, s.Error, s.SkillCount, s.LastPulledAt, s.CreatedAt, progress.Get(s.Id));
}

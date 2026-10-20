using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static BusinessSettingsDto ToSettingsDto(RuntimeSettings s) => new(
        s.BusinessMemoryEnabled, s.BusinessMemoryIntervalHours, s.BusinessMemoryModel, s.DeveloperName, s.EffectiveDeveloperName,
        GitIdentity.UserName);
}

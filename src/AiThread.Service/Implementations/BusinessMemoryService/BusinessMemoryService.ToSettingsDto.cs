using AiThread.Common.DTOs;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static BusinessSettingsDto ToSettingsDto(RuntimeSettings s) => new(
        s.BusinessMemoryEnabled, s.BusinessMemoryIntervalHours, s.BusinessMemoryModel, s.DeveloperName, s.EffectiveDeveloperName,
        GitIdentity.UserName);
}

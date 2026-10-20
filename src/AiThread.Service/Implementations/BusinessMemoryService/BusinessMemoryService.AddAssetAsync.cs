using AiThread.Common.DTOs;
using AiThread.Service.Business;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessAssetDto> AddAssetAsync(Guid projectId, Guid? pageId, byte[] data, string? caption, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var asset = await pages.AddAssetAsync(project.ProjectKey, pageId, data, caption, null, settings.EffectiveDeveloperName, cancellationToken);
        return new BusinessAssetDto(asset.Id, asset.MediaType, asset.Caption, BusinessMarkdown.AssetRoute + asset.Id, data.Length);
    }
}

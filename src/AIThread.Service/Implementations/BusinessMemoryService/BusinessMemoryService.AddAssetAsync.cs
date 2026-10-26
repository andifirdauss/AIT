using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Business;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessAssetDto> AddAssetAsync(Guid projectId, Guid? pageId, byte[] data, string? caption, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var asset = await pages.AddAssetAsync(project.ProjectKey, pageId, data, caption, null, settings.EffectiveDeveloperName, cancellationToken);
        return new BusinessAssetDto(asset.Id, asset.MediaType, asset.Caption, BusinessMarkdown.AssetRoute + asset.Id, data.Length);
    }
}

using System.Text.Json.Serialization;
using System.Text.Json;
using AIThread.API.Infrastructure;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Menu Setup: the first-run checklist, the actions that complete its steps in place, and the team profile.
[ApiController]
[Route("api/setup")]
public class SetupAssistantController(ISetupService setup, ISetupProfileService profiles) : ControllerBase
{
    private static readonly JsonSerializerOptions ProfileJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<SetupStatusDto>>> Status([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        ApiResponse<SetupStatusDto>.Ok(await setup.GetStatusAsync(ProxyUrlResolver.Resolve(HttpContext), refresh, cancellationToken));

    [HttpPost("router")]
    public async Task<ActionResult<ApiResponse<SetupRouterResultDto>>> SaveRouter(SetupRouterRequest request, CancellationToken cancellationToken)
    {
        var result = await setup.SaveRouterAsync(request, cancellationToken);
        return ApiResponse<SetupRouterResultDto>.Ok(result, result.Message);
    }

    [HttpPost("preset")]
    public async Task<ActionResult<ApiResponse<SetupPresetResultDto>>> Preset(SetupPresetRequest request, CancellationToken cancellationToken)
    {
        var result = await setup.ApplyPresetAsync(request, cancellationToken);
        var created = result.Nodes.Count(n => n.Created);
        return ApiResponse<SetupPresetResultDto>.Ok(result, Lang.T($"{created} node dibuat, {result.Nodes.Count - created} diperbarui.", $"{created} node(s) created, {result.Nodes.Count - created} updated."));
    }

    // The team profile as a file to download and hand to the other developers.
    [HttpGet("profile")]
    public async Task<IActionResult> ExportProfile(CancellationToken cancellationToken)
    {
        var file = await profiles.ExportAsync(cancellationToken);
        return File(JsonSerializer.SerializeToUtf8Bytes(file, ProfileJson), "application/json", $"ait-setup-{DateTime.Now:yyyyMMdd-HHmm}.json");
    }

    // DryRun (the default) only reports what would change and which secrets the profile needs.
    [HttpPost("profile/import")]
    public async Task<ActionResult<ApiResponse<SetupProfileImportResultDto>>> ImportProfile(SetupProfileImportRequest request, CancellationToken cancellationToken)
    {
        var result = await profiles.ImportAsync(request, cancellationToken);
        var message = result.DryRun ? null
            : result.Errors.Count > 0 ? Lang.T($"Profil diimpor sebagian: {result.Errors.Count} bagian gagal.", $"Profile partly imported: {result.Errors.Count} part(s) failed.")
            : Lang.T($"Profil diimpor: {result.Changes.Count} perubahan.", $"Profile imported: {result.Changes.Count} change(s).");
        return ApiResponse<SetupProfileImportResultDto>.Ok(result, message);
    }
}

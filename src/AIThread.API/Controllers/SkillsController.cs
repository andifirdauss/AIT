using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Skill tab of the Setting page (orchestrator model, sources, store) and the skill suggestions of the Node form.
[ApiController]
[Route("api/skills")]
public class SkillsController(ISkillService skillService, ISkillOrchestrator orchestrator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SkillDto>>>> GetAll([FromQuery] string? query, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<SkillDto>>.Ok(await skillService.GetSkillsAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SkillDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await skillService.GetSkillAsync(id, cancellationToken) is { } skill
            ? ApiResponse<SkillDetailDto>.Ok(skill)
            : NotFound(ApiResponse<SkillDetailDto>.Fail(Lang.T("Skill tidak ditemukan.", "Skill not found.")));

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<SkillSettingsDto>>> GetSettings(CancellationToken cancellationToken) =>
        ApiResponse<SkillSettingsDto>.Ok(await skillService.GetSettingsAsync(cancellationToken));

    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<SkillSettingsDto>>> UpdateSettings(UpdateSkillSettingsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<SkillSettingsDto>.Ok(await skillService.UpdateSettingsAsync(request, cancellationToken), Lang.T("Setting Skill tersimpan.", "Skill settings saved."));

    [HttpGet("sources")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SkillSourceDto>>>> GetSources(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<SkillSourceDto>>.Ok(await skillService.GetSourcesAsync(cancellationToken));

    [HttpPost("sources")]
    public async Task<ActionResult<ApiResponse<SkillSourceDto>>> AddSource(AddSkillSourceRequest request, CancellationToken cancellationToken) =>
        ApiResponse<SkillSourceDto>.Ok(await skillService.AddSourceAsync(request, cancellationToken), Lang.T("Link ditambahkan — skill sedang ditarik.", "Link added — the skills are being pulled."));

    [HttpPost("sources/{id:guid}/sync")]
    public async Task<ActionResult<ApiResponse<SkillSourceDto>>> Sync(Guid id, CancellationToken cancellationToken) =>
        await skillService.SyncSourceAsync(id, cancellationToken) is { } source
            ? ApiResponse<SkillSourceDto>.Ok(source, Lang.T("Sumber masuk antrean untuk ditarik ulang.", "Source queued to be pulled again."))
            : NotFound(ApiResponse<SkillSourceDto>.Fail(Lang.T("Sumber skill tidak ditemukan.", "Skill source not found.")));

    [HttpDelete("sources/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteSource(Guid id, CancellationToken cancellationToken) =>
        await skillService.DeleteSourceAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Sumber skill dihapus.", "Skill source deleted."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Sumber skill tidak ditemukan.", "Skill source not found.")));

    [HttpPost("suggest")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SkillSuggestionDto>>>> Suggest(SuggestSkillsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<SkillSuggestionDto>>.Ok(await orchestrator.SuggestAsync(request, cancellationToken));
}

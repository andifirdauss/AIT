using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Multi-step test scenarios of a project (Project > Detail → Skenario test) and their Excel sheets.
[ApiController]
public class TestScenariosController(ITestScenarioService scenarios, IThreadOrchestrator orchestrator) : ControllerBase
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("api/projects/{projectId:guid}/test-scenarios")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TestingScenarioDto>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<TestingScenarioDto>>.Ok(await scenarios.ListAsync(projectId, cancellationToken));

    // The scenario of a tester's suggestion (the same one when it was made before).
    [HttpPost("api/threads/{threadId:guid}/follow-ups/{number:int}/scenario")]
    public async Task<ActionResult<ApiResponse<TestingScenarioDto>>> FromFollowUp(Guid threadId, int number, CancellationToken cancellationToken)
    {
        var scenario = await scenarios.CreateFromFollowUpAsync(threadId, number, cancellationToken);
        return ApiResponse<TestingScenarioDto>.Ok(scenario, Lang.T($"Skenario \"{scenario.Title}\" siap diunduh.", $"Scenario \"{scenario.Title}\" ready to download."));
    }

    [HttpGet("api/projects/{projectId:guid}/test-scenarios/{scenarioId:guid}/excel")]
    public async Task<IActionResult> Excel(Guid projectId, Guid scenarioId, CancellationToken cancellationToken)
    {
        var file = await scenarios.ExcelAsync(projectId, scenarioId, cancellationToken);
        return File(file.Content, ExcelType, file.FileName);
    }

    // The filled sheet (multipart, field "file"); rows that cannot be used come back as a 400 listing them.
    [HttpPost("api/projects/{projectId:guid}/test-scenarios/import")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<ScenarioImportResultDto>>> Import(Guid projectId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) throw new AppValidationException(Lang.T("Pilih file skenario (.xlsx) yang sudah diisi.", "Choose a filled-in scenario file (.xlsx)."));
        await using var content = file.OpenReadStream();
        var result = await scenarios.ImportAsync(projectId, content, cancellationToken);
        return ApiResponse<ScenarioImportResultDto>.Ok(result, Lang.T($"Skenario \"{result.Scenario.Title}\" versi {result.Scenario.Version} disimpan", $"Scenario \"{result.Scenario.Title}\" version {result.Scenario.Version} saved") +
                                                              (result.AccountsAdded.Count == 0 ? "." : Lang.T($"; akun uji baru: {string.Join(", ", result.AccountsAdded)}.", $"; new test accounts: {string.Join(", ", result.AccountsAdded)}.")));
    }

    // Runs it in the background on the thread it came from; the thread page follows it over SSE. Returns that thread's id.
    [HttpPost("api/projects/{projectId:guid}/test-scenarios/{scenarioId:guid}/run")]
    public async Task<ActionResult<ApiResponse<Guid>>> Run(Guid projectId, Guid scenarioId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RunScenarioRequest? request,
        CancellationToken cancellationToken) =>
        ApiResponse<Guid>.Ok(await orchestrator.ScenarioInBackgroundAsync(projectId, scenarioId, request?.Acknowledge ?? false, cancellationToken), Lang.T("Skenario dijalankan.", "Scenario started."));

    [HttpDelete("api/projects/{projectId:guid}/test-scenarios/{scenarioId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid projectId, Guid scenarioId, CancellationToken cancellationToken)
    {
        await scenarios.DeleteAsync(projectId, scenarioId, cancellationToken);
        return ApiResponse<bool>.Ok(true, Lang.T("Skenario dihapus.", "Scenario deleted."));
    }
}

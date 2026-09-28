using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/threads")]
public class ThreadsController(IThreadService threadService, IThreadOrchestrator orchestrator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ThreadSummaryDto>>>> GetRecent([FromQuery] int take = 30, [FromQuery] Guid? projectId = null, CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<ThreadSummaryDto>>.Ok(await threadService.GetRecentAsync(take, projectId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ThreadDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await threadService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ThreadDetailDto>.Ok(detail)
            : NotFound(ApiResponse<ThreadDetailDto>.Fail("Thread tidak ditemukan."));

    // Runs a prompt from the dashboard against a registered project; progress arrives over SSE.
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Start(StartThreadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) throw new AppValidationException("Prompt wajib diisi.");

        var threadId = await orchestrator.StartInBackgroundAsync(new ThreadRequest(
            ThreadSource.Dashboard,
            request.Prompt.Trim(),
            PromptContext: null,
            ConversationContext: null,
            WorkingDirectory: null,
            SolutionHint: null,
            request.ProjectId,
            SessionId: null), cancellationToken);

        return ApiResponse<Guid>.Ok(threadId, "Thread dimulai.");
    }

    [HttpPost("{id:guid}/cancel")]
    public ActionResult<ApiResponse<bool>> Cancel(Guid id) =>
        threadService.Cancel(id)
            ? ApiResponse<bool>.Ok(true, "Pembatalan dikirim.")
            : NotFound(ApiResponse<bool>.Fail("Thread tidak sedang berjalan."));
}

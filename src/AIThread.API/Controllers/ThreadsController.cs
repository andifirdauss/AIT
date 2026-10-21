using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/threads")]
public class ThreadsController(IThreadService threadService, IThreadOrchestrator orchestrator, IThreadRollback rollback) : ControllerBase
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
            request.ProjectId,
            SessionId: null), cancellationToken);

        return ApiResponse<Guid>.Ok(threadId, "Thread dimulai.");
    }

    [HttpPost("{id:guid}/cancel")]
    public ActionResult<ApiResponse<bool>> Cancel(Guid id) =>
        threadService.Cancel(id)
            ? ApiResponse<bool>.Ok(true, "Pembatalan dikirim.")
            : NotFound(ApiResponse<bool>.Fail("Thread tidak sedang berjalan."));

    // Runs the planned tasks that did not succeed again from the stored plan; progress arrives over SSE.
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retry(Guid id, CancellationToken cancellationToken) =>
        ApiResponse<Guid>.Ok(await orchestrator.RetryInBackgroundAsync(id, cancellationToken), "Tugas yang gagal dijalankan ulang.");

    // Runs the thread's Auto Testing again from its stored test plan, with the login typed on the thread page (never
    // stored); progress arrives over SSE.
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retest(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RetestRequest? request,
        CancellationToken cancellationToken)
    {
        var login = TestLogin.From(request?.Username, request?.Password);
        if (login is null && (!string.IsNullOrWhiteSpace(request?.Username) || !string.IsNullOrWhiteSpace(request?.Password)))
            throw new AppValidationException("Isi username dan password, atau kosongkan keduanya.");
        return ApiResponse<Guid>.Ok(await orchestrator.RetestInBackgroundAsync(id, login, cancellationToken), "Testing dijalankan ulang.");
    }

    // What undoing the thread's file changes would do, file by file, without touching anything.
    [HttpGet("{id:guid}/rollback")]
    public async Task<ActionResult<ApiResponse<RollbackPreviewDto>>> RollbackPreview(Guid id, CancellationToken cancellationToken) =>
        await rollback.PreviewAsync(id, cancellationToken) is { } preview
            ? ApiResponse<RollbackPreviewDto>.Ok(preview)
            : NotFound(ApiResponse<RollbackPreviewDto>.Fail("Thread tidak ditemukan."));

    // Undoes the thread's file changes; refused as a whole (400) when any file changed after the thread.
    [HttpPost("{id:guid}/rollback")]
    public async Task<ActionResult<ApiResponse<RollbackResultDto>>> Rollback(Guid id, CancellationToken cancellationToken)
    {
        var result = await rollback.RollbackAsync(id, cancellationToken);
        return ApiResponse<RollbackResultDto>.Ok(result, $"Perubahan dibatalkan: {result.Restored} file dikembalikan, {result.Deleted} file dihapus.");
    }
}

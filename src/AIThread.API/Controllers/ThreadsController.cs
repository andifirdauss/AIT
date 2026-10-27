using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/threads")]
public class ThreadsController(IThreadService threadService, IThreadOrchestrator orchestrator, IThreadRollback rollback, IGitChangeTracker gitChanges,
    IPendingQuestionStore questions, IPendingApprovalStore publishApprovals)
    : ControllerBase
{
    // What the thread waits for the developer to allow (a paused planner, or a commit/push of a finished thread); null
    // when nothing.
    [HttpGet("{id:guid}/approval")]
    public ActionResult<ApiResponse<PendingApprovalDto?>> GetApproval(Guid id)
    {
        var request = questions.Find(id)?.Question.Approval?.Request.ToDto(afterThread: false)
                      ?? publishApprovals.Find(id)?.Request.ToDto(afterThread: true);
        return ApiResponse<PendingApprovalDto?>.Ok(request);
    }

    // The developer's answer from the thread page: once, always or deny. The thread goes on in the background.
    [HttpPost("{id:guid}/approval")]
    public async Task<ActionResult<ApiResponse<Guid>>> Approve(Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        ApiResponse<Guid>.Ok(await orchestrator.ApproveInBackgroundAsync(id, request.Decision, cancellationToken),
            request.Decision == ApprovalDecision.Deny ? Lang.T("Izin ditolak.", "Permission denied.") : Lang.T("Izin diberikan.", "Permission given."));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ThreadSummaryDto>>>> GetRecent([FromQuery] int take = 30, [FromQuery] Guid? projectId = null, CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<ThreadSummaryDto>>.Ok(await threadService.GetRecentAsync(take, projectId, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ThreadDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await threadService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ThreadDetailDto>.Ok(detail)
            : NotFound(ApiResponse<ThreadDetailDto>.Fail(Lang.T("Thread tidak ditemukan.", "Thread not found.")));

    // Asks git again where the thread's change is now (committed, discarded, ...), instead of waiting for the watcher.
    [HttpPost("{id:guid}/git/refresh")]
    public async Task<ActionResult<ApiResponse<ThreadGitDto?>>> RefreshGit(Guid id, CancellationToken cancellationToken)
    {
        await gitChanges.RefreshThreadsAsync([id], cancellationToken);
        return await threadService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ThreadGitDto?>.Ok(detail.Git)
            : NotFound(ApiResponse<ThreadGitDto?>.Fail(Lang.T("Thread tidak ditemukan.", "Thread not found.")));
    }

    // Runs a prompt from the dashboard against a registered project; progress arrives over SSE.
    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Start(StartThreadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt)) throw new AppValidationException(Lang.T("Prompt wajib diisi.", "A prompt is required."));

        var threadId = await orchestrator.StartInBackgroundAsync(new ThreadRequest(
            ThreadSource.Dashboard,
            request.Prompt.Trim(),
            PromptContext: null,
            ConversationContext: null,
            WorkingDirectory: null,
            request.ProjectId,
            SessionId: null), cancellationToken);

        return ApiResponse<Guid>.Ok(threadId, Lang.T("Thread dimulai.", "Thread started."));
    }

    [HttpPost("{id:guid}/cancel")]
    public ActionResult<ApiResponse<bool>> Cancel(Guid id) =>
        threadService.Cancel(id)
            ? ApiResponse<bool>.Ok(true, Lang.T("Pembatalan dikirim.", "Cancellation sent."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Thread tidak sedang berjalan.", "The thread is not running.")));

    // Runs the planned tasks that did not succeed again from the stored plan; progress arrives over SSE.
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retry(Guid id, CancellationToken cancellationToken) =>
        ApiResponse<Guid>.Ok(await orchestrator.RetryInBackgroundAsync(id, cancellationToken), Lang.T("Tugas yang gagal dijalankan ulang.", "The failed tasks are running again."));

    // Runs the thread's Auto Testing again from its stored test plan, with the login typed on the thread page (never
    // stored); progress arrives over SSE.
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<ApiResponse<Guid>>> Retest(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RetestRequest? request,
        CancellationToken cancellationToken)
    {
        var login = TestLogin.From(request?.Username, request?.Password);
        if (login is null && (!string.IsNullOrWhiteSpace(request?.Username) || !string.IsNullOrWhiteSpace(request?.Password)))
            throw new AppValidationException(Lang.T("Isi username dan password, atau kosongkan keduanya.", "Fill in the username and password, or leave both empty."));
        return ApiResponse<Guid>.Ok(await orchestrator.RetestInBackgroundAsync(id, login, cancellationToken), Lang.T("Testing dijalankan ulang.", "Testing started again."));
    }

    // Runs one of the tester's suggested follow-up tests; progress arrives over SSE.
    [HttpPost("{id:guid}/follow-ups/{number:int}/run")]
    public async Task<ActionResult<ApiResponse<Guid>>> RunFollowUp(Guid id, int number, CancellationToken cancellationToken) =>
        ApiResponse<Guid>.Ok(await orchestrator.FollowUpInBackgroundAsync(id, number, cancellationToken), Lang.T($"Test lanjutan {number} dijalankan.", $"Follow-up test {number} started."));

    // What undoing the thread's file changes would do, file by file, without touching anything.
    [HttpGet("{id:guid}/rollback")]
    public async Task<ActionResult<ApiResponse<RollbackPreviewDto>>> RollbackPreview(Guid id, CancellationToken cancellationToken) =>
        await rollback.PreviewAsync(id, cancellationToken) is { } preview
            ? ApiResponse<RollbackPreviewDto>.Ok(preview)
            : NotFound(ApiResponse<RollbackPreviewDto>.Fail(Lang.T("Thread tidak ditemukan.", "Thread not found.")));

    // Undoes the thread's file changes; refused as a whole (400) when any file changed after the thread.
    [HttpPost("{id:guid}/rollback")]
    public async Task<ActionResult<ApiResponse<RollbackResultDto>>> Rollback(Guid id, CancellationToken cancellationToken)
    {
        var result = await rollback.RollbackAsync(id, cancellationToken);
        return ApiResponse<RollbackResultDto>.Ok(result, Lang.T($"Perubahan dibatalkan: {result.Restored} file dikembalikan, {result.Deleted} file dihapus.", $"Changes rolled back: {result.Restored} file(s) restored, {result.Deleted} file(s) deleted."));
    }
}

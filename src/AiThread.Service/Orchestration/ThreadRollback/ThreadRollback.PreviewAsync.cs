using AiThread.Common.DTOs;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadRollback
{
    // Null when the thread does not exist.
    public async Task<RollbackPreviewDto?> PreviewAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(threadId, cancellationToken);
        if (state is null) return null;

        var reason = Refusal(state);
        var files = state.Root is null ? [] : Evaluate(state);
        reason ??= ConflictReason(files);
        return new RollbackPreviewDto(reason is null, reason, files);
    }
}

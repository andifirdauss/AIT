using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private async Task<PreparedThread?> ResumeAsync(PendingQuestion pending, ThreadRequest request, CancellationToken cancellationToken)
    {
        ThreadRun? thread;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            thread = await db.Threads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == pending.ThreadId, cancellationToken);
        if (thread is not { Status: ThreadStatus.AwaitingInput }) return null;

        var project = await projectService.GetContextAsync(pending.ProjectId, cancellationToken);
        if (project is null) return null;

        // Wrapper, reviewer and context keeper see the original request together with the clarification.
        var clarified = request with
        {
            Prompt = $"{thread.Prompt}\n\n---\nAIT asked:{pending.Question.Question}\nDeveloper answered: {request.Prompt}"
        };
        activityLog.Info("thread", $"Jawaban untuk pertanyaan planner diterima: {Truncate(request.Prompt.ReplaceLineEndings(" "), 160)}", thread.Id, thread.ProjectId);
        return new PreparedThread(thread, project, clarified, pending, request.Prompt);
    }
}

using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private async Task<PreparedThread> PrepareAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(request.ProjectId, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan. Daftarkan ulang di menu Project.");

        var thread = new ThreadRun
        {
            ProjectId = project.ProjectId,
            Source = request.Source,
            SessionId = request.SessionId,
            WorkingDirectory = Truncate(request.WorkingDirectory ?? project.RootPath, 800),
            Prompt = request.Prompt,
            Status = ThreadStatus.Pending
        };

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.Threads.Add(thread);
            // Kept until the business wiki writer has summarized the thread: a screenshot may belong on a page.
            db.ThreadAttachments.AddRange(BusinessImages.FromPrompt(thread.Id, request.Images));
            await db.SaveChangesAsync(cancellationToken);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadCreated, new { thread.Id, thread.ProjectId, thread.Status }, thread.Id));
        activityLog.Info("thread",
            $"Thread baru dari {SourceLabel(request.Source)} · {project.Name}: {Truncate(request.Prompt.ReplaceLineEndings(" "), 160)}",
            thread.Id, thread.ProjectId);

        return new PreparedThread(thread, project, request);
    }
}

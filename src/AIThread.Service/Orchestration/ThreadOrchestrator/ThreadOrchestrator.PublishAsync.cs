using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Git;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The developer asked to commit and/or push (plan.Publish): after a verified change it runs at once when every
    // action has a standing grant; otherwise the answer ends with the permission request, answered by 1/2/3 in the chat
    // or on the thread page. blocker: why the change may not be published (a task failed, verification failed, ...).
    // A plan without tasks publishes what the earlier threads of the chat wrote.
    private async Task PublishAsync(ThreadRequest request, ProjectContext project, ThreadRun thread, ThreadPlan plan, RuntimeSettings settings, string? blocker,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        if (plan.Publish is not { } publish || !settings.GitPublishEnabled) return;
        if (blocker is not null)
        {
            await progress.FinalAsync(Lang.T($"\n\n📤 Commit/push tidak dijalankan: {blocker}", $"\n\n📤 Commit/push was not done: {blocker}"), cancellationToken);
            return;
        }

        var files = await PublishFilesAsync(thread, project, sessionWide: plan.Tasks.Count == 0, cancellationToken);
        var pullRequest = publish.PullRequest is { Title: { } title } asked ? new PublishPullRequest(title, asked.Body, asked.Base) : null;
        var (job, error) = await gitPublisher.PrepareAsync(files, new PublishRequest(publish.Commit, publish.Push, publish.Message, publish.Branch, pullRequest), cancellationToken);
        if (job is null)
        {
            await progress.FinalAsync(Lang.T($"\n\n📤 Commit/push tidak dijalankan: {error}.", $"\n\n📤 Commit/push was not done: {error}."), cancellationToken);
            return;
        }

        var items = job.Items;
        var granted = items.Count > 0;
        foreach (var item in items)
            if (!await approvals.UseAsync(project.ProjectId, item.Kind, item.Target, thread.Id, cancellationToken))
            {
                granted = false;
                break;
            }
        if (granted)
        {
            await progress.FinalAsync(await RunPublishAsync(job, project.ProjectId, thread.Id, cancellationToken), cancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(request.SessionId) && request.Source != ThreadSource.Dashboard)
        {
            await progress.FinalAsync(Lang.T("\n\n📤 Commit/push butuh izin developer, yang tidak bisa ditanyakan dari sini.", "\n\n📤 Commit/push needs the developer's permission, which cannot be asked from here."), cancellationToken);
            return;
        }

        var approval = new ApprovalRequest(items, Lang.T("Perubahan sudah terverifikasi; developer meminta commit/push.", "The change is verified; the developer asked for a commit/push."),
            AllowAlways: !job.PushesProtectedBranch);
        publishApprovals.Ask(request.SessionId ?? PendingQuestionStore.ThreadSession(thread.Id), project.ProjectId, thread.Id, approval, job);
        eventBus.Publish(AppEvent.Create(AppEventTypes.ApprovalRequested, new { ThreadId = thread.Id, project.ProjectId }, thread.Id));
        activityLog.Info("thread", Lang.T($"Commit/push menunggu izin developer: {string.Join(", ", items.Select(i => i.Target))}", $"Commit/push waits for the developer's permission: {string.Join(", ", items.Select(i => i.Target))}"), thread.Id, project.ProjectId);
        await progress.FinalAsync("\n\n" + approval.ToText(), cancellationToken);
    }
}

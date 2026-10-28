using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The developer's answer to a commit/push request: run it (and keep the grants for "always", unless the push goes to
    // a protected branch), or drop it. Returns the text for the developer.
    private async Task<string> AnswerPublishAsync(PendingPublish pending, ApprovalDecision decision, CancellationToken cancellationToken)
    {
        if (decision == ApprovalDecision.Deny)
        {
            activityLog.Info("thread", Lang.T("Developer menolak commit/push.", "The developer denied the commit/push."), pending.ThreadId, pending.ProjectId);
            return Lang.T("📤 Commit/push dibatalkan; perubahan tetap di working tree.", "📤 Commit/push cancelled; the change stays in the working tree.");
        }

        var note = string.Empty;
        // A commit/push has no pattern nor command: "always allow the pattern" and "allow all commands" count as "always".
        if (decision is ApprovalDecision.Always or ApprovalDecision.AlwaysPattern or ApprovalDecision.AlwaysAll)
        {
            if (pending.Request.AllowAlways)
                foreach (var item in pending.Request.Items)
                    await approvals.GrantAsync(pending.ProjectId, item.Kind, item.Target, pending.ThreadId, cancellationToken);
            else
                note = Lang.T("\n\n_Push ke branch utama/rilis selalu ditanyakan; izin ini hanya untuk sekali._", "\n\n_A push to the main or a release branch is always asked; this permission counts once._");
        }
        return (await RunPublishAsync(pending.Job, pending.ProjectId, pending.ThreadId, cancellationToken)).TrimStart('\n') + note;
    }
}

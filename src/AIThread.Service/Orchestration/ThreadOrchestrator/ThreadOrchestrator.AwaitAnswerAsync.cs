using System.Diagnostics;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The planner asked the developer something: the question is the reply, and the thread waits for the next prompt of
    // the session. The database is updated before the question is registered, so an instant answer finds it waiting.
    private async Task<ThreadResult> AwaitAnswerAsync(
        ThreadRecorder recorder,
        ClientProgress progress,
        ProjectContext project,
        ThreadRequest request,
        PlanningResult planning,
        PlannerPause pause,
        Stopwatch planClock,
        Stopwatch total,
        CancellationToken cancellationToken)
    {
        var thread = recorder.Thread;
        var text = pause.Question.ToText();
        await progress.FinalAsync(text, cancellationToken);
        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.AwaitingInput;
            t.PlannerModel = planning.Node.Model;
            t.PlannerSteps = planning.Steps;
            t.PlannerTokens = planning.Tokens;
            t.PlannerCachedTokens = planning.CachedTokens;
            t.ToolsNodeTokens = planning.ToolsNodeTokens;
            t.InvestigationJson = recorder.Investigation.ToJson();
            t.PlanningMs = (t.PlanningMs ?? 0) + planClock.ElapsedMilliseconds;
            t.FinalResponse = text;
            t.TotalMs = (t.TotalMs ?? 0) + total.ElapsedMilliseconds;
        });
        // A dashboard thread has no session: its permission requests are answered on the thread page, under its own key.
        questions.Add(thread.Id, project.ProjectId, request.SessionId ?? PendingQuestionStore.ThreadSession(thread.Id), pause.Question, pause.Conversation);
        if (pause.Question.Approval is not null)
            eventBus.Publish(AppEvent.Create(AppEventTypes.ApprovalRequested, new { ThreadId = thread.Id, project.ProjectId }, thread.Id));
        recorder.Log(ActivityLevel.Info, Lang.T($"Planner bertanya ke developer; thread menunggu jawaban: {Truncate(pause.Question.Question.ReplaceLineEndings(" "), 200)}", $"The planner asked the developer; the thread waits for the answer: {Truncate(pause.Question.Question.ReplaceLineEndings(" "), 200)}"));
        return new ThreadResult(thread.Id, ThreadStatus.AwaitingInput, progress.Transcript, TotalTokens(thread));
    }
}

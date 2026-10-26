using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static PlanningResult DirectAnswer(string text, PlannerRun run, AiNode node, ThreadRecorder recorder)
    {
        // The model insists on answering in prose: treat it as a direct answer.
        recorder.Log(ActivityLevel.Warning, Lang.T("Planner tidak memanggil submit_plan; jawaban teks dipakai sebagai jawaban langsung.", "The planner did not call submit_plan; its text answer is used as a direct answer."));
        return Finished(new ThreadPlan { Summary = Lang.T("Jawaban langsung dari planner.", "Direct answer from the planner."), Answer = text }, run, node);
    }
}

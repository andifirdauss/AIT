using AiThread.Common.Entities;
using AiThread.Common.Enums;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static PlanningResult DirectAnswer(string text, PlannerRun run, AiNode node, ThreadRecorder recorder)
    {
        // The model insists on answering in prose: treat it as a direct answer.
        recorder.Log(ActivityLevel.Warning, "Planner tidak memanggil submit_plan; jawaban teks dipakai sebagai jawaban langsung.");
        return Finished(new ThreadPlan { Summary = "Jawaban langsung dari planner.", Answer = text }, run, node);
    }
}

using System.Text.Json.Nodes;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static PlannerToolOutcome AskUser(JsonObject args, PlannerInput input, PlannerRun run)
    {
        if (!input.CanAsk)
            return new PlannerToolOutcome("Error: the developer cannot be asked here. Decide yourself and state your assumption in the summary.", null, true);
        if (run.Step >= run.MaxSteps)
            return new PlannerToolOutcome("Error: no steps left for a question. Call submit_plan with your best plan and state your assumptions in the summary.", null, true);
        if (run.QuestionsAsked >= MaxQuestions)
            return new PlannerToolOutcome($"Error: you already asked {run.QuestionsAsked} questions. Decide yourself and state your assumption in the summary.", null, true);

        var question = PlannerToolRunner.Str(args, "question");
        if (question is null) return new PlannerToolOutcome("Error: \"question\" is required.", null, true);

        var options = (args["options"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(o => (Label: PlannerToolRunner.Str(o, "label"), Description: PlannerToolRunner.Str(o, "description")))
            .Where(o => o.Label is not null)
            .Take(4)
            .Select(o => new QuestionOption(o.Label!, o.Description))
            .ToList();
        // Committed tasks already ran: the developer answers knowing that.
        if (input.Stream is { Executed: > 0 } stream)
            question += Lang.T($"\n\n_Catatan AIT: {stream.Executed} tugas yang sudah di-commit planner sudah dijalankan dan tetap ada; planner bisa menambah tugas untuk menyesuaikannya._",
                $"\n\n_AIT note: {stream.Executed} task(s) the planner committed have already run and stay; the planner can add tasks to adjust them._");
        return new PlannerToolOutcome(string.Empty, null, Question: new PlannerQuestion(question, options));
    }
}

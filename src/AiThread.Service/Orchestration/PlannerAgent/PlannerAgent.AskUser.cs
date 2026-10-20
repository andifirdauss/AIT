using System.Text.Json.Nodes;

namespace AiThread.Service.Orchestration;

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
        return new PlannerToolOutcome(string.Empty, null, Question: new PlannerQuestion(question, options));
    }
}

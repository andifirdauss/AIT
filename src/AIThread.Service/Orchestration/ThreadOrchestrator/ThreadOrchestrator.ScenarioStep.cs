using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A scenario step as the tester's single scenario: the instruction, then one line per form value the developer chose.
    private static TestScenario ScenarioStep(ScenarioStepDto step, string? username) => new()
    {
        Title = $"{step.Order}. {step.Action}" + (step.Role is null ? string.Empty : Lang.T($" oleh {step.Role}", $" as {step.Role}")),
        Actor = username ?? step.Role,
        Action = step.Action,
        Target = step.Target,
        Steps = [step.Instruction, .. step.Values.Select(v => Lang.T($"Isi {v.Key}: {v.Value}", $"Fill in {v.Key}: {v.Value}"))],
        Expect = step.Expect
    };
}

using AIThread.Common.DTOs;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A scenario step as the tester's single scenario: the instruction, then one line per form value the developer chose.
    private static TestScenario ScenarioStep(ScenarioStepDto step, string? username) => new()
    {
        Title = $"{step.Order}. {step.Action}" + (step.Role is null ? string.Empty : $" oleh {step.Role}"),
        Actor = username ?? step.Role,
        Action = step.Action,
        Target = step.Target,
        Steps = [step.Instruction, .. step.Values.Select(v => $"Isi {v.Key}: {v.Value}")],
        Expect = step.Expect
    };
}
